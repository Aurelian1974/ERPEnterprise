CREATE OR ALTER PROCEDURE fin.usp_ExpenseReport_Save
    @Id             int = NULL,
    @CompanyId      int,
    @EmployeeId     int,
    @CashAdvanceId  int = NULL,
    @DocumentDate   date,
    @Description    nvarchar(500) = NULL,
    @TotalJustified decimal(19,2),
    @Lines          fin.ExpenseReportLineList READONLY,
    @RowVersion     binary(8) = NULL,
    @UserId         int
AS
BEGIN
    -- Ciorna decontului de cheltuieli (02.02, F1 3.7; introdusa de contabil). Avansul e al aceluiasi angajat, acordat si nelegat de alt
    -- decont (51020); totalul e suma liniilor (51009). Diurna (zile, suma pe zi, partea neimpozabila si impozabila) e calculata in C#.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @EmployeeId AND CompanyId = @CompanyId)
        THROW 51021, N'Angajatul nu aparține firmei.', 1;

    IF @Id IS NOT NULL
    BEGIN
        DECLARE @Status tinyint, @Version binary(8), @DocCompanyId int;
        SELECT @Status = Status, @Version = RowVersion, @DocCompanyId = CompanyId FROM fin.ExpenseReport WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @Status IS NULL OR @DocCompanyId <> @CompanyId
            THROW 51024, N'Decontul nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51002, N'Decontul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 51025, N'Doar o ciornă de decont se modifică.', 1;
    END;

    DECLARE @AdvanceAmount decimal(19,2) = 0, @Currency char(3) = 'RON';
    IF @CashAdvanceId IS NOT NULL
    BEGIN
        SELECT @AdvanceAmount = Amount, @Currency = CurrencyCode
        FROM fin.CashAdvance
        WHERE Id = @CashAdvanceId AND CompanyId = @CompanyId AND EmployeeId = @EmployeeId AND Status IN (1, 2, 4);
        IF @@ROWCOUNT = 0
           OR EXISTS (SELECT 1 FROM fin.ExpenseReport WHERE CashAdvanceId = @CashAdvanceId AND Status <> 5 AND (@Id IS NULL OR Id <> @Id))
            THROW 51020, N'Avansul nu este al angajatului, nu este acordat sau este deja decontat.', 1;
    END;

    IF EXISTS (SELECT 1 FROM @Lines AS l LEFT JOIN fin.ExpenseType AS t ON t.Id = l.ExpenseTypeId AND t.IsActive = 1 WHERE t.Id IS NULL)
        THROW 51021, N'Tipul de cheltuială nu există sau este inactiv.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l
               WHERE (l.CostCenterId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue AS d WHERE d.Id = l.CostCenterId AND d.CompanyId = @CompanyId))
                  OR (l.ProjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue AS d WHERE d.Id = l.ProjectId AND d.CompanyId = @CompanyId)))
        THROW 51021, N'Centrul de cost sau proiectul nu aparține firmei.', 1;
    IF @TotalJustified <> ISNULL((SELECT SUM(Amount) FROM @Lines), 0)
        THROW 51009, N'Totalul justificat diferă de suma liniilor.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.ExpenseReport (CompanyId, EmployeeId, CashAdvanceId, DocumentDate, CurrencyCode, TotalJustified, AdvanceAmount, Description, CreatedBy)
        VALUES (@CompanyId, @EmployeeId, @CashAdvanceId, @DocumentDate, @Currency, @TotalJustified, @AdvanceAmount, @Description, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE fin.ExpenseReport
        SET EmployeeId = @EmployeeId, CashAdvanceId = @CashAdvanceId, DocumentDate = @DocumentDate, CurrencyCode = @Currency,
            TotalJustified = @TotalJustified, AdvanceAmount = @AdvanceAmount, Description = @Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM fin.ExpenseReportLine WHERE ExpenseReportId = @Id;
    END;

    INSERT INTO fin.ExpenseReportLine (ExpenseReportId, LineNumber, ExpenseTypeId, JustificationKind, DocumentNo, DocumentDate, SupplierFiscalCode,
        SupplierInvoiceId, Amount, VatAmount, CostCenterId, ProjectId, Days, DailyAmount, NonTaxableAmount, TaxableAmount, Note)
    SELECT @Id, LineNumber, ExpenseTypeId, JustificationKind, DocumentNo, DocumentDate, SupplierFiscalCode, SupplierInvoiceId, Amount, VatAmount,
        CostCenterId, ProjectId, Days, DailyAmount, NonTaxableAmount, TaxableAmount, Note
    FROM @Lines;

    SELECT Id, RowVersion FROM fin.ExpenseReport WHERE Id = @Id;
END;
