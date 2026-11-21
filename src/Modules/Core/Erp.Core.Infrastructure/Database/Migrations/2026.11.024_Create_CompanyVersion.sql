-- Datele firmei cu data de la care se aplica (02.01, 3.6 punctul 2; decizia lui Aurelian din 21.11.2026): denumirea, sediul,
-- regimul TVA, registrul comertului si capitalul social au istoric pe intervale. core.Company pastreaza versiunea valabila azi;
-- o versiune viitoare se aplica de jobul zilnic (core.usp_CompanyVersion_Apply), iar documentele iau datele valabile la data lor.
-- Versiunea initiala a fiecarei firme existente incepe la 01.01.1900 (valabila pentru orice document vechi).
CREATE TABLE core.CompanyVersion (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyVersion PRIMARY KEY,
    CompanyId       int               NOT NULL CONSTRAINT FK_CompanyVersion_Company REFERENCES core.Company (Id),
    ValidFrom       date              NOT NULL,
    Name            nvarchar(200)     NOT NULL,
    VatPayer        bit               NOT NULL,
    VatOnCollection bit               NOT NULL,
    TradeRegisterNo nvarchar(30)      NOT NULL,
    ShareCapital    decimal(19,2)     NULL,
    Address         nvarchar(300)     NOT NULL,
    City            nvarchar(100)     NOT NULL,
    CitySirutaCode  int               NULL CONSTRAINT FK_CompanyVersion_City REFERENCES core.City (SirutaCode),
    CountyCode      nvarchar(3)       NULL CONSTRAINT FK_CompanyVersion_County REFERENCES core.County (Code),
    CountryCode     char(2)           NOT NULL CONSTRAINT FK_CompanyVersion_Country REFERENCES core.Country (Code),
    PostalCode      nvarchar(10)      NULL,
    ModifiedAt      datetime2(3)      NOT NULL,
    ModifiedBy      int               NOT NULL CONSTRAINT FK_CompanyVersion_User REFERENCES core.[User] (Id),
    CONSTRAINT UQ_CompanyVersion UNIQUE (CompanyId, ValidFrom)
);
GO

INSERT INTO core.CompanyVersion (CompanyId, ValidFrom, Name, VatPayer, VatOnCollection, TradeRegisterNo, ShareCapital, Address, City, CitySirutaCode,
    CountyCode, CountryCode, PostalCode, ModifiedAt, ModifiedBy)
SELECT c.Id, '19000101', c.Name, c.VatPayer, c.VatOnCollection, c.TradeRegisterNo, c.ShareCapital, c.Address, c.City, c.CitySirutaCode, c.CountyCode,
       c.CountryCode, c.PostalCode, SYSUTCDATETIME(), c.CreatedBy
FROM core.Company AS c;
GO
