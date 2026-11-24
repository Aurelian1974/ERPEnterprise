CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_ReverseFromEvent
    @CompanyId             int,
    @OriginalDocumentType  varchar(50),
    @OriginalDocumentId    bigint,
    @SourceEventId         uniqueidentifier,
    @AccountingDate        date,
    @SourceDocumentId      bigint = NULL,
    @SourceDocumentNumber  nvarchar(50) = NULL,
    @Black                 bit,
    @UserId                int
AS
BEGIN
    -- Documentul stornat la sursa (C2 4.2): nota de storno a notei automate a documentului original, cu aceleasi conturi (sume negative sau
    -- parti inversate), nu o contare noua cu modelul de azi. Nu arunca pentru motivele de coada: intoarce (ErrorCode, ErrorMessage) — 0 reusita,
    -- 51849 eveniment deja contat, 51848 luna inchisa, -1 originalul nu are nota (apelantul decide) — apoi antetul notei.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Existing bigint = (SELECT Id FROM fin.JournalEntry WHERE SourceEventId = @SourceEventId);
    IF @Existing IS NOT NULL
    BEGIN
        SELECT 51849 AS ErrorCode, N'Evenimentul a fost deja contat.' AS ErrorMessage;
        EXEC fin.usp_JournalEntry_Header @Id = @Existing;
        RETURN;
    END;

    DECLARE @Outer int = @@TRANCOUNT;
    IF @Outer = 0 BEGIN TRANSACTION;
    DECLARE @Original bigint, @Journal int;
    SELECT TOP (1) @Original = Id, @Journal = JournalId
    FROM fin.JournalEntry WITH (UPDLOCK, HOLDLOCK)
    WHERE CompanyId = @CompanyId AND SourceKind = 1 AND Status = 2 AND SourceDocumentType = @OriginalDocumentType AND SourceDocumentId = @OriginalDocumentId
    ORDER BY Id DESC;
    IF @Original IS NULL
    BEGIN
        IF @Outer = 0 COMMIT TRANSACTION;
        SELECT -1 AS ErrorCode, N'Documentul stornat nu are notă contabilă validată.' AS ErrorMessage;
        RETURN;
    END;
    IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND StartDate <= @AccountingDate AND EndDate >= @AccountingDate AND Status >= 3)
    BEGIN
        IF @Outer = 0 COMMIT TRANSACTION;
        SELECT 51848 AS ErrorCode, N'Data stornării e într-o lună închisă.' AS ErrorMessage;
        RETURN;
    END;

    DECLARE @Number int, @Year smallint = YEAR(@AccountingDate);
    EXEC fin.usp_JournalSequence_Next @CompanyId = @CompanyId, @JournalId = @Journal, @Year = @Year, @Number = @Number OUTPUT;

    INSERT INTO fin.JournalEntry (CompanyId, JournalId, Number, EntryDate, AccountingDate, PeriodYear, PeriodMonth, SourceKind, SourceModule, SourceDocumentType,
        SourceDocumentId, SourceDocumentNumber, SourceEventId, Status, ReversalOfId, Description, TotalDebit, TotalCredit, ValidatedAt, ValidatedBy, CreatedBy)
    SELECT CompanyId, JournalId, @Number, @AccountingDate, @AccountingDate, YEAR(@AccountingDate), MONTH(@AccountingDate), 6, SourceModule, SourceDocumentType,
        ISNULL(@SourceDocumentId, SourceDocumentId), ISNULL(@SourceDocumentNumber, SourceDocumentNumber), @SourceEventId, 2, Id,
        CONCAT(N'Storno: ', Description), CASE WHEN @Black = 1 THEN TotalCredit ELSE -TotalDebit END, CASE WHEN @Black = 1 THEN TotalDebit ELSE -TotalCredit END,
        SYSUTCDATETIME(), @UserId, @UserId
    FROM fin.JournalEntry WHERE Id = @Original;
    DECLARE @NewId bigint = SCOPE_IDENTITY();

    INSERT INTO fin.JournalLine (EntryId, CompanyId, PeriodYear, PeriodMonth, LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, ItemId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef)
    SELECT @NewId, CompanyId, YEAR(@AccountingDate), MONTH(@AccountingDate), LineNumber, AccountId, CASE WHEN @Black = 1 THEN 3 - Side ELSE Side END,
        CASE WHEN @Black = 1 THEN AmountRon ELSE -AmountRon END, CurrencyCode, CASE WHEN @Black = 1 THEN AmountCurrency ELSE -AmountCurrency END, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, ItemId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef
    FROM fin.JournalLine WHERE EntryId = @Original;

    UPDATE fin.JournalEntry SET Status = 3, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Original;
    UPDATE fin.PostingQueue SET Status = 2, ResolvedEntryId = @NewId, ModifiedAt = SYSUTCDATETIME() WHERE EventId = @SourceEventId AND Status = 1;
    EXEC fin.usp_AltMapping_Apply @EntryId = @NewId;
    IF @Outer = 0 COMMIT TRANSACTION;

    SELECT 0 AS ErrorCode, CAST(NULL AS nvarchar(400)) AS ErrorMessage;
    EXEC fin.usp_JournalEntry_Header @Id = @NewId;
END;
