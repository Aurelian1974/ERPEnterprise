-- Nucleu, etapa 4 (02.01, 3.1, 3.5, 4.2 si 7): structura organizatorica (grup -> firma -> punct de lucru -> locatie),
-- conturile bancare ale firmei, tipurile de documente si seriile de numerotare. Tarile, judetele si politica de zecimale
-- se creeaza aici pentru ca firma le refera; ecranele lor vin in etapele 5 (nomenclatoare) si 9 (precizie).
-- Localitatea ramane text pana la nomenclatorul SIRUTA (etapa 5); moneda contului bancar primeste FK spre core.Currency
-- odata cu nomenclatorul de valute (etapa 5).

CREATE TABLE core.Country (
    Code       char(2)        NOT NULL CONSTRAINT PK_Country PRIMARY KEY,     -- ISO 3166-1 alfa-2 (abatere SQL-13, 7)
    Name       nvarchar(100)  NOT NULL,
    IsEu       bit            NOT NULL CONSTRAINT DF_Country_IsEu DEFAULT (0),
    IsActive   bit            NOT NULL CONSTRAINT DF_Country_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)   NOT NULL CONSTRAINT DF_Country_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int            NOT NULL,
    ModifiedAt datetime2(3)   NULL,
    ModifiedBy int            NULL,
    RowVersion rowversion     NOT NULL,
    CONSTRAINT CK_Country_Code CHECK (Code LIKE '[A-Z][A-Z]')
);
GO

CREATE TABLE core.County (
    Code        nvarchar(3)    NOT NULL CONSTRAINT PK_County PRIMARY KEY,     -- indicativul judetului (AB, ..., B), ISO 3166-2:RO
    CountryCode char(2)        NOT NULL CONSTRAINT FK_County_Country REFERENCES core.Country (Code),
    Name        nvarchar(100)  NOT NULL,
    IsActive    bit            NOT NULL CONSTRAINT DF_County_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)   NOT NULL CONSTRAINT DF_County_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int            NOT NULL,
    ModifiedAt  datetime2(3)   NULL,
    ModifiedBy  int            NULL,
    RowVersion  rowversion     NOT NULL
);
CREATE INDEX IX_County_CountryCode ON core.County (CountryCode);
GO

CREATE TABLE core.PrecisionPolicy (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PrecisionPolicy PRIMARY KEY,
    Code              nvarchar(20)      NOT NULL,
    Name              nvarchar(100)     NOT NULL,
    PricePrecision    tinyint           NOT NULL,
    QuantityPrecision tinyint           NOT NULL,
    AmountPrecision   tinyint           NOT NULL CONSTRAINT DF_PrecisionPolicy_AmountPrecision DEFAULT (2),
    RoundingMode      tinyint           NOT NULL CONSTRAINT DF_PrecisionPolicy_RoundingMode DEFAULT (1),  -- System.MidpointRounding: 1 = AwayFromZero
    IsActive          bit               NOT NULL CONSTRAINT DF_PrecisionPolicy_IsActive DEFAULT (1),
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_PrecisionPolicy_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CONSTRAINT UQ_PrecisionPolicy_Code UNIQUE (Code),
    CONSTRAINT CK_PrecisionPolicy_Price CHECK (PricePrecision BETWEEN 0 AND 6),
    CONSTRAINT CK_PrecisionPolicy_Quantity CHECK (QuantityPrecision BETWEEN 0 AND 6),
    CONSTRAINT CK_PrecisionPolicy_Amount CHECK (AmountPrecision = 2),
    CONSTRAINT CK_PrecisionPolicy_Rounding CHECK (RoundingMode BETWEEN 0 AND 4)
);
GO

CREATE TABLE core.CompanyGroup (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyGroup PRIMARY KEY,
    Code            nvarchar(20)      NOT NULL,
    Name            nvarchar(200)     NOT NULL,
    ParentGroupId   int               NULL CONSTRAINT FK_CompanyGroup_CompanyGroup_Parent REFERENCES core.CompanyGroup (Id),
    ParentCompanyId int               NULL,       -- FK dupa crearea core.Company
    IsActive        bit               NOT NULL CONSTRAINT DF_CompanyGroup_IsActive DEFAULT (1),
    CreatedAt       datetime2(3)      NOT NULL CONSTRAINT DF_CompanyGroup_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy       int               NOT NULL,
    ModifiedAt      datetime2(3)      NULL,
    ModifiedBy      int               NULL,
    RowVersion      rowversion        NOT NULL,
    CodeKey         AS UPPER(Code) PERSISTED,                                  -- SQL-24
    CONSTRAINT CK_CompanyGroup_NotOwnParent CHECK (ParentGroupId IS NULL OR ParentGroupId <> Id)
);
CREATE UNIQUE INDEX UX_CompanyGroup_CodeKey ON core.CompanyGroup (CodeKey);
CREATE INDEX IX_CompanyGroup_ParentGroupId ON core.CompanyGroup (ParentGroupId);
GO

