-- Tipurile de adrese ale firmei (configurabile in Administrare > Configurare). Cele de sistem (IsSystem) vin la instalare;
-- codul lor tehnic nu se schimba.
CREATE TABLE core.AddressType (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AddressType PRIMARY KEY,
    Code       nvarchar(30)      NOT NULL CONSTRAINT UQ_AddressType_Code UNIQUE,
    Name       nvarchar(100)     NOT NULL,
    SortOrder  int               NOT NULL CONSTRAINT DF_AddressType_SortOrder DEFAULT (100),
    IsSystem   bit               NOT NULL CONSTRAINT DF_AddressType_IsSystem DEFAULT (0),
    IsActive   bit               NOT NULL CONSTRAINT DF_AddressType_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_AddressType_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL CONSTRAINT DF_AddressType_CreatedBy DEFAULT (0),
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL
);
GO
INSERT INTO core.AddressType (Code, Name, SortOrder, IsSystem) VALUES
    (N'DELIVERY', N'Livrare', 10, 1), (N'BILLING', N'Facturare', 20, 1), (N'CORRESPONDENCE', N'Corespondență', 30, 1), (N'OTHER', N'Altă adresă', 40, 1);
GO
-- Adresele firmei pe tipuri (02.01, 3.1). Sediul social ramane pe core.Company (unic si obligatoriu);
-- aici sunt celelalte adrese, pe tipurile din core.AddressType.
CREATE TABLE core.CompanyAddress (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyAddress PRIMARY KEY,
    CompanyId   int               NOT NULL CONSTRAINT FK_CompanyAddress_Company REFERENCES core.Company (Id),
    AddressType int               NOT NULL CONSTRAINT FK_CompanyAddress_Type REFERENCES core.AddressType (Id),
    Name        nvarchar(100)     NULL,
    Address     nvarchar(300)     NOT NULL,
    City        nvarchar(100)     NOT NULL,
    CountyCode  nvarchar(3)       NULL CONSTRAINT FK_CompanyAddress_County REFERENCES core.County (Code),
    CountryCode char(2)           NOT NULL CONSTRAINT FK_CompanyAddress_Country REFERENCES core.Country (Code),
    PostalCode  nvarchar(10)      NULL,
    Phone       nvarchar(30)      NULL,
    Email       nvarchar(254)     NULL,
    IsDefault   bit               NOT NULL CONSTRAINT DF_CompanyAddress_IsDefault DEFAULT (0),
    IsActive    bit               NOT NULL CONSTRAINT DF_CompanyAddress_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_CompanyAddress_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT CK_CompanyAddress_County CHECK (CountryCode <> 'RO' OR CountyCode IS NOT NULL),
    CONSTRAINT CK_CompanyAddress_DefaultActive CHECK (IsDefault = 0 OR IsActive = 1)
);
CREATE INDEX IX_CompanyAddress_CompanyId ON core.CompanyAddress (CompanyId, AddressType);
-- O singura adresa implicita pe firma si tip (cea folosita de documente).
CREATE UNIQUE INDEX UX_CompanyAddress_Default ON core.CompanyAddress (CompanyId, AddressType) WHERE IsDefault = 1;
GO
-- Date de contact ale firmei (fisa firmei, Date generale).
ALTER TABLE core.Company ADD Phone nvarchar(30) NULL, Email nvarchar(254) NULL;
GO
-- Date de contact ale punctelor de lucru si locatiilor.
ALTER TABLE core.Branch ADD Phone nvarchar(30) NULL, Email nvarchar(254) NULL;
ALTER TABLE core.Location ADD Phone nvarchar(30) NULL, Email nvarchar(254) NULL;
GO
-- Persoanele de contact ale firmei (02.01, 3.1).
CREATE TABLE core.CompanyContact (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyContact PRIMARY KEY,
    CompanyId  int               NOT NULL CONSTRAINT FK_CompanyContact_Company REFERENCES core.Company (Id),
    Name       nvarchar(150)     NOT NULL,
    Role       nvarchar(100)     NULL,
    Phone      nvarchar(30)      NULL,
    Email      nvarchar(254)     NULL,
    Notes      nvarchar(500)     NULL,
    BranchId   int               NULL CONSTRAINT FK_CompanyContact_Branch REFERENCES core.Branch (Id),
    LocationId int               NULL CONSTRAINT FK_CompanyContact_Location REFERENCES core.Location (Id),
    IsActive   bit               NOT NULL CONSTRAINT DF_CompanyContact_IsActive     DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_CompanyContact_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CONSTRAINT CK_CompanyContact_Reach CHECK (Phone IS NOT NULL OR Email IS NOT NULL),
    CONSTRAINT CK_CompanyContact_Owner CHECK (BranchId IS NULL OR LocationId IS NULL)
);
CREATE INDEX IX_CompanyContact_CompanyId ON core.CompanyContact (CompanyId);
GO
