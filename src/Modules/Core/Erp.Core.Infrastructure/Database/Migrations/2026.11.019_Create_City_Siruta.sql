-- Localitatile SIRUTA (02.01, 3.5; e-Factura cere adrese corecte): nomenclatorul core.City, cu datele livrate in 14_Cities.sql
-- (generat cu build/siruta-seed.mjs din fisierul INS). Cheia e codul SIRUTA (abaterea de la SQL-13 din 02.01, 7). Nivelul 2 =
-- unitatea administrativ-teritoriala (municipiu, oras, comuna, Bucuresti), nivelul 3 = localitatea (sat, localitate componenta,
-- sectorul Bucurestiului), cu unitatea ei in ParentSirutaCode. Adresele pastreaza textul localitatii (si pentru strainatate) si
-- primesc codul SIRUTA optional; procedura core.usp_City_Resolve il verifica si il completeaza la salvare.
CREATE TABLE core.City (
    SirutaCode       int            NOT NULL CONSTRAINT PK_City PRIMARY KEY,
    Name             nvarchar(100)  NOT NULL,
    NameSearch       AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,
    CountyCode       nvarchar(3)    NOT NULL CONSTRAINT FK_City_County REFERENCES core.County (Code),
    ParentSirutaCode int            NULL,
    Kind             tinyint        NOT NULL,
    [Level]          tinyint        NOT NULL CONSTRAINT CK_City_Level CHECK ([Level] IN (2, 3)),
    PostalCode       char(6)        NULL,
    IsActive         bit            NOT NULL CONSTRAINT DF_City_IsActive DEFAULT (1),
    CreatedAt        datetime2(3)   NOT NULL CONSTRAINT DF_City_CreatedAt DEFAULT (sysutcdatetime())
);
GO

-- Cautarea din formulare: judetul, apoi inceputul numelui fara diacritice si majuscule (SQL-25).
CREATE INDEX IX_City_CountyCode_NameSearch ON core.City (CountyCode, [Level], NameSearch) INCLUDE (Name, ParentSirutaCode, Kind, PostalCode, IsActive);
CREATE INDEX IX_City_ParentSirutaCode ON core.City (ParentSirutaCode);
GO

ALTER TABLE core.Company ADD CitySirutaCode int NULL CONSTRAINT FK_Company_City REFERENCES core.City (SirutaCode);
ALTER TABLE core.Branch ADD CitySirutaCode int NULL CONSTRAINT FK_Branch_City REFERENCES core.City (SirutaCode);
ALTER TABLE core.Location ADD CitySirutaCode int NULL CONSTRAINT FK_Location_City REFERENCES core.City (SirutaCode);
ALTER TABLE core.CompanyAddress ADD CitySirutaCode int NULL CONSTRAINT FK_CompanyAddress_City REFERENCES core.City (SirutaCode);
ALTER TABLE core.Partner ADD CitySirutaCode int NULL CONSTRAINT FK_Partner_City REFERENCES core.City (SirutaCode);
ALTER TABLE core.PartnerAddress ADD CitySirutaCode int NULL CONSTRAINT FK_PartnerAddress_City REFERENCES core.City (SirutaCode);
GO