CREATE TABLE core.Company (
    Id                       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Company PRIMARY KEY,
    GroupId                  int               NULL CONSTRAINT FK_Company_CompanyGroup REFERENCES core.CompanyGroup (Id),
    Code                     nvarchar(20)      NOT NULL,
    Name                     nvarchar(200)     NOT NULL,
    FiscalCode               nvarchar(20)      NOT NULL,      -- CUI fara prefixul RO; prefixul rezulta din VatPayer
    VatPayer                 bit               NOT NULL,
    VatOnCollection          bit               NOT NULL CONSTRAINT DF_Company_VatOnCollection DEFAULT (0),
    TradeRegisterNo          nvarchar(30)      NOT NULL,
    ShareCapital             decimal(19,2)     NULL,
    Address                  nvarchar(300)     NOT NULL,
    City                     nvarchar(100)     NOT NULL,
    CountyCode               nvarchar(3)       NULL CONSTRAINT FK_Company_County REFERENCES core.County (Code),
    CountryCode              char(2)           NOT NULL CONSTRAINT FK_Company_Country REFERENCES core.Country (Code),
    PostalCode               nvarchar(10)      NULL,
    FunctionalCurrency       char(3)           NOT NULL CONSTRAINT DF_Company_FunctionalCurrency DEFAULT ('RON'),
    DefaultPrecisionPolicyId int               NOT NULL CONSTRAINT FK_Company_PrecisionPolicy REFERENCES core.PrecisionPolicy (Id),
    LogoAttachmentId         int               NULL,          -- FK spre core.Attachment odata cu atasamentele (etapa 10)
    IncludeInConsolidation   bit               NOT NULL CONSTRAINT DF_Company_IncludeInConsolidation DEFAULT (1),
    IsActive                 bit               NOT NULL CONSTRAINT DF_Company_IsActive DEFAULT (1),
    CreatedAt                datetime2(3)      NOT NULL CONSTRAINT DF_Company_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy                int               NOT NULL,
    ModifiedAt               datetime2(3)      NULL,
    ModifiedBy               int               NULL,
    RowVersion               rowversion        NOT NULL,
    CodeKey                  AS UPPER(Code) PERSISTED,
    NameSearch               AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,   -- SQL-25
    CONSTRAINT UQ_Company_FiscalCode UNIQUE (FiscalCode),
    CONSTRAINT CK_Company_FiscalCode CHECK (FiscalCode NOT LIKE '%[^0-9]%' AND LEN(FiscalCode) BETWEEN 2 AND 10),
    CONSTRAINT CK_Company_FunctionalCurrency CHECK (FunctionalCurrency = 'RON'),       -- perimetru (02.01, 3.1)
    CONSTRAINT CK_Company_ShareCapital CHECK (ShareCapital IS NULL OR ShareCapital > 0),
    CONSTRAINT CK_Company_County CHECK (CountryCode <> 'RO' OR CountyCode IS NOT NULL)
);
CREATE UNIQUE INDEX UX_Company_CodeKey ON core.Company (CodeKey);
CREATE INDEX IX_Company_GroupId ON core.Company (GroupId);
CREATE INDEX IX_Company_NameSearch ON core.Company (NameSearch) INCLUDE (Code, Name, FiscalCode, IsActive);
GO

ALTER TABLE core.CompanyGroup ADD CONSTRAINT FK_CompanyGroup_Company_Parent FOREIGN KEY (ParentCompanyId) REFERENCES core.Company (Id);
ALTER TABLE core.[User] ADD CONSTRAINT FK_User_Company_Last FOREIGN KEY (LastCompanyId) REFERENCES core.Company (Id);
ALTER TABLE core.UserRoleAssignment ADD CONSTRAINT FK_UserRoleAssignment_Company FOREIGN KEY (CompanyId) REFERENCES core.Company (Id);
ALTER TABLE core.AuditEntry ADD CONSTRAINT FK_AuditEntry_Company FOREIGN KEY (CompanyId) REFERENCES core.Company (Id);
CREATE INDEX IX_CompanyGroup_ParentCompanyId ON core.CompanyGroup (ParentCompanyId) WHERE ParentCompanyId IS NOT NULL;
GO

