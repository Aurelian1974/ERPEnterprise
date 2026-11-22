CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_Reverse
    @Id           int,
    @CompanyId    int,
    @Number       nvarchar(30),
    @ReversalDate date,
    @RowVersion   binary(8),
    @UserId       int
AS
BEGIN
    -- Stornarea unui OP validat (02.02, F2 3.6), in tranzactia apelantului: un OP nou, validat, cu sumele negative, la data aleasa de
    -- handler (data OP-ului sau azi, daca perioada e inchisa); originalul devine Stornat. Un OP confirmat pe extras se desface intai pe
    -- extras (51217); storno-ul nu se mai storneaza (51212).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @OrderCompanyId int, @Version binary(8), @Confirmation tinyint, @ReversalOf int;
    SELECT @Status = Status, @OrderCompanyId = CompanyId, @Version = RowVersion, @Confirmation = ConfirmationStatus, @ReversalOf = ReversalOfId
    FROM fin.PaymentOrder WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;

    IF @Status IS NULL OR @OrderCompanyId <> @CompanyId
        THROW 51225, N'Ordinul de plată nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Ordinul de plată a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 2 OR @ReversalOf IS NOT NULL
        THROW 51212, N'Se stornează doar un ordin de plată validat (nu o ciornă sau un storno).', 1;
    IF @Confirmation = 2
        THROW 51217, N'Ordinul de plată este confirmat pe extras; desfaceți întâi validarea liniei de extras.', 1;

    INSERT INTO fin.PaymentOrder (CompanyId, TreasuryAccountId, Number, DocumentDate, RequestedExecutionDate, IsUrgent, Origin, Purpose, PartnerId,
        EmployeeId, PartnerBankAccountId, BeneficiaryName, BeneficiaryIban, BeneficiaryBic, BeneficiaryFiscalCode, IsFreeIban, CurrencyCode, Amount,
        RemittanceInfo, BudgetObligationRef, Status, ValidatedAt, ValidatedBy, ReversalOfId, CreatedBy)
    SELECT CompanyId, TreasuryAccountId, @Number, @ReversalDate, @ReversalDate, 0, Origin, Purpose, PartnerId, EmployeeId, PartnerBankAccountId,
        BeneficiaryName, BeneficiaryIban, BeneficiaryBic, BeneficiaryFiscalCode, IsFreeIban, CurrencyCode, -Amount, RemittanceInfo, BudgetObligationRef, 2,
        SYSUTCDATETIME(), @UserId, Id, @UserId
    FROM fin.PaymentOrder WHERE Id = @Id;
    DECLARE @ReversalId int = SCOPE_IDENTITY();

    INSERT INTO fin.PaymentOrderLine (PaymentOrderId, LineNumber, TreasuryOperationTypeId, SettlementRef, Amount, CostCenterId, ProjectId)
    SELECT @ReversalId, LineNumber, TreasuryOperationTypeId, SettlementRef, -Amount, CostCenterId, ProjectId FROM fin.PaymentOrderLine WHERE PaymentOrderId = @Id;

    UPDATE fin.PaymentOrder SET Status = 3, ConfirmationStatus = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;

    SELECT @ReversalId AS Id, RowVersion FROM fin.PaymentOrder WHERE Id = @ReversalId;
END;
