CREATE OR ALTER PROCEDURE fin.usp_MatchingRule_Save
    @Id                     int = NULL,
    @CompanyId              int,
    @Priority               int,
    @Name                   nvarchar(200),
    @TreasuryAccountId      int = NULL,
    @Direction              tinyint = NULL,
    @CounterpartyIban       varchar(34) = NULL,
    @CounterpartyFiscalCode varchar(20) = NULL,
    @RemittanceContains     nvarchar(200) = NULL,
    @BankTransactionCode    varchar(35) = NULL,
    @AmountFrom             decimal(19,2) = NULL,
    @AmountTo               decimal(19,2) = NULL,
    @TreasuryOperationTypeId int,
    @PartnerId              int = NULL,
    @CostCenterId           int = NULL,
    @ProjectId              int = NULL,
    @NoteTemplate           nvarchar(500) = NULL,
    @AutoValidate           bit,
    @CreatedFromLineId      bigint = NULL,
    @IsActive               bit,
    @RowVersion             binary(8) = NULL,
    @UserId                 int
AS
BEGIN
    -- Salveaza o regula de potrivire (02.02, F2 3.5). Contul si linia sursa sunt ale firmei, tipul e bancar si activ (51021); validarea
    -- automata doar pentru tipurile fara partener (comisioane, dobanzi; 51021). Versiune diferita: 51202.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @TreasuryAccountId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId)
        THROW 51023, N'Contul de trezorerie nu există în firma curentă.', 1;
    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryOperationType WHERE Id = @TreasuryOperationTypeId AND IsActive = 1 AND AppliesToBank = 1)
        THROW 51021, N'Tipul de operațiune nu există, este inactiv sau nu se folosește la bancă.', 1;
    IF @AutoValidate = 1 AND EXISTS (SELECT 1 FROM fin.TreasuryOperationType WHERE Id = @TreasuryOperationTypeId AND RequiresPartner = 1)
        THROW 51021, N'Validarea automată se poate cere doar pentru tipuri fără partener (comisioane, dobânzi).', 1;
    IF @PartnerId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
        THROW 51021, N'Partenerul nu există.', 1;
    IF @CreatedFromLineId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fin.BankStatementLine AS l JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId
                                                      WHERE l.Id = @CreatedFromLineId AND s.CompanyId = @CompanyId)
        THROW 51201, N'Linia de extras nu există.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.MatchingRule (CompanyId, Priority, Name, TreasuryAccountId, Direction, CounterpartyIban, CounterpartyFiscalCode, RemittanceContains,
            BankTransactionCode, AmountFrom, AmountTo, TreasuryOperationTypeId, PartnerId, CostCenterId, ProjectId, NoteTemplate, AutoValidate,
            CreatedFromLineId, IsActive, CreatedBy)
        VALUES (@CompanyId, @Priority, @Name, @TreasuryAccountId, @Direction, @CounterpartyIban, @CounterpartyFiscalCode, @RemittanceContains,
            @BankTransactionCode, @AmountFrom, @AmountTo, @TreasuryOperationTypeId, @PartnerId, @CostCenterId, @ProjectId, @NoteTemplate, @AutoValidate,
            @CreatedFromLineId, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @RuleCompanyId int, @Version binary(8);
        SELECT @RuleCompanyId = CompanyId, @Version = RowVersion FROM fin.MatchingRule WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @RuleCompanyId IS NULL OR @RuleCompanyId <> @CompanyId
            THROW 51021, N'Regula nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51202, N'Regula a fost modificată de alt utilizator; reîncărcați datele.', 1;
        UPDATE fin.MatchingRule
        SET Priority = @Priority, Name = @Name, TreasuryAccountId = @TreasuryAccountId, Direction = @Direction, CounterpartyIban = @CounterpartyIban,
            CounterpartyFiscalCode = @CounterpartyFiscalCode, RemittanceContains = @RemittanceContains, BankTransactionCode = @BankTransactionCode,
            AmountFrom = @AmountFrom, AmountTo = @AmountTo, TreasuryOperationTypeId = @TreasuryOperationTypeId, PartnerId = @PartnerId,
            CostCenterId = @CostCenterId, ProjectId = @ProjectId, NoteTemplate = @NoteTemplate, AutoValidate = @AutoValidate, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    END;

    SELECT Id, RowVersion FROM fin.MatchingRule WHERE Id = @Id;
END;
