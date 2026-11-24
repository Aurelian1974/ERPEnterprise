CREATE OR ALTER PROCEDURE fin.usp_Reconciliation_Get
    @CompanyId  int,
    @AsOf       date,
    @Explain    bit = 1
AS
BEGIN
    -- Concordantele la o data (C3 3.4): soldul contabil (D − C, din anul financiar al datei) fata de evidenta operativa, pe zona si cheie.
    -- Clienti: conturile rolurilor CustomerReceivable si CustomerAdvance (cu analiticele lor) pe partener, fata de partidele clientilor la data
    -- (F3); furnizori: SupplierPayable si SupplierAdvance fata de partidele furnizorilor; casa: contul fiecarei case in lei fata de soldul
    -- ultimei zile din registrul de casa. Intai randurile cu diferenta, apoi documentele din coada „De contat” care le explica (documentul
    -- partidei e in coada), doar cu @Explain = 1 (lista de verificari citeste doar diferentele).
    SET NOCOUNT ON;

    DECLARE @YearStart date = ISNULL((SELECT StartDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND StartDate <= @AsOf AND EndDate >= @AsOf),
        DATEFROMPARTS(YEAR(@AsOf), 1, 1));
    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);

    DECLARE @RoleCodes TABLE (Area tinyint, Code nvarchar(20));
    INSERT INTO @RoleCodes
    SELECT DISTINCT CASE WHEN r.Role IN ('CustomerReceivable', 'CustomerAdvance') THEN 1 ELSE 2 END, a.Code
    FROM fin.GlAccountRoleAssignment AS r JOIN fin.GlAccount AS a ON a.Id = r.AccountId
    WHERE r.Role IN ('CustomerReceivable', 'CustomerAdvance', 'SupplierPayable', 'SupplierAdvance') AND (r.CompanyId IS NULL OR r.CompanyId = @CompanyId)
      AND r.ValidFrom <= @AsOf AND (r.ValidTo IS NULL OR r.ValidTo >= @AsOf);

    DECLARE @Ledger TABLE (Area tinyint, PartnerId int, Amount decimal(19,2));
    INSERT INTO @Ledger
    SELECT rc.Area, l.PartnerId, SUM(l.DebitRon - l.CreditRon)
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
    CROSS APPLY (SELECT TOP (1) c.Area FROM @RoleCodes AS c
                 WHERE a.Code = c.Code OR a.Code LIKE c.Code + N'.%' OR (CHARINDEX(N'.', c.Code) = 0 AND a.Code LIKE c.Code + N'[0-9]%')) AS rc
    WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @AsOf
    GROUP BY rc.Area, l.PartnerId;

    DECLARE @Sub TABLE (Area tinyint, PartnerId int, Amount decimal(19,2));
    INSERT INTO @Sub
    SELECT o.Role, o.PartnerId, SUM(ROUND(x.OpenAsOf * o.ExchangeRate, 2))
    FROM fin.OpenItem AS o
    OUTER APPLY (SELECT SUM(a.DebitAmount) AS Allocated FROM fin.Allocation AS a
                 WHERE a.DebitOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS d
    OUTER APPLY (SELECT SUM(a.CreditAmount) AS Allocated FROM fin.Allocation AS a
                 WHERE a.CreditOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS c
    CROSS APPLY (SELECT o.Amount - ISNULL(d.Allocated, 0) + ISNULL(c.Allocated, 0) AS OpenAsOf) AS x
    WHERE o.CompanyId = @CompanyId AND o.DocumentDate <= @AsOf AND o.Role IN (1, 2)
    GROUP BY o.Role, o.PartnerId;

    DECLARE @Rows TABLE (Area tinyint, KeyId int, KeyName nvarchar(200), Ledger decimal(19,2), Subledger decimal(19,2));
    INSERT INTO @Rows
    SELECT ISNULL(g.Area, s.Area), ISNULL(g.PartnerId, s.PartnerId), p.Name, ISNULL(g.Amount, 0), ISNULL(s.Amount, 0)
    FROM @Ledger AS g
    FULL JOIN @Sub AS s ON s.Area = g.Area AND s.PartnerId = g.PartnerId
    LEFT JOIN core.Partner AS p ON p.Id = ISNULL(g.PartnerId, s.PartnerId)
    WHERE ISNULL(g.Amount, 0) <> ISNULL(s.Amount, 0);

    INSERT INTO @Rows
    SELECT 3, t.Id, t.Code + N' · ' + t.Name, ISNULL(gl.Amount, 0), ISNULL(cd.Closing, 0)
    FROM fin.TreasuryAccount AS t
    OUTER APPLY (SELECT TOP (1) d.ClosingBalance AS Closing FROM fin.CashRegisterDay AS d WHERE d.TreasuryAccountId = t.Id AND d.[Day] <= @AsOf ORDER BY d.[Day] DESC) AS cd
    OUTER APPLY (SELECT SUM(l.DebitRon - l.CreditRon) AS Amount
                 FROM fin.JournalLine AS l
                 JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
                 JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
                 WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @AsOf
                   AND (a.Code = t.GlAccountCode OR a.Code LIKE t.GlAccountCode + N'.%')) AS gl
    WHERE t.CompanyId = @CompanyId AND t.Kind = 2 AND t.CurrencyCode = 'RON' AND t.GlAccountCode IS NOT NULL AND ISNULL(gl.Amount, 0) <> ISNULL(cd.Closing, 0);

    SELECT Area, KeyId, KeyName, Ledger, Subledger, Ledger - Subledger AS Difference FROM @Rows ORDER BY Area, KeyName;

    IF @Explain = 0
        RETURN;

    SELECT o.Role AS Area, o.PartnerId AS KeyId, q.Id AS QueueId, ISNULL(q.SourceDocumentNumber, o.DocumentNo) AS DocumentNo, q.Reason, ROUND(o.Amount * o.ExchangeRate, 2) AS Amount
    FROM fin.PostingQueue AS q
    JOIN fin.OpenItem AS o ON o.CompanyId = q.CompanyId AND o.SourceDocumentType = q.SourceDocumentType AND o.SourceDocumentId = q.SourceDocumentId
    JOIN @Rows AS r ON r.Area = o.Role AND r.KeyId = o.PartnerId
    WHERE q.CompanyId = @CompanyId AND q.Status = 1 AND q.AccountingDate <= @AsOf;
END;
