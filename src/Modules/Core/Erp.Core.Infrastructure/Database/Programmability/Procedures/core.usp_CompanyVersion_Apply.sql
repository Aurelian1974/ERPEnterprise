CREATE OR ALTER PROCEDURE core.usp_CompanyVersion_Apply
    @Today     date,
    @CompanyId int = NULL
AS
BEGIN
    -- Aduce datele firmei (o firma sau toate) la versiunea valabila azi (02.01, 3.6 punctul 2): ultima versiune cu ValidFrom <= @Today.
    -- Apelata la salvarea firmei si de jobul zilnic, pentru versiunile programate. Intoarce firmele schimbate.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE c
    SET Name = v.Name, VatPayer = v.VatPayer, VatOnCollection = v.VatOnCollection, TradeRegisterNo = v.TradeRegisterNo, ShareCapital = v.ShareCapital,
        Address = v.Address, City = v.City, CitySirutaCode = v.CitySirutaCode, CountyCode = v.CountyCode, CountryCode = v.CountryCode,
        PostalCode = v.PostalCode, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = v.ModifiedBy
    OUTPUT inserted.Id AS CompanyId
    FROM core.Company AS c
    CROSS APPLY (SELECT TOP (1) x.* FROM core.CompanyVersion AS x WHERE x.CompanyId = c.Id AND x.ValidFrom <= @Today ORDER BY x.ValidFrom DESC) AS v
    WHERE (@CompanyId IS NULL OR c.Id = @CompanyId)
      AND (c.Name <> v.Name OR c.VatPayer <> v.VatPayer OR c.VatOnCollection <> v.VatOnCollection OR c.TradeRegisterNo <> v.TradeRegisterNo
           OR ISNULL(c.ShareCapital, -1) <> ISNULL(v.ShareCapital, -1) OR c.Address <> v.Address OR c.City <> v.City
           OR ISNULL(c.CitySirutaCode, 0) <> ISNULL(v.CitySirutaCode, 0) OR ISNULL(c.CountyCode, N'') <> ISNULL(v.CountyCode, N'')
           OR c.CountryCode <> v.CountryCode OR ISNULL(c.PostalCode, N'') <> ISNULL(v.PostalCode, N''));
END;
