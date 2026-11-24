CREATE OR ALTER PROCEDURE fin.usp_GeneralLedger_Get
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Cartea mare (C5 3.3): rulajele lunii pe fiecare cont sintetic (codul de dinaintea punctului), pe conturi corespondente. Contul corespondent
    -- al unei linii e sinteticul unic de pe partea opusa a notei; cand partea opusa are mai multe sintetice („%”), corespondentul e „%”.
    -- Soldurile initiale si finale vin din balanta (acelasi calcul, C5 regula 1).
    SET NOCOUNT ON;

    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);
    WITH Lines AS (
        SELECT l.EntryId, l.Side, LEFT(a.Code, CHARINDEX(N'.', a.Code + N'.') - 1) AS Synthetic, l.DebitRon, l.CreditRon
        FROM fin.JournalLine AS l
        JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
        JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
        WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.SourceKind <> 5 AND e.AccountingDate BETWEEN @From AND @To),
    Opposite AS (
        SELECT EntryId, Side, MIN(Synthetic) AS FirstCode, COUNT(DISTINCT Synthetic) AS Codes FROM Lines GROUP BY EntryId, Side)
    SELECT x.Synthetic AS AccountCode, CASE WHEN o.Codes = 1 THEN o.FirstCode ELSE N'%' END AS CounterAccount,
        SUM(x.DebitRon) AS Debit, SUM(x.CreditRon) AS Credit
    FROM Lines AS x
    JOIN Opposite AS o ON o.EntryId = x.EntryId AND o.Side = 3 - x.Side
    GROUP BY x.Synthetic, CASE WHEN o.Codes = 1 THEN o.FirstCode ELSE N'%' END
    ORDER BY x.Synthetic, CounterAccount;
END;
