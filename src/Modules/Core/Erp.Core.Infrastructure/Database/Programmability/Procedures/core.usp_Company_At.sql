CREATE OR ALTER PROCEDURE core.usp_Company_At
    @CompanyId int,
    @Date      date
AS
BEGIN
    -- Datele firmei valabile la data unui document (ICompanyDirectory; 02.01, 3.6 punctul 2 si 9): versiunea cu ValidFrom <= @Date,
    -- altfel cea mai veche. CUI-ul si codul nu au istoric.
    SET NOCOUNT ON;

    SELECT TOP (1) c.Id AS CompanyId, c.Code, c.FiscalCode, v.Name, v.VatPayer, v.VatOnCollection, v.TradeRegisterNo, v.ShareCapital, v.Address,
           v.City, v.CitySirutaCode, v.CountyCode, v.CountryCode, v.PostalCode, v.ValidFrom
    FROM core.Company AS c
    JOIN core.CompanyVersion AS v ON v.CompanyId = c.Id
    WHERE c.Id = @CompanyId
    ORDER BY CASE WHEN v.ValidFrom <= @Date THEN 0 ELSE 1 END, CASE WHEN v.ValidFrom <= @Date THEN v.ValidFrom END DESC, v.ValidFrom;
END;
