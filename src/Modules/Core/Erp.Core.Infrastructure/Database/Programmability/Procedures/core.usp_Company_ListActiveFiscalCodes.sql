CREATE OR ALTER PROCEDURE core.usp_Company_ListActiveFiscalCodes
AS
BEGIN
    -- Firmele active si CUI-urile lor (08, 4.3 pasul 7): aplicatia verifica la pornire si zilnic ca toate sunt in licenta.
    SET NOCOUNT ON;

    SELECT c.Id, c.FiscalCode
    FROM core.Company AS c
    WHERE c.IsActive = 1;
END;
