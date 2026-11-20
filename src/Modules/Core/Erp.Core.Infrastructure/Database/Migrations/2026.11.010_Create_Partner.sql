-- Partenerii (02.01, 3.2): nomenclatorul unic de clienti si furnizori, la nivel de instalare, cu grupele, adresele,
-- conturile bancare, persoanele de contact si proprietatile configurabile (comune cu articolele, EntityType).
CREATE TABLE core.PartnerGroup (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerGroup PRIMARY KEY,
    Code       nvarchar(20)      NOT NULL,
    Name       nvarchar(100)     NOT NULL,
    SortOrder  int               NOT NULL CONSTRAINT DF_PartnerGroup_SortOrder DEFAULT (100),
    IsActive   bit               NOT NULL CONSTRAINT DF_PartnerGroup_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_PartnerGroup_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CodeKey    AS UPPER(Code) PERSISTED
);
CREATE UNIQUE INDEX UX_PartnerGroup_CodeKey ON core.PartnerGroup (CodeKey);
GO

-- PartnerType: 1 juridica RO, 2 juridica UE, 3 juridica non-UE, 4 persoana fizica. Sediul social sta pe partener (ca la firma);
-- celelalte adrese sunt in core.PartnerAddress, pe tipurile din core.AddressType.
CREATE TABLE core.Partner (
    Id                  int IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Partner PRIMARY KEY,
    Code                nvarchar(30)       NOT NULL,
    Name                nvarchar(200)      NOT NULL,
    PartnerType         tinyint            NOT NULL,
    FiscalCode          nvarchar(30)       NULL,
    PersonalIdEncrypted varbinary(256)     NULL,      -- CNP criptat cu Data Protection (GDPR, A13)
    IsCustomer          bit                NOT NULL CONSTRAINT DF_Partner_IsCustomer DEFAULT (0),
    IsSupplier          bit                NOT NULL CONSTRAINT DF_Partner_IsSupplier DEFAULT (0),
    PartnerGroupId      int                NULL CONSTRAINT FK_Partner_PartnerGroup REFERENCES core.PartnerGroup (Id),
    VatPayer            bit                NOT NULL CONSTRAINT DF_Partner_VatPayer DEFAULT (0),
    VatOnCollection     bit                NOT NULL CONSTRAINT DF_Partner_VatOnCollection DEFAULT (0),
    AnafInactive        bit                NOT NULL CONSTRAINT DF_Partner_AnafInactive DEFAULT (0),
    AnafCheckedAt       datetime2(3)       NULL,
    TradeRegisterNo     nvarchar(30)       NULL,
    Address             nvarchar(300)      NOT NULL,
    City                nvarchar(100)      NOT NULL,
    CountyCode          nvarchar(3)        NULL CONSTRAINT FK_Partner_County REFERENCES core.County (Code),
    CountryCode         char(2)            NOT NULL CONSTRAINT FK_Partner_Country REFERENCES core.Country (Code),
    PostalCode          nvarchar(10)       NULL,
    Phone               nvarchar(30)       NULL,
    Email               nvarchar(254)      NULL,
    IntraGroupCompanyId int                NULL CONSTRAINT FK_Partner_Company REFERENCES core.Company (Id),
    IsActive            bit                NOT NULL CONSTRAINT DF_Partner_IsActive DEFAULT (1),
    CreatedAt           datetime2(3)       NOT NULL CONSTRAINT DF_Partner_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int                NOT NULL,
    ModifiedAt          datetime2(3)       NULL,
    ModifiedBy          int                NULL,
    RowVersion          rowversion         NOT NULL,
    CodeKey             AS UPPER(Code) PERSISTED,                                          -- SQL-24
    NameSearch          AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,      -- SQL-25
    CONSTRAINT CK_Partner_Role CHECK (IsCustomer = 1 OR IsSupplier = 1),
    CONSTRAINT CK_Partner_Type CHECK (PartnerType BETWEEN 1 AND 4),
    CONSTRAINT CK_Partner_FiscalCode CHECK (PartnerType = 4 OR FiscalCode IS NOT NULL),
    CONSTRAINT CK_Partner_PersonalId CHECK (PartnerType = 4 OR PersonalIdEncrypted IS NULL),
    CONSTRAINT CK_Partner_VatOnCollection CHECK (VatOnCollection = 0 OR VatPayer = 1),
    CONSTRAINT CK_Partner_County CHECK (CountryCode <> 'RO' OR CountyCode IS NOT NULL)
);
CREATE UNIQUE INDEX UX_Partner_FiscalCode ON core.Partner (FiscalCode) WHERE FiscalCode IS NOT NULL;
CREATE UNIQUE INDEX UX_Partner_CodeKey ON core.Partner (CodeKey);
CREATE INDEX IX_Partner_NameSearch ON core.Partner (NameSearch) INCLUDE (Code, Name, FiscalCode, IsActive);
CREATE INDEX IX_Partner_PartnerGroupId ON core.Partner (PartnerGroupId) WHERE PartnerGroupId IS NOT NULL;
GO

