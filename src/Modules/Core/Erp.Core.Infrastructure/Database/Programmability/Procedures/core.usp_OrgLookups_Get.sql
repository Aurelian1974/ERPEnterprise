CREATE OR ALTER PROCEDURE core.usp_OrgLookups_Get
AS
BEGIN
    -- Listele pentru formularele structurii si ale seriilor: tari, judete, politici de zecimale, tipuri de documente.
    SET NOCOUNT ON;

    SELECT Code, Name FROM core.Country WHERE IsActive = 1 ORDER BY CASE WHEN Code = 'RO' THEN 0 ELSE 1 END, Name;
    SELECT Code, CountryCode, Name FROM core.County WHERE IsActive = 1 ORDER BY Name;
    SELECT Id, Code, Name FROM core.PrecisionPolicy WHERE IsActive = 1 ORDER BY Name;
    SELECT Code, ModuleCode, Name, IsFiscalDefault FROM core.DocumentType ORDER BY Name;
    SELECT Id, Code, Name, SortOrder, IsSystem, IsActive, RowVersion FROM core.AddressType ORDER BY SortOrder, Name;
END;
