CREATE OR ALTER PROCEDURE fin.usp_CashDocument_Reverse
    @CashDocumentId int,
    @CompanyId      int,
    @RowVersion     binary(8),
    @Day            date,
    @Number         nvarchar(30) = NULL,
    @UserId         int
AS
BEGIN
    -- Stornarea unui document de casa validat (02.02, F1 3.3; decizia din 15.11.2026: doar storno, fara anulare): un document nou, de
    -- acelasi fel, cu sumele negative, datat in ziua curenta deschisa (@Day) si validat in aceeasi tranzactie, cu numar nou din serie.
    -- Ziua documentului initial nu se modifica. Un document deja stornat sau o stornare nu se mai storneaza (51011).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @CurrentVersion binary(8), @DocCompanyId int, @ReversalOfId int, @ReversedById int;
    SELECT @Status = Status, @CurrentVersion = RowVersion, @DocCompanyId = CompanyId, @ReversalOfId = ReversalOfId, @ReversedById = ReversedById
    FROM fin.CashDocument WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @CashDocumentId;

    IF @Status IS NULL OR @DocCompanyId <> @CompanyId
        THROW 51001, N'Documentul de casă nu există.', 1;
    IF @CurrentVersion <> @RowVersion
        THROW 51002, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status = 3 OR @ReversedById IS NOT NULL OR @ReversalOfId IS NOT NULL
        THROW 51011, N'Documentul este deja stornat sau este el însuși o stornare.', 1;
    IF @Status <> 2
        THROW 51003, N'Doar un document validat se stornează; o ciornă se modifică sau se șterge.', 1;

    DECLARE @ReversalId int;
    INSERT INTO fin.CashDocument (CompanyId, TreasuryAccountId, Kind, DocumentDate, PartnerId, EmployeeId, PersonName, PersonIdEncrypted, CurrencyCode,
        ExchangeRate, RateDate, TotalAmount, TotalAmountRon, Description, ReversalOfId, CashAdvanceId, ExpenseReportId, CreatedBy)
    SELECT CompanyId, TreasuryAccountId, Kind, @Day, PartnerId, EmployeeId, PersonName, PersonIdEncrypted, CurrencyCode,
        ExchangeRate, RateDate, -TotalAmount, -TotalAmountRon, CONCAT(N'Storno ', Number), Id, CashAdvanceId, ExpenseReportId, @UserId
    FROM fin.CashDocument WHERE Id = @CashDocumentId;
    SET @ReversalId = SCOPE_IDENTITY();

    INSERT INTO fin.CashDocumentLine (CashDocumentId, LineNumber, TreasuryOperationTypeId, Amount, AmountRon, CounterTreasuryAccountId, SettlementRef,
        CostCenterId, ProjectId, Note)
    SELECT @ReversalId, LineNumber, TreasuryOperationTypeId, -Amount, -AmountRon, CounterTreasuryAccountId, SettlementRef, CostCenterId, ProjectId, Note
    FROM fin.CashDocumentLine WHERE CashDocumentId = @CashDocumentId;

    DECLARE @ReversalVersion binary(8) = (SELECT RowVersion FROM fin.CashDocument WHERE Id = @ReversalId);
    DECLARE @Validated TABLE (CashDocumentId int, Number nvarchar(30), DayClosingBalance decimal(19,2), RowVersion binary(8));
    INSERT INTO @Validated
    EXEC fin.usp_CashDocument_Validate @CashDocumentId = @ReversalId, @CompanyId = @CompanyId, @Number = @Number, @RowVersion = @ReversalVersion,
        @UserId = @UserId;

    UPDATE fin.CashDocument
    SET Status = 3, ReversedById = @ReversalId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @CashDocumentId;

    SELECT v.CashDocumentId, v.Number, v.DayClosingBalance, d.RowVersion
    FROM @Validated AS v JOIN fin.CashDocument AS d ON d.Id = v.CashDocumentId;
END;
