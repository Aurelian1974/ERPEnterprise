CREATE OR ALTER PROCEDURE fin.usp_CashRegisterDay_ListAutoClose
    @UpTo date
AS
BEGIN
    -- Zilele deschise pana la @UpTo (ziua precedenta) ale caselor active cu inchidere automata (02.02, F1 4.2), in ordinea zilelor pe
    -- fiecare casa: jobul de la miezul noptii le inchide pe rand si se opreste la prima zi care nu indeplineste conditiile.
    SET NOCOUNT ON;

    SELECT a.Id AS TreasuryAccountId, a.CompanyId, a.Code, r.[Day]
    FROM fin.TreasuryAccount AS a
    JOIN fin.CashRegisterDay AS r ON r.TreasuryAccountId = a.Id
    WHERE a.Kind = 2 AND a.IsActive = 1 AND a.CloseMode = 2 AND r.Status = 1 AND r.[Day] <= @UpTo
    ORDER BY a.Id, r.[Day];
END;
