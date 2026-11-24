CREATE OR ALTER PROCEDURE fin.usp_CashFlowSnapshot_List
    @CompanyId int
AS
BEGIN
    -- Instantaneele firmei (F5 3.6), cele mai noi primele.
    SET NOCOUNT ON;

    SELECT s.Id, s.Name, s.TakenAt, u.DisplayName AS TakenByName, s.StartDate, s.HorizonFrom, s.HorizonTo, s.Granularity, s.TreasuryAccountId,
           a.Code AS TreasuryAccountCode, s.CurrencyCode, s.IsAutomatic, s.OpeningBalanceRon
    FROM fin.CashFlowSnapshot AS s
    LEFT JOIN core.[User] AS u ON u.Id = s.TakenBy
    LEFT JOIN fin.TreasuryAccount AS a ON a.Id = s.TreasuryAccountId
    WHERE s.CompanyId = @CompanyId
    ORDER BY s.TakenAt DESC, s.Id DESC;
END;
