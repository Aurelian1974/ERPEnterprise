CREATE OR ALTER PROCEDURE fin.usp_CashDocument_Save
    @Id                int = NULL,
    @CompanyId         int,
    @TreasuryAccountId int,
    @Kind              tinyint,
    @DocumentDate      date,
    @PartnerId         int = NULL,
    @EmployeeId        int = NULL,
    @PersonName        nvarchar(200) = NULL,
    @PersonIdEncrypted varbinary(512) = NULL,
    @KeepPersonId      bit = 0,
    @ExchangeRate      decimal(19,6),
    @RateDate          date,
    @TotalAmount       decimal(19,2),
    @TotalAmountRon    decimal(19,2),
    @Description       nvarchar(500) = NULL,
    @Lines             fin.CashDocumentLineList READONLY,
    @RowVersion        binary(8) = NULL,
    @RequireAssignment bit,
    @UserId            int
AS
BEGIN
    -- Ciorna unui document de casa (02.02, F1 3.3 si 4.1) cu liniile ei (lista inlocuieste liniile). Casa e a firmei, activa; casierul
    -- e atribuit casei (51014), daca nu are dreptul pe toate casele. Liniile au sensul documentului (51008), iar totalurile sunt suma
    -- liniilor (51009); sumele in lei sunt calculate in C# (IPrecisionService, SP-13). Soldul initial nu are linii. Doar o ciorna se
    -- modifica (51003). Referintele altei firme sau inactive: 51021.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @AccountCompanyId int, @AccountKind tinyint, @AccountActive bit, @Currency char(3);
    SELECT @AccountCompanyId = CompanyId, @AccountKind = Kind, @AccountActive = IsActive, @Currency = CurrencyCode
    FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId;

    IF @AccountCompanyId IS NULL OR @AccountCompanyId <> @CompanyId
        THROW 51023, N'Casa nu există în firma curentă.', 1;
    IF @AccountKind <> 2 OR @AccountActive = 0
        THROW 51021, N'Documentele de casă se fac doar pe o casă activă.', 1;
    IF @RequireAssignment = 1 AND NOT EXISTS (
            SELECT 1 FROM fin.TreasuryAccountUser
            WHERE TreasuryAccountId = @TreasuryAccountId AND UserId = @UserId
              AND (ValidFrom IS NULL OR ValidFrom <= @DocumentDate) AND (ValidTo IS NULL OR ValidTo >= @DocumentDate))
        THROW 51014, N'Nu sunteți atribuit acestei case.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @CurrentStatus tinyint, @CurrentCompanyId int, @CurrentVersion binary(8), @CurrentAccountId int;
        SELECT @CurrentStatus = Status, @CurrentCompanyId = CompanyId, @CurrentVersion = RowVersion, @CurrentAccountId = TreasuryAccountId
        FROM fin.CashDocument WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @CurrentStatus IS NULL OR @CurrentCompanyId <> @CompanyId
            THROW 51001, N'Documentul de casă nu există.', 1;
        IF @CurrentVersion <> @RowVersion
            THROW 51002, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @CurrentStatus <> 1
            THROW 51003, N'Doar o ciornă se poate modifica.', 1;
        IF @RequireAssignment = 1 AND @CurrentAccountId <> @TreasuryAccountId AND NOT EXISTS (
                SELECT 1 FROM fin.TreasuryAccountUser WHERE TreasuryAccountId = @CurrentAccountId AND UserId = @UserId)
            THROW 51014, N'Nu sunteți atribuit casei documentului.', 1;
    END;

    IF @Kind NOT IN (1, 2, 3, 4)
        THROW 51021, N'Felul documentului de casă nu este valid.', 1;
    IF @Kind = 1 AND @PartnerId IS NULL AND @PersonName IS NULL
        THROW 51021, N'Chitanța are obligatoriu un plătitor: partener sau nume și act de identitate.', 1;
    IF @PartnerId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId AND IsActive = 1)
        THROW 51021, N'Partenerul nu există sau este inactiv.', 1;
    IF @EmployeeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @EmployeeId AND CompanyId = @CompanyId)
        THROW 51021, N'Angajatul nu aparține firmei.', 1;

    IF @Kind = 4
    BEGIN
        IF EXISTS (SELECT 1 FROM @Lines)
            THROW 51021, N'Soldul inițial nu are linii.', 1;
        IF @TotalAmount < 0 OR @TotalAmountRon < 0
            THROW 51021, N'Soldul inițial nu poate fi negativ.', 1;
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM @Lines)
            THROW 51021, N'Documentul are cel puțin o linie.', 1;
        IF EXISTS (SELECT 1 FROM @Lines WHERE Amount <= 0)
            THROW 51021, N'Sumele liniilor sunt pozitive; corecțiile se fac prin storno.', 1;
        IF EXISTS (SELECT 1 FROM @Lines AS l
                   LEFT JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId AND t.IsActive = 1 AND t.AppliesToCash = 1
                   WHERE t.Id IS NULL)
            THROW 51021, N'Tipul de operațiune nu există, este inactiv sau nu se folosește la casă.', 1;
        IF EXISTS (SELECT 1 FROM @Lines AS l JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
                   WHERE t.Direction <> CASE WHEN @Kind = 3 THEN 2 ELSE 1 END)
            THROW 51008, N'Toate liniile au sensul documentului: încasări pe chitanță și dispoziție de încasare, plăți pe dispoziție de plată.', 1;
        IF EXISTS (SELECT 1 FROM @Lines AS l JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
                   WHERE (t.RequiresPartner = 1 AND @PartnerId IS NULL AND @PersonName IS NULL)
                      OR (t.RequiresEmployee = 1 AND @EmployeeId IS NULL)
                      OR (t.RequiresCounterAccount = 1 AND l.CounterTreasuryAccountId IS NULL))
            THROW 51021, N'Tipul de operațiune cere partenerul, angajatul sau contul de trezorerie corespondent.', 1;
        IF EXISTS (SELECT 1 FROM @Lines AS l
                   WHERE l.CounterTreasuryAccountId IS NOT NULL
                     AND (l.CounterTreasuryAccountId = @TreasuryAccountId
                          OR NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount AS a WHERE a.Id = l.CounterTreasuryAccountId AND a.CompanyId = @CompanyId
                                         AND a.IsActive = 1)))
            THROW 51021, N'Contul corespondent trebuie să fie alt cont activ al firmei.', 1;
        IF EXISTS (SELECT 1 FROM @Lines AS l
                   WHERE (l.CostCenterId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue AS d
                            WHERE d.Id = l.CostCenterId AND d.CompanyId = @CompanyId AND d.DimensionType = 'COST_CENTER'))
                      OR (l.ProjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue AS d
                            WHERE d.Id = l.ProjectId AND d.CompanyId = @CompanyId AND d.DimensionType = 'PROJECT')))
            THROW 51021, N'Centrul de cost sau proiectul nu aparține firmei.', 1;
        IF @TotalAmount <> (SELECT SUM(Amount) FROM @Lines) OR @TotalAmountRon <> (SELECT SUM(AmountRon) FROM @Lines)
            THROW 51009, N'Totalul documentului diferă de suma liniilor.', 1;
    END;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.CashDocument (CompanyId, TreasuryAccountId, Kind, DocumentDate, PartnerId, EmployeeId, PersonName, PersonIdEncrypted,
            CurrencyCode, ExchangeRate, RateDate, TotalAmount, TotalAmountRon, Description, CreatedBy)
        VALUES (@CompanyId, @TreasuryAccountId, @Kind, @DocumentDate, @PartnerId, @EmployeeId, @PersonName, @PersonIdEncrypted,
            @Currency, @ExchangeRate, @RateDate, @TotalAmount, @TotalAmountRon, @Description, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE fin.CashDocument
        SET TreasuryAccountId = @TreasuryAccountId, Kind = @Kind, DocumentDate = @DocumentDate, PartnerId = @PartnerId, EmployeeId = @EmployeeId,
            PersonName = @PersonName,
            PersonIdEncrypted = CASE WHEN @PersonName IS NULL THEN NULL WHEN @KeepPersonId = 1 THEN PersonIdEncrypted ELSE @PersonIdEncrypted END,
            CurrencyCode = @Currency, ExchangeRate = @ExchangeRate, RateDate = @RateDate, TotalAmount = @TotalAmount,
            TotalAmountRon = @TotalAmountRon, Description = @Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;

        DELETE FROM fin.CashDocumentLine WHERE CashDocumentId = @Id;
    END;

    INSERT INTO fin.CashDocumentLine (CashDocumentId, LineNumber, TreasuryOperationTypeId, Amount, AmountRon, CounterTreasuryAccountId, SettlementRef,
        CostCenterId, ProjectId, Note)
    SELECT @Id, LineNumber, TreasuryOperationTypeId, Amount, AmountRon, CounterTreasuryAccountId, SettlementRef, CostCenterId, ProjectId, Note
    FROM @Lines;

    SELECT Id, RowVersion FROM fin.CashDocument WHERE Id = @Id;
END;
