CREATE OR ALTER PROCEDURE core.usp_CompanyVersion_List
    @CompanyId int
AS
BEGIN
    -- Datele firmei pe perioade (02.01, 3.6 punctul 2), cele mai noi intai; ValidTo = ziua dinaintea versiunii urmatoare.
    SET NOCOUNT ON;

    SELECT v.Id, v.ValidFrom, DATEADD(DAY, -1, LEAD(v.ValidFrom) OVER (ORDER BY v.ValidFrom)) AS ValidTo, v.Name, v.VatPayer, v.VatOnCollection,
           v.TradeRegisterNo, v.ShareCapital, v.Address, v.City, v.CountyCode, v.CountryCode, v.PostalCode, u.DisplayName AS ModifiedByName, v.ModifiedAt
    FROM core.CompanyVersion AS v
    JOIN core.[User] AS u ON u.Id = v.ModifiedBy
    WHERE v.CompanyId = @CompanyId
    ORDER BY v.ValidFrom DESC;
END;
