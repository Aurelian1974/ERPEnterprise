CREATE OR ALTER PROCEDURE core.usp_Currency_List
AS
BEGIN
    -- Nomenclatorul de valute (02.01, 3.5) pentru listele de alegere (moneda contului bancar, a documentelor): RON intai, apoi activele.
    SET NOCOUNT ON;

    SELECT c.Code, c.Name, c.Decimals, c.IsActive, c.RowVersion
    FROM core.Currency AS c
    ORDER BY CASE WHEN c.Code = 'RON' THEN 0 ELSE 1 END, c.IsActive DESC, c.Code;
END;
