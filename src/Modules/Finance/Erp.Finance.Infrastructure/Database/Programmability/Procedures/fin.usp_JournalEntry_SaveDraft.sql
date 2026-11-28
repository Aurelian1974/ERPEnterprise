CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_SaveDraft
    @Id              bigint = NULL,
    @CompanyId       int,
    @JournalId       int,
    @EntryDate       date,
    @AccountingDate  date,
    @SourceKind      tinyint,
    @Description     nvarchar(400) = NULL,
    @DocumentRef     nvarchar(50) = NULL,
    @Lines           nvarchar(max),
    @RowVersion      binary(8) = NULL,
    @UserId          int
AS
BEGIN
    -- Ciorna unei note manuale sau de reclasificare (C2 3.8, 4.3): antetul si liniile (JSON [{accountId, side, amountRon, currencyCode,
    -- amountCurrency, partnerId, employeeId, costCenterId, projectId, warehouseId, siteId, description}]), inlocuite la fiecare salvare.
    -- Doar ciornele se modifica (51850); jurnalul e al instalarii sau al firmei (51021); RowVersion (51842).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.Journal WHERE Id = @JournalId AND IsActive = 1 AND (CompanyId IS NULL OR CompanyId = @CompanyId))
        THROW 51021, N'Jurnalul nu există.', 1;

    BEGIN TRANSACTION;
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.JournalEntry (CompanyId, JournalId, EntryDate, AccountingDate, PeriodYear, PeriodMonth, SourceKind, SourceDocumentNumber, Description,
            CreatedBy)
        VALUES (@CompanyId, @JournalId, @EntryDate, @AccountingDate, YEAR(@AccountingDate), MONTH(@AccountingDate), @SourceKind, @DocumentRef, @Description,
            @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM fin.JournalEntry WHERE Id = @Id AND CompanyId = @CompanyId)
            THROW 51841, N'Nota nu există.', 1;
        IF EXISTS (SELECT 1 FROM fin.JournalEntry WHERE Id = @Id AND Status <> 1)
            THROW 51850, N'Doar o notă în ciornă se modifică.', 1;
        UPDATE fin.JournalEntry
        SET JournalId = @JournalId, EntryDate = @EntryDate, AccountingDate = @AccountingDate, PeriodYear = YEAR(@AccountingDate),
            PeriodMonth = MONTH(@AccountingDate), SourceKind = @SourceKind, SourceDocumentNumber = @DocumentRef, Description = @Description,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 51842, N'Nota a fost modificată de altcineva între timp.', 1;
        DELETE FROM fin.JournalLine WHERE EntryId = @Id;
    END;

    INSERT INTO fin.JournalLine (EntryId, CompanyId, PeriodYear, PeriodMonth, LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, Description)
    SELECT @Id, @CompanyId, YEAR(@AccountingDate), MONTH(@AccountingDate), CAST([key] AS int) + 1, j.AccountId, j.Side, j.AmountRon, j.CurrencyCode,
        j.AmountCurrency, j.PartnerId, j.EmployeeId, j.CostCenterId, j.ProjectId, j.WarehouseId, j.SiteId, j.Description
    FROM OPENJSON(@Lines) AS x
    CROSS APPLY OPENJSON(x.value) WITH (AccountId int '$.accountId', Side tinyint '$.side', AmountRon decimal(19,2) '$.amountRon',
        CurrencyCode char(3) '$.currencyCode', AmountCurrency decimal(19,2) '$.amountCurrency', PartnerId int '$.partnerId', EmployeeId int '$.employeeId',
        CostCenterId int '$.costCenterId', ProjectId int '$.projectId', WarehouseId int '$.warehouseId', SiteId int '$.siteId',
        Description nvarchar(400) '$.description') AS j;

    UPDATE e SET TotalDebit = t.Debit, TotalCredit = t.Credit
    FROM fin.JournalEntry AS e
    CROSS APPLY (SELECT ISNULL(SUM(DebitRon), 0) AS Debit, ISNULL(SUM(CreditRon), 0) AS Credit FROM fin.JournalLine WHERE EntryId = e.Id) AS t
    WHERE e.Id = @Id;
    COMMIT TRANSACTION;

    SELECT Id, RowVersion FROM fin.JournalEntry WHERE Id = @Id;
END;
