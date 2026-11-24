CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Insert
    @CompanyId             int,
    @JournalId             int,
    @AccountingDate        date,
    @DocumentDate          date,
    @SourceModule          varchar(20),
    @SourceDocumentType    varchar(50),
    @SourceDocumentId      bigint = NULL,
    @SourceDocumentNumber  nvarchar(50) = NULL,
    @SourceEventId         uniqueidentifier,
    @VariantId             int = NULL,
    @Description           nvarchar(400) = NULL,
    @Lines                 nvarchar(max),
    @EventType             varchar(80) = NULL,
    @Payload               nvarchar(max) = NULL,
    @UserId                int
AS
BEGIN
    -- Scrie nota validata a unui eveniment (C2 4.1 pasul 5). Nu arunca pentru motivele de coada: intoarce intai (ErrorCode, ErrorMessage), 0 la
    -- reusita, apoi antetul notei. Idempotent pe eveniment (51849, apelantul il trateaza ca reusit), conturile dupa cod (51844), perioada care
    -- primeste note automate (51848), verificarile din fin.usp_JournalEntry_FirstError si numarul jurnalului. Liniile: JSON [{accountCode, side,
    -- amountRon, currencyCode, amountCurrency, partnerId, employeeId, costCenterId, projectId, warehouseId, siteId, vatRateCode, vatCategory,
    -- description, resolvedFrom, sourceLineRef}]. In tranzactia apelantului (consumatorul) lucreaza pe un punct de salvare, ca o nota respinsa sa
    -- fie anulata fara sa anuleze punerea in coada care urmeaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Existing bigint = (SELECT Id FROM fin.JournalEntry WHERE SourceEventId = @SourceEventId);
    IF @Existing IS NOT NULL
    BEGIN
        SELECT 51849 AS ErrorCode, N'Evenimentul a fost deja contat.' AS ErrorMessage;
        EXEC fin.usp_JournalEntry_Header @Id = @Existing;
        RETURN;
    END;

    DECLARE @Chart int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    DECLARE @New TABLE (LineNumber int, AccountCode nvarchar(20), AccountId int, Side tinyint, AmountRon decimal(19,2), CurrencyCode char(3),
        AmountCurrency decimal(19,2), PartnerId int, EmployeeId int, CostCenterId int, ProjectId int, WarehouseId int, SiteId int, VatRateCode nvarchar(10),
        VatCategory nvarchar(4), Description nvarchar(400), ResolvedFrom tinyint, SourceLineRef nvarchar(40));
    INSERT INTO @New
    SELECT CAST(x.[key] AS int) + 1, j.AccountCode, a.Id, j.Side, j.AmountRon, j.CurrencyCode, j.AmountCurrency, j.PartnerId, j.EmployeeId, j.CostCenterId,
        j.ProjectId, j.WarehouseId, j.SiteId, j.VatRateCode, j.VatCategory, j.Description, j.ResolvedFrom, j.SourceLineRef
    FROM OPENJSON(@Lines) AS x
    CROSS APPLY OPENJSON(x.value) WITH (AccountCode nvarchar(20) '$.accountCode', Side tinyint '$.side', AmountRon decimal(19,2) '$.amountRon',
        CurrencyCode char(3) '$.currencyCode', AmountCurrency decimal(19,2) '$.amountCurrency', PartnerId int '$.partnerId', EmployeeId int '$.employeeId',
        CostCenterId int '$.costCenterId', ProjectId int '$.projectId', WarehouseId int '$.warehouseId', SiteId int '$.siteId',
        VatRateCode nvarchar(10) '$.vatRateCode', VatCategory nvarchar(4) '$.vatCategory', Description nvarchar(400) '$.description',
        ResolvedFrom tinyint '$.resolvedFrom', SourceLineRef nvarchar(40) '$.sourceLineRef') AS j
    LEFT JOIN fin.GlAccount AS a ON a.ChartId = @Chart AND a.CodeKey = UPPER(j.AccountCode);

    DECLARE @Missing nvarchar(20) = (SELECT TOP (1) AccountCode FROM @New WHERE AccountId IS NULL ORDER BY LineNumber);
    IF @Missing IS NOT NULL
    BEGIN
        SELECT 51844 AS ErrorCode, CONCAT(N'Contul ', @Missing, N' nu există în planul de conturi.') AS ErrorMessage;
        RETURN;
    END;

    DECLARE @Outer int = @@TRANCOUNT, @Code int, @Message nvarchar(400);
    IF @Outer = 0 BEGIN TRANSACTION; ELSE SAVE TRANSACTION PostingInsert;
    IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND StartDate <= @AccountingDate AND EndDate >= @AccountingDate AND Status >= 3)
    BEGIN
        IF @Outer = 0 ROLLBACK TRANSACTION; ELSE ROLLBACK TRANSACTION PostingInsert;
        SELECT 51848 AS ErrorCode, N'Data contabilă e într-o lună închisă.' AS ErrorMessage;
        RETURN;
    END;

    INSERT INTO fin.JournalEntry (CompanyId, JournalId, EntryDate, AccountingDate, PeriodYear, PeriodMonth, SourceKind, SourceModule, SourceDocumentType,
        SourceDocumentId, SourceDocumentNumber, SourceEventId, PostingModelVariantId, Status, Description, TotalDebit, TotalCredit, CreatedBy)
    VALUES (@CompanyId, @JournalId, @DocumentDate, @AccountingDate, YEAR(@AccountingDate), MONTH(@AccountingDate), 1, @SourceModule, @SourceDocumentType,
        @SourceDocumentId, @SourceDocumentNumber, @SourceEventId, @VariantId, 1, @Description,
        (SELECT ISNULL(SUM(AmountRon), 0) FROM @New WHERE Side = 1), (SELECT ISNULL(SUM(AmountRon), 0) FROM @New WHERE Side = 2), @UserId);
    DECLARE @Id bigint = SCOPE_IDENTITY();

    INSERT INTO fin.JournalLine (EntryId, CompanyId, PeriodYear, PeriodMonth, LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef)
    SELECT @Id, @CompanyId, YEAR(@AccountingDate), MONTH(@AccountingDate), LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef
    FROM @New;

    IF @Payload IS NOT NULL
        INSERT INTO fin.JournalEntrySource (EntryId, EventType, Payload) VALUES (@Id, @EventType, @Payload);

    EXEC fin.usp_JournalEntry_FirstError @EntryId = @Id, @ErrorCode = @Code OUTPUT, @ErrorMessage = @Message OUTPUT;
    IF @Code <> 0
    BEGIN
        IF @Outer = 0 ROLLBACK TRANSACTION; ELSE ROLLBACK TRANSACTION PostingInsert;
        SELECT @Code AS ErrorCode, @Message AS ErrorMessage;
        RETURN;
    END;

    DECLARE @Number int, @Year smallint = YEAR(@AccountingDate);
    EXEC fin.usp_JournalSequence_Next @CompanyId = @CompanyId, @JournalId = @JournalId, @Year = @Year, @Number = @Number OUTPUT;
    UPDATE fin.JournalEntry SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId WHERE Id = @Id;
    UPDATE fin.PostingQueue SET Status = 2, ResolvedEntryId = @Id, ModifiedAt = SYSUTCDATETIME() WHERE EventId = @SourceEventId AND Status = 1;
    EXEC fin.usp_AltMapping_Apply @EntryId = @Id;
    IF @Outer = 0 COMMIT TRANSACTION;

    SELECT 0 AS ErrorCode, CAST(NULL AS nvarchar(400)) AS ErrorMessage;
    EXEC fin.usp_JournalEntry_Header @Id = @Id;
END;
