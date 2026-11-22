CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_Save
    @Id                     int = NULL,
    @CompanyId              int,
    @TreasuryAccountId      int,
    @DocumentDate           date,
    @RequestedExecutionDate date,
    @IsUrgent               bit,
    @Origin                 tinyint,
    @Purpose                tinyint,
    @PartnerId              int = NULL,
    @EmployeeId             int = NULL,
    @PartnerBankAccountId   int = NULL,
    @BeneficiaryName        nvarchar(140) = NULL,
    @BeneficiaryIban        varchar(34) = NULL,
    @BeneficiaryBic         varchar(11) = NULL,
    @BeneficiaryFiscalCode  varchar(20) = NULL,
    @AllowFreeIban          bit,
    @RemittanceInfo         nvarchar(500) = NULL,
    @BudgetObligationRef    nvarchar(50) = NULL,
    @SourceProposalId       int = NULL,
    @Lines                  fin.PaymentOrderLineList READONLY,
    @RowVersion             binary(8) = NULL,
    @UserId                 int
AS
BEGIN
    -- Ciorna unui ordin de plata (02.02, F2 3.6) cu liniile ei (lista inlocuieste liniile). Contul sursa e al firmei (51023), activ, de tip
    -- Banca sau Card (51021); moneda e a contului. Beneficiarul: partenerul cu un cont bancar al lui (IBAN-ul si numele din fisa, regulile
    -- 1 si 5) sau, doar cu @AllowFreeIban, un IBAN tastat (51215 fara el). Liniile sunt plati bancare active, pozitive (51021); suma OP-ului
    -- e suma liniilor. Doar o ciorna se modifica (51212). EndToEndId = C<firma>-OP<Id>, unic.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @AccountCompanyId int, @Kind tinyint, @Active bit, @Currency char(3);
    SELECT @AccountCompanyId = CompanyId, @Kind = Kind, @Active = IsActive, @Currency = CurrencyCode FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId;
    IF @AccountCompanyId IS NULL OR @AccountCompanyId <> @CompanyId
        THROW 51023, N'Contul bancar nu există în firma curentă.', 1;
    IF @Kind NOT IN (1, 3) OR @Active = 0
        THROW 51021, N'Ordinele de plată se fac doar dintr-un cont bancar activ.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @CurrentStatus tinyint, @CurrentCompanyId int, @CurrentVersion binary(8);
        SELECT @CurrentStatus = Status, @CurrentCompanyId = CompanyId, @CurrentVersion = RowVersion FROM fin.PaymentOrder WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @CurrentStatus IS NULL OR @CurrentCompanyId <> @CompanyId
            THROW 51225, N'Ordinul de plată nu există.', 1;
        IF @CurrentVersion <> @RowVersion
            THROW 51202, N'Ordinul de plată a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @CurrentStatus <> 1
            THROW 51212, N'Un OP validat nu se mai modifică; se corectează prin storno.', 1;
    END;

    IF @PartnerId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId AND IsActive = 1)
        THROW 51021, N'Partenerul nu există sau este inactiv.', 1;
    IF @EmployeeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @EmployeeId AND CompanyId = @CompanyId)
        THROW 51021, N'Angajatul nu există în firma curentă.', 1;

    IF @PartnerBankAccountId IS NOT NULL
    BEGIN
        SELECT @BeneficiaryIban = b.Iban, @BeneficiaryName = p.Name
        FROM core.PartnerBankAccount AS b JOIN core.Partner AS p ON p.Id = b.PartnerId
        WHERE b.Id = @PartnerBankAccountId AND b.PartnerId = @PartnerId AND b.IsActive = 1;
        IF @@ROWCOUNT = 0
            THROW 51215, N'Contul bancar ales nu este un cont activ al partenerului.', 1;
    END
    ELSE IF @AllowFreeIban = 0
        THROW 51215, N'IBAN-ul beneficiarului se alege din fișa partenerului.', 1;

    IF @BeneficiaryIban IS NULL OR @BeneficiaryName IS NULL
        THROW 51215, N'Lipsește IBAN-ul sau numele beneficiarului.', 1;
    IF NOT EXISTS (SELECT 1 FROM @Lines)
        THROW 51221, N'Ordinul de plată nu are linii.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l LEFT JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
               WHERE t.Id IS NULL OR t.IsActive = 0 OR t.AppliesToBank = 0 OR t.Direction NOT IN (2, 3) OR l.Amount <= 0)
        THROW 51021, N'Liniile ordinului de plată sunt plăți bancare active, cu sume pozitive.', 1;

    DECLARE @Amount decimal(19,2) = (SELECT SUM(Amount) FROM @Lines);
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.PaymentOrder (CompanyId, TreasuryAccountId, DocumentDate, RequestedExecutionDate, IsUrgent, Origin, Purpose, PartnerId, EmployeeId,
            PartnerBankAccountId, BeneficiaryName, BeneficiaryIban, BeneficiaryBic, BeneficiaryFiscalCode, IsFreeIban, CurrencyCode, Amount, RemittanceInfo,
            BudgetObligationRef, SourceProposalId, CreatedBy)
        VALUES (@CompanyId, @TreasuryAccountId, @DocumentDate, @RequestedExecutionDate, @IsUrgent, @Origin, @Purpose, @PartnerId, @EmployeeId,
            @PartnerBankAccountId, @BeneficiaryName, @BeneficiaryIban, @BeneficiaryBic, @BeneficiaryFiscalCode,
            CASE WHEN @PartnerBankAccountId IS NULL THEN 1 ELSE 0 END, @Currency, @Amount, @RemittanceInfo, @BudgetObligationRef, @SourceProposalId, @UserId);
        SET @Id = SCOPE_IDENTITY();
        UPDATE fin.PaymentOrder SET EndToEndId = CONCAT('C', @CompanyId, '-OP', @Id) WHERE Id = @Id;
    END
    ELSE
    BEGIN
        UPDATE fin.PaymentOrder
        SET TreasuryAccountId = @TreasuryAccountId, DocumentDate = @DocumentDate, RequestedExecutionDate = @RequestedExecutionDate, IsUrgent = @IsUrgent,
            Purpose = @Purpose, PartnerId = @PartnerId, EmployeeId = @EmployeeId, PartnerBankAccountId = @PartnerBankAccountId,
            BeneficiaryName = @BeneficiaryName, BeneficiaryIban = @BeneficiaryIban, BeneficiaryBic = @BeneficiaryBic,
            BeneficiaryFiscalCode = @BeneficiaryFiscalCode, IsFreeIban = CASE WHEN @PartnerBankAccountId IS NULL THEN 1 ELSE 0 END, CurrencyCode = @Currency,
            Amount = @Amount, RemittanceInfo = @RemittanceInfo, BudgetObligationRef = @BudgetObligationRef, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM fin.PaymentOrderLine WHERE PaymentOrderId = @Id;
    END;

    INSERT INTO fin.PaymentOrderLine (PaymentOrderId, LineNumber, TreasuryOperationTypeId, SettlementRef, Amount, CostCenterId, ProjectId)
    SELECT @Id, LineNumber, TreasuryOperationTypeId, SettlementRef, Amount, CostCenterId, ProjectId FROM @Lines;

    SELECT Id, RowVersion FROM fin.PaymentOrder WHERE Id = @Id;
END;