CREATE TABLE core.Branch (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branch PRIMARY KEY,
    CompanyId   int               NOT NULL CONSTRAINT FK_Branch_Company REFERENCES core.Company (Id),
    Code        nvarchar(20)      NOT NULL,
    Name        nvarchar(200)     NOT NULL,
    Address     nvarchar(300)     NOT NULL,
    City        nvarchar(100)     NOT NULL,
    CountyCode  nvarchar(3)       NULL CONSTRAINT FK_Branch_County REFERENCES core.County (Code),
    CountryCode char(2)           NOT NULL CONSTRAINT FK_Branch_Country REFERENCES core.Country (Code),
    PostalCode  nvarchar(10)      NULL,
    IsActive    bit               NOT NULL CONSTRAINT DF_Branch_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_Branch_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CodeKey     AS UPPER(Code) PERSISTED,
    -- Tinta cheilor compuse (BranchId, CompanyId): locatia si seria apartin firmei punctului de lucru.
    CONSTRAINT UQ_Branch_Id_CompanyId UNIQUE (Id, CompanyId),
    CONSTRAINT CK_Branch_County CHECK (CountryCode <> 'RO' OR CountyCode IS NOT NULL)
);
CREATE UNIQUE INDEX UX_Branch_CompanyId_CodeKey ON core.Branch (CompanyId, CodeKey);
GO

