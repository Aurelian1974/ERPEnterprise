CREATE OR ALTER PROCEDURE fin.usp_CashFlowSnapshot_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Un instantaneu (F5 3.6, 4.5): antetul (fara rand daca nu exista) si sumele prognozate pe zi de inceput a perioadei si categorie, in lei.
    SET NOCOUNT ON;

    SELECT s.Id, s.Name, s.TakenAt, s.StartDate, s.HorizonFrom, s.HorizonTo, s.Granularity, s.TreasuryAccountId, s.CurrencyCode, s.IsAutomatic,
           s.OpeningBalanceRon
    FROM fin.CashFlowSnapshot AS s
    WHERE s.Id = @Id AND s.CompanyId = @CompanyId;

    SELECT l.PeriodStart, l.CategoryId, SUM(l.AmountRon) AS AmountRon
    FROM fin.CashFlowSnapshotLine AS l
    JOIN fin.CashFlowSnapshot AS s ON s.Id = l.SnapshotId
    WHERE l.SnapshotId = @Id AND s.CompanyId = @CompanyId AND l.SourceKind <> 1
    GROUP BY l.PeriodStart, l.CategoryId;
END;
