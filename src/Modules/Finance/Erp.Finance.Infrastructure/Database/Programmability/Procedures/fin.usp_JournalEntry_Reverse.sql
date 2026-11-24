CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Reverse
    @CompanyId       int,
    @Id              bigint,
    @AccountingDate  date,
    @Description     nvarchar(400),
    @Black           bit,
    @AllowAutomatic  bit,
    @UserId          int
AS
BEGIN
    -- Stornarea unei note validate (C2 3.1 regula 5, 4.5): nota noua (SourceKind 6) in acelasi jurnal, la data aleasa, cu liniile repetate
    -- „in rosu” (sume negative, aceeasi parte) sau „in negru” (partile inversate); nota stornata trece in Stornata. Doar notele validate
    -- (51850); notele automate doar cu dreptul special (51852); data intr-o luna care primeste note (51848).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Status tinyint, @Kind tinyint, @Journal int;
    SELECT @Status = Status, @Kind = SourceKind, @Journal = JournalId FROM fin.JournalEntry WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 51841, N'Nota nu există.', 1;
    IF @Status <> 2
        THROW 51850, N'Doar o notă validată, nestornată, se stornează.', 1;
    IF @Kind = 1 AND @AllowAutomatic = 0
        THROW 51852, N'Nota automată se stornează prin stornarea documentului sursă.', 1;
    IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND StartDate <= @AccountingDate AND EndDate >= @AccountingDate AND Status >= 3)
        THROW 51848, N'Data stornării e într-o lună închisă.', 1;

    DECLARE @Number int, @Year smallint = YEAR(@AccountingDate);
    EXEC fin.usp_JournalSequence_Next @CompanyId = @CompanyId, @JournalId = @Journal, @Year = @Year, @Number = @Number OUTPUT;

    INSERT INTO fin.JournalEntry (CompanyId, JournalId, Number, EntryDate, AccountingDate, PeriodYear, PeriodMonth, SourceKind, SourceModule, SourceDocumentType,
        SourceDocumentId, SourceDocumentNumber, Status, ReversalOfId, Description, TotalDebit, TotalCredit, ValidatedAt, ValidatedBy, CreatedBy)
    SELECT CompanyId, JournalId, @Number, @AccountingDate, @AccountingDate, YEAR(@AccountingDate), MONTH(@AccountingDate), 6, SourceModule, SourceDocumentType,
        SourceDocumentId, SourceDocumentNumber, 2, Id, @Description, CASE WHEN @Black = 1 THEN TotalCredit ELSE -TotalDebit END,
        CASE WHEN @Black = 1 THEN TotalDebit ELSE -TotalCredit END, SYSUTCDATETIME(), @UserId, @UserId
    FROM fin.JournalEntry WHERE Id = @Id;
    DECLARE @NewId bigint = SCOPE_IDENTITY();

    INSERT INTO fin.JournalLine (EntryId, CompanyId, PeriodYear, PeriodMonth, LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, ItemId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef)
    SELECT @NewId, CompanyId, YEAR(@AccountingDate), MONTH(@AccountingDate), LineNumber, AccountId, CASE WHEN @Black = 1 THEN 3 - Side ELSE Side END,
        CASE WHEN @Black = 1 THEN AmountRon ELSE -AmountRon END, CurrencyCode, CASE WHEN @Black = 1 THEN AmountCurrency ELSE -AmountCurrency END, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, ItemId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef
    FROM fin.JournalLine WHERE EntryId = @Id;

    UPDATE fin.JournalEntry SET Status = 3, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    COMMIT TRANSACTION;

    EXEC fin.usp_JournalEntry_Header @Id = @NewId;
END;