CREATE TABLE core.Location (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Location PRIMARY KEY,
    CompanyId    int               NOT NULL CONSTRAINT FK_Location_Company REFERENCES core.Company (Id),
    BranchId     int               NULL,
    Code         nvarchar(20)      NOT NULL,
    Name         nvarchar(200)     NOT NULL,
    LocationType tinyint           NOT NULL,     -- 1 birou, 2 depozit, 3 magazin, 4 fabrica, 5 santier, 6 punct de livrare
    Address      nvarchar(300)     NULL,
    City         nvarchar(100)     NULL,
    CountyCode   nvarchar(3)       NULL CONSTRAINT FK_Location_County REFERENCES core.County (Code),
    CountryCode  char(2)           NULL CONSTRAINT FK_Location_Country REFERENCES core.Country (Code),
    PostalCode   nvarchar(10)      NULL,
    IsActive     bit               NOT NULL CONSTRAINT DF_Location_IsActive DEFAULT (1),
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_Location_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    ModifiedAt   datetime2(3)      NULL,
    ModifiedBy   int               NULL,
    RowVersion   rowversion        NOT NULL,
    CodeKey      AS UPPER(Code) PERSISTED,
    CONSTRAINT FK_Location_Branch FOREIGN KEY (BranchId, CompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT CK_Location_Type CHECK (LocationType BETWEEN 1 AND 6)
);
CREATE UNIQUE INDEX UX_Location_CompanyId_CodeKey ON core.Location (CompanyId, CodeKey);
CREATE INDEX IX_Location_CompanyId_BranchId ON core.Location (CompanyId, BranchId);
GO

CREATE TABLE core.CompanyBankAccount (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyBankAccount PRIMARY KEY,
    CompanyId  int               NOT NULL CONSTRAINT FK_CompanyBankAccount_Company REFERENCES core.Company (Id),
    Iban       varchar(34)       NOT NULL,       -- fara spatii, majuscule (ISO 13616)
    BankName   nvarchar(100)     NOT NULL,
    Currency   char(3)           NOT NULL,
    IsDefault  bit               NOT NULL CONSTRAINT DF_CompanyBankAccount_IsDefault DEFAULT (0),
    IsActive   bit               NOT NULL CONSTRAINT DF_CompanyBankAccount_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_CompanyBankAccount_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CONSTRAINT UQ_CompanyBankAccount_Iban UNIQUE (Iban),
    CONSTRAINT CK_CompanyBankAccount_Iban CHECK (Iban LIKE '[A-Z][A-Z][0-9][0-9]%' AND Iban NOT LIKE '%[^A-Z0-9]%'),
    CONSTRAINT CK_CompanyBankAccount_Currency CHECK (Currency LIKE '[A-Z][A-Z][A-Z]'),
    CONSTRAINT CK_CompanyBankAccount_DefaultActive CHECK (IsDefault = 0 OR IsActive = 1)
);
CREATE INDEX IX_CompanyBankAccount_CompanyId ON core.CompanyBankAccount (CompanyId);
-- Un singur cont implicit pe firma si moneda (cel tiparit pe facturi).
CREATE UNIQUE INDEX UX_CompanyBankAccount_Default ON core.CompanyBankAccount (CompanyId, Currency) WHERE IsDefault = 1;
GO

-- Catalogul tipurilor de documente numerotate; fiecare modul isi adauga tipurile (date livrate in core, ca permisiunile).
CREATE TABLE core.DocumentType (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentType PRIMARY KEY,
    Code             nvarchar(50)      NOT NULL,      -- tehnic, comparat exact: Modul.Tip (de exemplu Sales.Invoice)
    ModuleCode       nvarchar(30)      NOT NULL,
    Name             nvarchar(100)     NOT NULL,
    IsFiscalDefault  bit               NOT NULL CONSTRAINT DF_DocumentType_IsFiscalDefault DEFAULT (0),
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_DocumentType_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CONSTRAINT UQ_DocumentType_Code UNIQUE (Code),
    CONSTRAINT CK_DocumentType_Code CHECK (Code LIKE ModuleCode + N'.%')
);
GO

CREATE TABLE core.NumberSeries (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NumberSeries PRIMARY KEY,
    CompanyId      int               NOT NULL CONSTRAINT FK_NumberSeries_Company REFERENCES core.Company (Id),
    BranchId       int               NULL,
    DocumentTypeId int               NOT NULL CONSTRAINT FK_NumberSeries_DocumentType REFERENCES core.DocumentType (Id),
    Prefix         nvarchar(20)      NOT NULL,
    [Year]         smallint          NOT NULL,
    NextNumber     bigint            NOT NULL,
    Padding        tinyint           NOT NULL CONSTRAINT DF_NumberSeries_Padding DEFAULT (6),
    IsFiscal       bit               NOT NULL,
    IsDefault      bit               NOT NULL CONSTRAINT DF_NumberSeries_IsDefault DEFAULT (0),
    AutoRollover   bit               NOT NULL CONSTRAINT DF_NumberSeries_AutoRollover DEFAULT (0),
    FirstNumber    bigint            NOT NULL,      -- primul numar al seriei; folosite = NextNumber - FirstNumber
    LastUsedAt     datetime2(3)      NULL,
    ClosedAt       datetime2(3)      NULL,
    CreatedAt      datetime2(3)      NOT NULL CONSTRAINT DF_NumberSeries_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int               NOT NULL,
    ModifiedAt     datetime2(3)      NULL,
    ModifiedBy     int               NULL,
    RowVersion     rowversion        NOT NULL,
    PrefixKey      AS UPPER(Prefix) PERSISTED,      -- SQL-24
    CONSTRAINT FK_NumberSeries_Branch FOREIGN KEY (BranchId, CompanyId) REFERENCES core.Branch (Id, CompanyId),
    CONSTRAINT CK_NumberSeries_Year CHECK ([Year] BETWEEN 2000 AND 2999),
    CONSTRAINT CK_NumberSeries_Numbers CHECK (FirstNumber >= 1 AND NextNumber >= FirstNumber),
    CONSTRAINT CK_NumberSeries_Padding CHECK (Padding BETWEEN 1 AND 12),
    CONSTRAINT CK_NumberSeries_Prefix CHECK (Prefix NOT LIKE N'%[^A-Za-z0-9]%' AND LEN(Prefix) >= 1)
);
CREATE UNIQUE INDEX UX_NumberSeries_Key ON core.NumberSeries (CompanyId, BranchId, DocumentTypeId, PrefixKey, [Year]);
-- O singura serie implicita deschisa pe firma, punct de lucru, tip si an (cea folosita la validare, 4.2).
CREATE UNIQUE INDEX UX_NumberSeries_Default ON core.NumberSeries (CompanyId, BranchId, DocumentTypeId, [Year])
    WHERE IsDefault = 1 AND ClosedAt IS NULL;
GO
