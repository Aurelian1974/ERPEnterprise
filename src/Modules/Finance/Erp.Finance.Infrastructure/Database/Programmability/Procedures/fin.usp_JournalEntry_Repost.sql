CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Repost
    @CompanyId   int,
    @Id          bigint,
    @RowVersion  binary(8),
    @VariantId   int = NULL,
    @Lines       nvarchar(max),
    @Reason      nvarchar(300),
    @UserId      int
AS
BEGIN
    -- Recontarea unei note automate (C2 4.6): doar notele automate validate (51850), in luni care nu sunt inchise (51854). Versiunea veche (liniile,
    -- ca JSON) trece in fin.JournalEntryHistory; liniile se rescriu cu conturile date (cod de cont, ca la fin.usp_JournalEntry_Insert), numarul si
    -- data raman; verificarile din fin.usp_JournalEntry_Check. Intoarce versiunea veche si totalurile, pentru NotaContabilaRecontata.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Status tinyint, @Kind tinyint, @Version binary(8), @Date date, @OldTotal decimal(19,2);
    SELECT @Status = Status, @Kind = SourceKind, @Version = RowVersion, @Date = AccountingDate, @OldTotal = TotalDebit
    FROM fin.JournalEntry WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 51841, N'Nota nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51842, N'Nota a fost modificată între timp.', 1;
    IF @Status <> 2 OR @Kind <> 1
        THROW 51850, N'Se recontează doar notele automate validate și nestornate.', 1;
    IF EXISTS (SELECT 1 FROM fin.AccountingPeriod WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND StartDate <= @Date AND EndDate >= @Date AND Status >= 3)
        THROW 51854, N'Recontarea nu se face într-o lună închisă; corecția e o notă de reclasificare în luna curentă.', 1;

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
        DECLARE @Message nvarchar(200) = CONCAT(N'Contul ', @Missing, N' nu există în planul de conturi.');
        THROW 51844, @Message, 1;
    END;

    DECLARE @NextVersion int = ISNULL((SELECT MAX(Version) FROM fin.JournalEntryHistory WHERE EntryId = @Id), 0) + 1;
    INSERT INTO fin.JournalEntryHistory (EntryId, Version, Reason, TotalDebit, Lines, ChangedBy)
    SELECT @Id, @NextVersion, @Reason, @OldTotal,
        (SELECT a.Code AS accountCode, l.Side AS side, l.AmountRon AS amountRon, l.CurrencyCode AS currencyCode, l.AmountCurrency AS amountCurrency,
                l.PartnerId AS partnerId, l.EmployeeId AS employeeId, l.CostCenterId AS costCenterId, l.ProjectId AS projectId, l.WarehouseId AS warehouseId,
                l.SiteId AS siteId, l.Description AS description, l.ResolvedFrom AS resolvedFrom
         FROM fin.JournalLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId WHERE l.EntryId = @Id ORDER BY l.LineNumber FOR JSON PATH),
        @UserId;

    DELETE FROM fin.JournalLine WHERE EntryId = @Id;
    INSERT INTO fin.JournalLine (EntryId, CompanyId, PeriodYear, PeriodMonth, LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef)
    SELECT @Id, @CompanyId, YEAR(@Date), MONTH(@Date), LineNumber, AccountId, Side, AmountRon, CurrencyCode, AmountCurrency, PartnerId,
        EmployeeId, CostCenterId, ProjectId, WarehouseId, SiteId, VatRateCode, VatCategory, Description, ResolvedFrom, SourceLineRef
    FROM @New;

    EXEC fin.usp_JournalEntry_Check @EntryId = @Id;

    UPDATE fin.JournalEntry
    SET TotalDebit = (SELECT ISNULL(SUM(AmountRon), 0) FROM @New WHERE Side = 1), TotalCredit = (SELECT ISNULL(SUM(AmountRon), 0) FROM @New WHERE Side = 2),
        PostingModelVariantId = @VariantId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;
    EXEC fin.usp_AltMapping_Apply @EntryId = @Id;
    COMMIT TRANSACTION;

    SELECT @NextVersion AS OldVersion, @OldTotal AS OldTotal, TotalDebit AS NewTotal FROM fin.JournalEntry WHERE Id = @Id;
END;
