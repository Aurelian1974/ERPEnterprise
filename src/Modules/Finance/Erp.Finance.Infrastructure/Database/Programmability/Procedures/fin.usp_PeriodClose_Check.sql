CREATE OR ALTER PROCEDURE fin.usp_PeriodClose_Check
    @CompanyId  int,
    @Code       varchar(40),
    @Start      date,
    @End        date
AS
BEGIN
    -- Un pas al listei de verificari (C3 3.3), pe set: intoarce problemele gasite (Message, DocumentRef, Amount), niciuna = pas in regula.
    -- PREV_PERIOD luna anterioara inca deschisa; EVENTS_IN_TRANSIT evenimente cu model de contare inca neprocesate de outbox (documente validate
    -- care nu au ajuns la contare); CASH_DAYS zile de casa deschise; POSTING_QUEUE coada „De contat” in luna; DRAFT_ENTRIES ciorne de note;
    -- ALT_MAPPING linii nemapate in planurile cu mapare completa; ABNORMAL_BALANCES 5311 si 371 creditoare, 401 debitor pe partener.
    SET NOCOUNT ON;

    IF @Code = 'PREV_PERIOD'
        SELECT TOP (1) CONCAT(N'Luna care începe la ', CONVERT(char(10), StartDate, 104), N' nu e închisă.') AS Message, CAST(NULL AS nvarchar(100)) AS DocumentRef,
            CAST(NULL AS decimal(19,2)) AS Amount
        FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND EndDate < @Start AND Status < 3 ORDER BY StartDate;
    ELSE IF @Code = 'EVENTS_IN_TRANSIT'
        SELECT CONCAT(N'Evenimentul ', m.EventType, N' din ', CONVERT(char(16), o.OccurredAt, 120), N' nu e încă tratat.') AS Message,
            CAST(o.EventId AS nvarchar(100)) AS DocumentRef, CAST(NULL AS decimal(19,2)) AS Amount
        FROM core.Outbox AS o
        JOIN fin.PostingModel AS m ON m.IsActive = 1 AND o.EventType LIKE N'%.' + m.EventType
        WHERE o.CompanyId = @CompanyId AND o.ProcessedAt IS NULL;
    ELSE IF @Code = 'CASH_DAYS'
        SELECT CONCAT(N'Ziua ', CONVERT(char(10), d.[Day], 104), N' a casei ', t.Code, N' nu e închisă.') AS Message, t.Code AS DocumentRef,
            CAST(NULL AS decimal(19,2)) AS Amount
        FROM fin.CashRegisterDay AS d JOIN fin.TreasuryAccount AS t ON t.Id = d.TreasuryAccountId
        WHERE t.CompanyId = @CompanyId AND d.[Day] BETWEEN @Start AND @End AND d.Status = 1;
    ELSE IF @Code = 'POSTING_QUEUE'
        SELECT CONCAT(ISNULL(q.SourceDocumentNumber, q.EventType), N' așteaptă în coada „De contat”.') AS Message, CAST(q.Id AS nvarchar(100)) AS DocumentRef,
            CAST(NULL AS decimal(19,2)) AS Amount
        FROM fin.PostingQueue AS q WHERE q.CompanyId = @CompanyId AND q.Status = 1 AND q.AccountingDate BETWEEN @Start AND @End;
    ELSE IF @Code = 'DRAFT_ENTRIES'
        SELECT CONCAT(N'Ciorna din ', CONVERT(char(10), e.AccountingDate, 104), N': ', ISNULL(e.Description, N'')) AS Message, CAST(e.Id AS nvarchar(100)) AS DocumentRef,
            e.TotalDebit AS Amount
        FROM fin.JournalEntry AS e WHERE e.CompanyId = @CompanyId AND e.Status = 1 AND e.AccountingDate BETWEEN @Start AND @End;
    ELSE IF @Code = 'ALT_MAPPING'
        SELECT CONCAT(N'Planul ', c.Code, N': ', COUNT(*), N' linii nemapate.') AS Message, c.Code AS DocumentRef, SUM(m.AmountRon) AS Amount
        FROM fin.JournalLineAltAccount AS m JOIN fin.ChartOfAccounts AS c ON c.Id = m.ChartId AND c.RequireCompleteMapping = 1 AND c.IsActive = 1
        WHERE m.CompanyId = @CompanyId AND m.PeriodYear = YEAR(@Start) AND m.PeriodMonth = MONTH(@Start) AND m.TargetAccountId IS NULL
        GROUP BY c.Code;
    ELSE IF @Code = 'ABNORMAL_BALANCES'
        SELECT CONCAT(N'Contul ', x.Code, CASE WHEN x.PartnerName IS NULL THEN N'' ELSE N' · ' + x.PartnerName END,
                CASE WHEN x.Net < 0 THEN N' are sold creditor.' ELSE N' are sold debitor.' END) AS Message, x.Code AS DocumentRef, ABS(x.Net) AS Amount
        FROM (
            SELECT a.Code, CASE WHEN a.Code LIKE N'401%' THEN p.Name END AS PartnerName, SUM(l.DebitRon - l.CreditRon) AS Net
            FROM fin.JournalLine AS l
            JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
            JOIN fin.GlAccount AS a ON a.Id = l.AccountId
            LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
            WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate <= @End
              AND e.AccountingDate >= ISNULL((SELECT y.StartDate FROM fin.FiscalYear AS y WHERE y.CompanyId = @CompanyId AND y.StartDate <= @End AND y.EndDate >= @End),
                                             DATEFROMPARTS(YEAR(@End), 1, 1))
              AND (a.Code LIKE N'5311%' OR a.Code LIKE N'371%' OR a.Code LIKE N'401%')
            GROUP BY a.Code, CASE WHEN a.Code LIKE N'401%' THEN p.Name END, CASE WHEN a.Code LIKE N'401%' THEN l.PartnerId END) AS x
        WHERE (x.Code LIKE N'401%' AND x.Net > 0) OR (x.Code NOT LIKE N'401%' AND x.Net < 0);
    ELSE
        THROW 51903, N'Pasul listei de verificări nu există.', 1;
END;
