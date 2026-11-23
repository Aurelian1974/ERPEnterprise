CREATE OR ALTER PROCEDURE fin.usp_TreasuryOperationType_Kinds
AS
BEGIN
    -- Decontarea si sensul tipurilor de operatiuni cu decontare (F3 4.2), pentru legarea documentelor de plata de partide.
    SET NOCOUNT ON;

    SELECT t.Code, t.SettlementKind, t.Direction FROM fin.TreasuryOperationType AS t WHERE t.SettlementKind <> 0;
END;
