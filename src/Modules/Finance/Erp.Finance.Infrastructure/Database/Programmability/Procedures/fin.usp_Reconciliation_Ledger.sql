CREATE OR ALTER PROCEDURE fin.usp_Reconciliation_Ledger
    @CompanyId int,
    @AsOf      date,
    @Roles     nvarchar(400)
AS
BEGIN
    -- Soldul contabil pentru concordanta unei evidente operative a altui modul (C3 3.4; ISubledgerBalanceProvider): D − C din anul financiar al
    -- datei, pe gestiune (dimensiunea liniei), pe conturile rolurilor date (lista separata prin virgula) si analiticele lor. Doar liniile cu
    -- gestiune intra (de exemplu 4428 al facturilor cu TVA la incasare nu are gestiune).
    SET NOCOUNT ON;

    DECLARE @YearStart date = ISNULL((SELECT StartDate FROM fin.FiscalYear WHERE CompanyId = @CompanyId AND StartDate <= @AsOf AND EndDate >= @AsOf),
        DATEFROMPARTS(YEAR(@AsOf), 1, 1));
    DECLARE @Statutory int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);

    DECLARE @Codes TABLE (Code nvarchar(20) PRIMARY KEY);
    INSERT INTO @Codes
    SELECT DISTINCT a.Code
    FROM fin.GlAccountRoleAssignment AS r JOIN fin.GlAccount AS a ON a.Id = r.AccountId
    WHERE r.Role IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@Roles, ',')) AND (r.CompanyId IS NULL OR r.CompanyId = @CompanyId)
      AND r.ValidFrom <= @AsOf AND (r.ValidTo IS NULL OR r.ValidTo >= @AsOf);

    SELECT l.WarehouseId AS KeyId, SUM(l.DebitRon - l.CreditRon) AS Amount
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId AND a.ChartId = @Statutory
    WHERE e.CompanyId = @CompanyId AND e.Status >= 2 AND e.AccountingDate BETWEEN @YearStart AND @AsOf AND l.WarehouseId IS NOT NULL
      AND EXISTS (SELECT 1 FROM @Codes AS c WHERE a.Code = c.Code OR a.Code LIKE c.Code + N'.%' OR a.Code LIKE c.Code + N'[0-9]%')
    GROUP BY l.WarehouseId;
END;
