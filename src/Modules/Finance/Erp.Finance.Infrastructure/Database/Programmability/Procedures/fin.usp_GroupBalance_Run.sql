CREATE OR ALTER PROCEDURE fin.usp_GroupBalance_Run
    @ChartId  int,
    @Year     smallint,
    @Month    tinyint,
    @Final    bit,
    @UserId   int
AS
BEGIN
    -- Balanta de grup (C5 3.7, 4.3; decizia 3) pe planul alternativ: soldurile firmelor planului cumulate de la inceputul anului pana la luna aleasa
    -- (din maparea liniilor, C1) si eliminarile: liniile conturilor marcate cu partener din alta firma a grupului (core.Partner.IntraGroupCompanyId; pe liniile fara partener, cel al notei),
    -- intoarse. Rularea finala cere luna inchisa la toate firmele (51979) si maparea completa (51980); previzualizarea, nu. Intoarce Id-ul rularii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Companies TABLE (CompanyId int PRIMARY KEY);
    INSERT INTO @Companies SELECT CompanyId FROM fin.ChartOfAccountsCompany WHERE ChartId = @ChartId;
    IF NOT EXISTS (SELECT 1 FROM @Companies)
        THROW 51980, N'Planul alternativ nu are firme.', 1;
    DECLARE @MonthStart date = DATEFROMPARTS(@Year, @Month, 1);
    IF @Final = 1
    BEGIN
        IF EXISTS (SELECT 1 FROM @Companies AS c WHERE NOT EXISTS (
                SELECT 1 FROM fin.AccountingPeriod AS p WHERE p.CompanyId = c.CompanyId AND p.StartDate = @MonthStart AND p.Status >= 3))
            THROW 51979, N'Luna nu e închisă la toate firmele grupului.', 1;
        IF EXISTS (SELECT 1 FROM fin.JournalLineAltAccount AS m JOIN @Companies AS c ON c.CompanyId = m.CompanyId
                   WHERE m.ChartId = @ChartId AND m.PeriodYear = @Year AND m.PeriodMonth <= @Month AND m.TargetAccountId IS NULL)
            THROW 51980, N'Există linii nemapate în planul alternativ.', 1;
    END;

    BEGIN TRANSACTION;
    INSERT INTO fin.GroupBalanceRun (ChartId, PeriodYear, PeriodMonth, IsFinal, RunBy) VALUES (@ChartId, @Year, @Month, @Final, @UserId);
    DECLARE @RunId int = SCOPE_IDENTITY();

    SELECT m.CompanyId, m.TargetAccountId AS AccountId, a.EliminateIntercompany, COALESCE(p.IntraGroupCompanyId, eg.GroupCompanyId) AS PartnerCompanyId,
        SUM(CASE WHEN m.Side = 1 THEN m.AmountRon ELSE 0 END) AS DebitRon, SUM(CASE WHEN m.Side = 2 THEN m.AmountRon ELSE 0 END) AS CreditRon
    INTO #Lines
    FROM fin.JournalLineAltAccount AS m
    JOIN @Companies AS c ON c.CompanyId = m.CompanyId
    JOIN fin.JournalLine AS l ON l.Id = m.JournalLineId
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId AND e.Status >= 2
    JOIN fin.GlAccount AS a ON a.Id = m.TargetAccountId
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    OUTER APPLY (SELECT MAX(p2.IntraGroupCompanyId) AS GroupCompanyId FROM fin.JournalLine AS l2 JOIN core.Partner AS p2 ON p2.Id = l2.PartnerId
                 WHERE l2.EntryId = l.EntryId) AS eg
    WHERE m.ChartId = @ChartId AND m.PeriodYear = @Year AND m.PeriodMonth <= @Month
    GROUP BY m.CompanyId, m.TargetAccountId, a.EliminateIntercompany, COALESCE(p.IntraGroupCompanyId, eg.GroupCompanyId);

    INSERT INTO fin.GroupBalanceRunLine (RunId, CompanyId, Kind, AccountId, DebitRon, CreditRon)
    SELECT @RunId, CompanyId, 1, AccountId, SUM(DebitRon), SUM(CreditRon) FROM #Lines GROUP BY CompanyId, AccountId;
    INSERT INTO fin.GroupBalanceRunLine (RunId, CompanyId, Kind, AccountId, DebitRon, CreditRon)
    SELECT @RunId, l.CompanyId, 2, l.AccountId, SUM(l.CreditRon), SUM(l.DebitRon)
    FROM #Lines AS l JOIN @Companies AS other ON other.CompanyId = l.PartnerCompanyId
    WHERE l.EliminateIntercompany = 1 AND l.PartnerCompanyId <> l.CompanyId
    GROUP BY l.CompanyId, l.AccountId;
    COMMIT;
    SELECT @RunId;
END;