-- Utilizatorii externi apartin unui partener (3.4); cheia straina amanata din 2026.11.002.
ALTER TABLE core.[User] ADD CONSTRAINT FK_User_Partner_External FOREIGN KEY (ExternalPartnerId) REFERENCES core.Partner (Id);
GO

CREATE TABLE core.PartnerAddress (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerAddress PRIMARY KEY,
    PartnerId   int               NOT NULL CONSTRAINT FK_PartnerAddress_Partner REFERENCES core.Partner (Id),
    AddressType int               NOT NULL CONSTRAINT FK_PartnerAddress_Type REFERENCES core.AddressType (Id),
    Name        nvarchar(100)     NULL,
    Address     nvarchar(300)     NOT NULL,
    City        nvarchar(100)     NOT NULL,
    CountyCode  nvarchar(3)       NULL CONSTRAINT FK_PartnerAddress_County REFERENCES core.County (Code),
    CountryCode char(2)           NOT NULL CONSTRAINT FK_PartnerAddress_Country REFERENCES core.Country (Code),
    PostalCode  nvarchar(10)      NULL,
    Phone       nvarchar(30)      NULL,
    Email       nvarchar(254)     NULL,
    IsDefault   bit               NOT NULL CONSTRAINT DF_PartnerAddress_IsDefault DEFAULT (0),
    IsActive    bit               NOT NULL CONSTRAINT DF_PartnerAddress_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_PartnerAddress_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT CK_PartnerAddress_County CHECK (CountryCode <> 'RO' OR CountyCode IS NOT NULL),
    CONSTRAINT CK_PartnerAddress_DefaultActive CHECK (IsDefault = 0 OR IsActive = 1)
);
CREATE INDEX IX_PartnerAddress_PartnerId ON core.PartnerAddress (PartnerId, AddressType);
CREATE UNIQUE INDEX UX_PartnerAddress_Default ON core.PartnerAddress (PartnerId, AddressType) WHERE IsDefault = 1;
GO

CREATE TABLE core.PartnerBankAccount (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerBankAccount PRIMARY KEY,
    PartnerId  int               NOT NULL CONSTRAINT FK_PartnerBankAccount_Partner REFERENCES core.Partner (Id),
    Iban       varchar(34)       NOT NULL,
    BankName   nvarchar(100)     NOT NULL,
    Currency   char(3)           NOT NULL,
    IsDefault  bit               NOT NULL CONSTRAINT DF_PartnerBankAccount_IsDefault DEFAULT (0),
    IsActive   bit               NOT NULL CONSTRAINT DF_PartnerBankAccount_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_PartnerBankAccount_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CONSTRAINT CK_PartnerBankAccount_DefaultActive CHECK (IsDefault = 0 OR IsActive = 1)
);
-- Acelasi IBAN o singura data la un partener; doi parteneri pot avea un cont comun (de exemplu factor, cesiune).
CREATE UNIQUE INDEX UX_PartnerBankAccount_Iban ON core.PartnerBankAccount (PartnerId, Iban);
CREATE UNIQUE INDEX UX_PartnerBankAccount_Default ON core.PartnerBankAccount (PartnerId, Currency) WHERE IsDefault = 1;
GO

CREATE TABLE core.PartnerContact (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerContact PRIMARY KEY,
    PartnerId  int               NOT NULL CONSTRAINT FK_PartnerContact_Partner REFERENCES core.Partner (Id),
    Name       nvarchar(150)     NOT NULL,
    Role       nvarchar(100)     NULL,
    Phone      nvarchar(30)      NULL,
    Email      nvarchar(254)     NULL,
    Notes      nvarchar(500)     NULL,
    IsActive   bit               NOT NULL CONSTRAINT DF_PartnerContact_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_PartnerContact_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CONSTRAINT CK_PartnerContact_Reach CHECK (Phone IS NOT NULL OR Email IS NOT NULL)
);
CREATE INDEX IX_PartnerContact_PartnerId ON core.PartnerContact (PartnerId);
GO

