CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccount_Save
    @Id                   int = NULL,
    @CompanyId            int,
    @Kind                 tinyint,
    @Code                 nvarchar(30),
    @Name                 nvarchar(200),
    @BranchId             int = NULL,
    @CurrencyCode         char(3),
    @CompanyBankAccountId int = NULL,
    @GlAccountCode        nvarchar(20) = NULL,
    @ResponsibleUserId    int = NULL,
    @ReceiptSeriesId      int = NULL,
    @CashInSeriesId       int = NULL,
    @CashOutSeriesId      int = NULL,
    @MaxClosingBalance    decimal(19,2) = NULL,
    @CloseMode            tinyint,
    @OpeningDate          date = NULL,
    @IsActive             bit,
    @Cashiers             fin.TreasuryCashierList READONLY,
    @RowVersion           binary(8) = NULL,
    @UserId               int
AS
BEGIN
    -- Contul de trezorerie al firmei curente (02.02, F1 3.1; macheta f1-01) si casierii lui (lista inlocuieste atribuirile).
    -- Firma si tipul nu se schimba dupa creare. Referintele (punct de lucru, cont bancar, gestionar, serii, casieri) trebuie sa fie ale
    -- firmei si active: 51021. Cod duplicat pe firma: 51022. Contul altei firme sau inexistent: 51023. Versiune diferita: 51002.
    -- Moneda blocata dupa primul document (51015) si dezactivarea cu sold (51016) se verifica odata cu documentele de casa.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @CurrentCompanyId int, @CurrentKind tinyint;
    IF @Id IS NOT NULL
    BEGIN
        SELECT @CurrentCompanyId = CompanyId, @CurrentKind = Kind FROM fin.TreasuryAccount WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @CurrentCompanyId IS NULL OR @CurrentCompanyId <> @CompanyId
            THROW 51023, N'Contul de trezorerie nu există în firma curentă.', 1;
        IF @CurrentKind <> @Kind
            THROW 51021, N'Tipul contului nu se schimbă după creare.', 1;
    END;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId AND IsActive = 1)
        THROW 51021, N'Firma nu există sau este inactivă.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Currency WHERE Code = @CurrencyCode AND (IsActive = 1 OR Code = 'RON'))
        THROW 51021, N'Moneda nu există sau nu este activă.', 1;
    IF @BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @BranchId AND CompanyId = @CompanyId AND IsActive = 1)
        THROW 51021, N'Punctul de lucru nu aparține firmei sau este inactiv.', 1;
    IF @Kind IN (1, 3) AND NOT EXISTS (SELECT 1 FROM core.CompanyBankAccount
                                      WHERE Id = @CompanyBankAccountId AND CompanyId = @CompanyId AND IsActive = 1 AND Currency = @CurrencyCode)
        THROW 51021, N'Contul bancar trebuie să fie al firmei, activ și în moneda contului de trezorerie.', 1;
    IF @ResponsibleUserId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.[User] WHERE Id = @ResponsibleUserId AND IsActive = 1 AND IsSystem = 0)
        THROW 51021, N'Gestionarul ales nu există sau este inactiv.', 1;
    IF EXISTS (SELECT 1
               FROM (VALUES (@ReceiptSeriesId, N'Finance.Receipt'), (@CashInSeriesId, N'Finance.CashIn'), (@CashOutSeriesId, N'Finance.Payment'))
                    AS v (SeriesId, DocumentType)
               WHERE v.SeriesId IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM core.NumberSeries AS s JOIN core.DocumentType AS t ON t.Id = s.DocumentTypeId
                                 WHERE s.Id = v.SeriesId AND s.CompanyId = @CompanyId AND t.Code = v.DocumentType))
        THROW 51021, N'Seriile trebuie să fie ale firmei și ale tipului de document potrivit.', 1;
    IF EXISTS (SELECT 1 FROM @Cashiers AS c
               WHERE NOT EXISTS (SELECT 1 FROM core.[User] AS u WHERE u.Id = c.UserId AND u.IsActive = 1 AND u.IsSystem = 0))
        THROW 51021, N'Un casier ales nu există sau este inactiv.', 1;

    IF EXISTS (SELECT 1 FROM fin.TreasuryAccount WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 51022, N'Codul contului de trezorerie există deja în firmă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.TreasuryAccount (CompanyId, BranchId, Code, Name, Kind, CurrencyCode, CompanyBankAccountId, GlAccountCode, ResponsibleUserId,
            ReceiptSeriesId, CashInSeriesId, CashOutSeriesId, MaxClosingBalance, CloseMode, OpeningDate, IsActive, CreatedBy)
        VALUES (@CompanyId, @BranchId, @Code, @Name, @Kind, @CurrencyCode, @CompanyBankAccountId, @GlAccountCode, @ResponsibleUserId,
            @ReceiptSeriesId, @CashInSeriesId, @CashOutSeriesId, @MaxClosingBalance, @CloseMode, @OpeningDate, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE fin.TreasuryAccount
        SET BranchId = @BranchId, Code = @Code, Name = @Name, CurrencyCode = @CurrencyCode, CompanyBankAccountId = @CompanyBankAccountId,
            GlAccountCode = @GlAccountCode, ResponsibleUserId = @ResponsibleUserId, ReceiptSeriesId = @ReceiptSeriesId,
            CashInSeriesId = @CashInSeriesId, CashOutSeriesId = @CashOutSeriesId, MaxClosingBalance = @MaxClosingBalance, CloseMode = @CloseMode,
            OpeningDate = @OpeningDate, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 51002, N'Contul de trezorerie a fost modificat de alt utilizator; reîncărcați datele.', 1;
    END;

    -- Casierii: lista primita inlocuieste atribuirile (doar casele au casieri; pentru celelalte tipuri lista vine goala).
    DELETE u FROM fin.TreasuryAccountUser AS u
    WHERE u.TreasuryAccountId = @Id AND NOT EXISTS (SELECT 1 FROM @Cashiers AS c WHERE c.UserId = u.UserId);

    UPDATE u
    SET CanValidate = c.CanValidate, ValidFrom = c.ValidFrom, ValidTo = c.ValidTo, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.TreasuryAccountUser AS u
    JOIN @Cashiers AS c ON c.UserId = u.UserId
    WHERE u.TreasuryAccountId = @Id
      AND (u.CanValidate <> c.CanValidate OR ISNULL(u.ValidFrom, '19000101') <> ISNULL(c.ValidFrom, '19000101')
           OR ISNULL(u.ValidTo, '19000101') <> ISNULL(c.ValidTo, '19000101'));

    INSERT INTO fin.TreasuryAccountUser (TreasuryAccountId, UserId, CanValidate, ValidFrom, ValidTo, CreatedBy)
    SELECT @Id, c.UserId, c.CanValidate, c.ValidFrom, c.ValidTo, @UserId
    FROM @Cashiers AS c
    WHERE NOT EXISTS (SELECT 1 FROM fin.TreasuryAccountUser AS u WHERE u.TreasuryAccountId = @Id AND u.UserId = c.UserId);

    SELECT Id, RowVersion FROM fin.TreasuryAccount WHERE Id = @Id;
END;
