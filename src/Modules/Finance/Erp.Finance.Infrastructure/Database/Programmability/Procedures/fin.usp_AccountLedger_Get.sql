CREATE OR ALTER PROCEDURE fin.usp_AccountLedger_Get
    @CompanyId       int,
    @AccountCode     nvarchar(20),
    @From            date,
    @To              date,
    @PartnerId       int = NULL,
    @CostCenterId    int = NULL,
    @ProjectId       int = NULL,
    @IncludeClosing  bit = 1,
    @Skip            int = 0,
    @Take            int = 500
AS
BEGIN
    -- Fisa contului (C5 3.3): contul si descendentii lui (sinteticele de grad inferior prin cifre, analiticele prin punct), in anul financiar al
    -- perioadei (51973). Intai antetul: soldul initial al perioadei (notele de deschidere si tot ce e inainte de @From in anul financiar),
    -- rulajele perioadei, numarul de linii; apoi liniile perioadei, cu soldul curent (debit − credit, cumulat cu functie fereastra), paginate.
    SET NOCOUNT ON;

    DECLARE @YearStart date, @YearEnd date;
    SELECT @YearStart = StartDate, @YearEnd = EndDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND StartDate <= @From AND EndDate >= @From;
    IF @YearStart IS NULL
        SELECT @YearStart = DATEFROMPARTS(YEAR(@From), 1, 1), @YearEnd = DATEFROMPARTS(YEAR(@From), 12, 31);
    IF @To < @From OR @To > @YearEnd
        THROW 51973, N'Fișa contului e pe un singur an financiar: intervalul trece în anul următor.', 1;

    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    DECLARE @Lines TABLE (LineId bigint PRIMARY KEY, EntryId bigint, AccountingDate date, LineNumber int, IsOpening bit, AccountCode nvarchar(20), Debit decimal(19,2),
        Credit decimal(19,2));
    INSERT INTO @Lines
    SELECT l.Id, e.Id, e.AccountingDate, l.LineNumber, CASE WHEN e.SourceKind = 5 OR e.AccountingDate < @From THEN 1 ELSE 0 END, a.Code, l.DebitRon, l.CreditRon
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
    WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @To AND (@IncludeClosing = 1 OR e.SourceKind <> 4)
      AND (a.Code = @AccountCode OR a.Code LIKE @AccountCode + N'.%' OR (CHARINDEX(N'.', @AccountCode) = 0 AND a.Code LIKE @AccountCode + N'[0-9]%'))
      AND (@PartnerId IS NULL OR l.PartnerId = @PartnerId) AND (@CostCenterId IS NULL OR l.CostCenterId = @CostCenterId)
      AND (@ProjectId IS NULL OR l.ProjectId = @ProjectId);

    DECLARE @Opening decimal(19,2) = ISNULL((SELECT SUM(Debit - Credit) FROM @Lines WHERE IsOpening = 1), 0);
    SELECT @Opening AS Opening, ISNULL(SUM(Debit), 0) AS PeriodDebit, ISNULL(SUM(Credit), 0) AS PeriodCredit, COUNT(*) AS LineCount,
        (SELECT a.Name FROM fin.GlAccount AS a WHERE a.ChartId = @Statutory AND a.CodeKey = UPPER(@AccountCode)) AS AccountName,
        (SELECT a.AccountType FROM fin.GlAccount AS a WHERE a.ChartId = @Statutory AND a.CodeKey = UPPER(@AccountCode)) AS AccountType
    FROM @Lines WHERE IsOpening = 0;

    WITH Running AS (
        SELECT x.LineId, x.EntryId, x.AccountingDate, x.LineNumber, x.AccountCode, x.Debit, x.Credit,
            @Opening + SUM(x.Debit - x.Credit) OVER (ORDER BY x.AccountingDate, x.EntryId, x.LineNumber ROWS UNBOUNDED PRECEDING) AS Balance
        FROM @Lines AS x WHERE x.IsOpening = 0)
    SELECT r.LineId, r.EntryId, j.Code AS JournalCode, e.Number, r.AccountingDate, r.AccountCode, e.SourceKind, e.SourceDocumentType, e.SourceDocumentNumber,
        COALESCE(l.Description, e.Description) AS Description, p.Name AS PartnerName, cc.Code AS CostCenterCode, pr.Code AS ProjectCode, r.Debit, r.Credit, r.Balance
    FROM Running AS r
    JOIN fin.JournalEntry AS e ON e.Id = r.EntryId
    JOIN fin.Journal AS j ON j.Id = e.JournalId
    JOIN fin.JournalLine AS l ON l.Id = r.LineId
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    LEFT JOIN core.DimensionValue AS cc ON cc.Id = l.CostCenterId
    LEFT JOIN core.DimensionValue AS pr ON pr.Id = l.ProjectId
    ORDER BY r.AccountingDate, r.EntryId, r.LineNumber
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END;
