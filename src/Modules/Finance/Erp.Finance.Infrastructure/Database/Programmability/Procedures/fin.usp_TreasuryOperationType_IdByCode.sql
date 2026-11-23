CREATE OR ALTER PROCEDURE fin.usp_TreasuryOperationType_IdByCode
    @Code nvarchar(30)
AS
BEGIN
    -- Id-ul unui tip de operatiune livrat, dupa cod (de exemplu PLATA-FURNIZOR pentru OP-urile din facturi scadente).
    SET NOCOUNT ON;

    SELECT t.Id FROM fin.TreasuryOperationType AS t WHERE t.CodeKey = UPPER(@Code) AND t.IsActive = 1;
END;
