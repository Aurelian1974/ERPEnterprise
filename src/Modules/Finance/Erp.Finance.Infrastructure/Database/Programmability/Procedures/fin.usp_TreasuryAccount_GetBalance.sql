CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccount_GetBalance
    @TreasuryAccountId int,
    @Day               date
AS
BEGIN
    -- Soldul unui cont de trezorerie la sfarsitul zilei @Day (F1 6): soldul final al ultimei zile cu activitate pana la ea; ziua e inchisa
    -- daca are rand inchis. Pentru valuta, echivalentul in lei se calculeaza in C# cu cursul zilei.
    SET NOCOUNT ON;

    SELECT a.Id AS TreasuryAccountId, a.CompanyId, a.CurrencyCode, ISNULL(last.ClosingBalance, 0) AS Balance,
           CAST(CASE WHEN EXISTS (SELECT 1 FROM fin.CashRegisterDay AS r WHERE r.TreasuryAccountId = a.Id AND r.[Day] = @Day AND r.Status = 2)
                THEN 1 ELSE 0 END AS bit) AS IsDayClosed
    FROM fin.TreasuryAccount AS a
    OUTER APPLY (SELECT TOP (1) r.ClosingBalance FROM fin.CashRegisterDay AS r
                 WHERE r.TreasuryAccountId = a.Id AND r.[Day] <= @Day ORDER BY r.[Day] DESC) AS last
    WHERE a.Id = @TreasuryAccountId;
END;