-- Proprietatile configurabile (02.01, 3.2). DataType: 1 text, 2 numar, 3 data, 4 da/nu, 5 lista, 6 lista multipla,
-- 7 utilizator. AppliesTo: 1 client, 2 furnizor, 3 ambele (pentru articole: 3). CompanyId NULL = tot grupul.
CREATE TABLE core.PropertyDefinition (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PropertyDefinition PRIMARY KEY,
    EntityType   varchar(20)       NOT NULL,
    Code         nvarchar(30)      NOT NULL,
    Name         nvarchar(100)     NOT NULL,
    DataType     tinyint           NOT NULL,
    AppliesTo    tinyint           NOT NULL CONSTRAINT DF_PropertyDefinition_AppliesTo DEFAULT (3),
    Section      nvarchar(50)      NOT NULL,
    SortOrder    int               NOT NULL CONSTRAINT DF_PropertyDefinition_SortOrder DEFAULT (100),
    IsRequired   bit               NOT NULL CONSTRAINT DF_PropertyDefinition_IsRequired DEFAULT (0),
    IsFilterable bit               NOT NULL CONSTRAINT DF_PropertyDefinition_IsFilterable DEFAULT (0),
    CompanyId    int               NULL CONSTRAINT FK_PropertyDefinition_Company REFERENCES core.Company (Id),
    IsActive     bit               NOT NULL CONSTRAINT DF_PropertyDefinition_IsActive DEFAULT (1),
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_PropertyDefinition_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    ModifiedAt   datetime2(3)      NULL,
    ModifiedBy   int               NULL,
    RowVersion   rowversion        NOT NULL,
    CodeKey      AS UPPER(Code) PERSISTED,
    CONSTRAINT CK_PropertyDefinition_EntityType CHECK (EntityType IN ('Partner', 'Item')),
    CONSTRAINT CK_PropertyDefinition_DataType CHECK (DataType BETWEEN 1 AND 7),
    CONSTRAINT CK_PropertyDefinition_AppliesTo CHECK (AppliesTo BETWEEN 1 AND 3)
);
CREATE UNIQUE INDEX UX_PropertyDefinition_Code ON core.PropertyDefinition (EntityType, CompanyId, CodeKey);
CREATE INDEX IX_PropertyDefinition_EntityType ON core.PropertyDefinition (EntityType, IsActive);
GO

CREATE TABLE core.PropertyOption (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PropertyOption PRIMARY KEY,
    PropertyDefinitionId int               NOT NULL CONSTRAINT FK_PropertyOption_Definition REFERENCES core.PropertyDefinition (Id),
    Code                 nvarchar(30)      NOT NULL,
    Label                nvarchar(100)     NOT NULL,
    SortOrder            int               NOT NULL CONSTRAINT DF_PropertyOption_SortOrder DEFAULT (100),
    IsActive             bit               NOT NULL CONSTRAINT DF_PropertyOption_IsActive DEFAULT (1),
    CreatedAt            datetime2(3)      NOT NULL CONSTRAINT DF_PropertyOption_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int               NOT NULL,
    ModifiedAt           datetime2(3)      NULL,
    ModifiedBy           int               NULL,
    RowVersion           rowversion        NOT NULL,
    CodeKey              AS UPPER(Code) PERSISTED,
    -- Cheia compusa lasa PropertyValue sa verifice ca optiunea apartine proprietatii.
    CONSTRAINT UQ_PropertyOption_Definition UNIQUE (Id, PropertyDefinitionId)
);
CREATE UNIQUE INDEX UX_PropertyOption_Code ON core.PropertyOption (PropertyDefinitionId, CodeKey);
GO

-- O singura coloana de valoare completata, dupa tip; listele multiple au cate un rand pe optiune.
-- ValueUserId (proprietatile de tip utilizator, de exemplu agentul de vanzari) completeaza coloanele din 3.2.
CREATE TABLE core.PropertyValue (
    Id                   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PropertyValue PRIMARY KEY,
    PropertyDefinitionId int                  NOT NULL CONSTRAINT FK_PropertyValue_Definition REFERENCES core.PropertyDefinition (Id),
    EntityId             int                  NOT NULL,
    ValueText            nvarchar(1000)       NULL,
    ValueNumber          decimal(19,6)        NULL,
    ValueDate            date                 NULL,
    ValueBool            bit                  NULL,
    ValueOptionId        int                  NULL,
    ValueUserId          int                  NULL CONSTRAINT FK_PropertyValue_User REFERENCES core.[User] (Id),
    CreatedAt            datetime2(3)         NOT NULL CONSTRAINT DF_PropertyValue_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int                  NOT NULL,
    CONSTRAINT FK_PropertyValue_Option FOREIGN KEY (ValueOptionId, PropertyDefinitionId) REFERENCES core.PropertyOption (Id, PropertyDefinitionId),
    CONSTRAINT CK_PropertyValue_Single CHECK (
        (CASE WHEN ValueText IS NULL THEN 0 ELSE 1 END) + (CASE WHEN ValueNumber IS NULL THEN 0 ELSE 1 END)
        + (CASE WHEN ValueDate IS NULL THEN 0 ELSE 1 END) + (CASE WHEN ValueBool IS NULL THEN 0 ELSE 1 END)
        + (CASE WHEN ValueOptionId IS NULL THEN 0 ELSE 1 END) + (CASE WHEN ValueUserId IS NULL THEN 0 ELSE 1 END) = 1)
);
CREATE INDEX IX_PropertyValue_Definition ON core.PropertyValue (PropertyDefinitionId, EntityId) INCLUDE (ValueOptionId);
CREATE INDEX IX_PropertyValue_EntityId ON core.PropertyValue (EntityId);
GO

-- Lista de valori trimisa la salvarea fisei (TVP pentru liste).
CREATE TYPE core.PropertyValueList AS TABLE (
    PropertyDefinitionId int            NOT NULL,
    ValueText            nvarchar(1000) NULL,
    ValueNumber          decimal(19,6)  NULL,
    ValueDate            date           NULL,
    ValueBool            bit            NULL,
    ValueOptionId        int            NULL,
    ValueUserId          int            NULL
);
GO
