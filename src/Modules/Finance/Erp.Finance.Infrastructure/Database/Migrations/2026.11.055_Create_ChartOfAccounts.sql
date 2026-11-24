-- 02.02 Contabilitate, C1.1: planurile de conturi (C1 3.1, 3.2). Un singur plan statutar (RO, livrat de seed-ul 09_ChartOfAccounts);
-- conturile tuturor planurilor intr-un tabel, cu parinte, nivel, tip, frunza (IsPostable, intretinuta de proceduri), restrictie pe firma,
-- atributele de contare si contul SAF-T; istoricul denumirilor; regulile de dimensiuni pe cont (mostenite de la parinte) si rolurile
-- conturilor, pe toata instalarea (CompanyId NULL) sau pe firma, cu conditii pe tipul partenerului si pe moneda.

CREATE TABLE fin.ChartOfAccounts (
    Id                      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChartOfAccounts PRIMARY KEY,
    Code                    nvarchar(20)      NOT NULL,
    Name                    nvarchar(200)     NOT NULL,
    Kind                    tinyint           NOT NULL,
    Purpose                 tinyint           NULL,
    LanguageCode            char(2)           NOT NULL CONSTRAINT DF_ChartOfAccounts_LanguageCode DEFAULT ('ro'),
    RequireCompleteMapping  bit               NOT NULL CONSTRAINT DF_ChartOfAccounts_RequireCompleteMapping DEFAULT (0),
    IsActive                bit               NOT NULL CONSTRAINT DF_ChartOfAccounts_IsActive DEFAULT (1),
    CreatedAt               datetime2(3)      NOT NULL CONSTRAINT DF_ChartOfAccounts_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy               int               NOT NULL,
    ModifiedAt              datetime2(3)      NULL,
    ModifiedBy              int               NULL,
    RowVersion              rowversion        NOT NULL,
    CodeKey                 AS UPPER(Code) PERSISTED,
    CONSTRAINT CK_ChartOfAccounts_Kind CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_ChartOfAccounts_Purpose CHECK ((Kind = 1 AND Purpose IS NULL) OR (Kind = 2 AND Purpose BETWEEN 1 AND 5))
);
CREATE UNIQUE INDEX UX_ChartOfAccounts_CodeKey ON fin.ChartOfAccounts (CodeKey);
CREATE UNIQUE INDEX UX_ChartOfAccounts_Statutory ON fin.ChartOfAccounts (Kind) WHERE Kind = 1;

CREATE TABLE fin.ChartOfAccountsCompany (
    ChartId    int  NOT NULL CONSTRAINT FK_ChartOfAccountsCompany_Chart REFERENCES fin.ChartOfAccounts (Id),
    CompanyId  int  NOT NULL CONSTRAINT FK_ChartOfAccountsCompany_Company REFERENCES core.Company (Id),
    CONSTRAINT PK_ChartOfAccountsCompany PRIMARY KEY (ChartId, CompanyId)
);
CREATE INDEX IX_ChartOfAccountsCompany_CompanyId ON fin.ChartOfAccountsCompany (CompanyId);

CREATE TABLE fin.GlAccount (
    Id                  int IDENTITY(1,1)  NOT NULL CONSTRAINT PK_GlAccount PRIMARY KEY,
    ChartId             int                NOT NULL CONSTRAINT FK_GlAccount_ChartOfAccounts REFERENCES fin.ChartOfAccounts (Id),
    Code                nvarchar(20)       NOT NULL,
    Name                nvarchar(200)      NOT NULL,
    ParentId            int                NULL CONSTRAINT FK_GlAccount_Parent REFERENCES fin.GlAccount (Id),
    Level               tinyint            NOT NULL,
    AccountType         tinyint            NULL,
    IsPostable          bit                NOT NULL CONSTRAINT DF_GlAccount_IsPostable DEFAULT (1),
    CompanyId           int                NULL CONSTRAINT FK_GlAccount_Company REFERENCES core.Company (Id),
    IsOffBalance        bit                NOT NULL CONSTRAINT DF_GlAccount_IsOffBalance DEFAULT (0),
    IsManagement        bit                NOT NULL CONSTRAINT DF_GlAccount_IsManagement DEFAULT (0),
    StatutoryReporting  bit                NOT NULL CONSTRAINT DF_GlAccount_StatutoryReporting DEFAULT (1),
    CurrencyMode        tinyint            NOT NULL CONSTRAINT DF_GlAccount_CurrencyMode DEFAULT (0),
    CurrencyCode        char(3)            NULL CONSTRAINT FK_GlAccount_Currency REFERENCES core.Currency (Code),
    PartyRequirement    tinyint            NOT NULL CONSTRAINT DF_GlAccount_PartyRequirement DEFAULT (0),
    ManualPosting       bit                NOT NULL CONSTRAINT DF_GlAccount_ManualPosting DEFAULT (1),
    SaftAccountCode     nvarchar(20)       NULL,
    TaxTreatment        tinyint            NOT NULL CONSTRAINT DF_GlAccount_TaxTreatment DEFAULT (1),
    ClosingTarget       tinyint            NOT NULL CONSTRAINT DF_GlAccount_ClosingTarget DEFAULT (0),
    ValidFrom           date               NULL,
    ValidTo             date               NULL,
    IsSystem            bit                NOT NULL CONSTRAINT DF_GlAccount_IsSystem DEFAULT (0),
    IsActive            bit                NOT NULL CONSTRAINT DF_GlAccount_IsActive DEFAULT (1),
    CreatedAt           datetime2(3)       NOT NULL CONSTRAINT DF_GlAccount_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int                NOT NULL,
    ModifiedAt          datetime2(3)       NULL,
    ModifiedBy          int                NULL,
    RowVersion          rowversion         NOT NULL,
    CodeKey             AS UPPER(Code) PERSISTED,
    CONSTRAINT UQ_GlAccount_ChartId_CodeKey UNIQUE (ChartId, CodeKey),
    CONSTRAINT CK_GlAccount_Level CHECK (Level BETWEEN 1 AND 9),
    CONSTRAINT CK_GlAccount_AccountType CHECK (AccountType IS NULL OR AccountType BETWEEN 1 AND 3),
    CONSTRAINT CK_GlAccount_CurrencyMode CHECK ((CurrencyMode IN (0, 1) AND CurrencyCode IS NULL) OR (CurrencyMode = 2 AND CurrencyCode IS NOT NULL)),
    CONSTRAINT CK_GlAccount_PartyRequirement CHECK (PartyRequirement BETWEEN 0 AND 3),
    CONSTRAINT CK_GlAccount_TaxTreatment CHECK (TaxTreatment BETWEEN 1 AND 4),
    CONSTRAINT CK_GlAccount_ClosingTarget CHECK (ClosingTarget BETWEEN 0 AND 2),
    CONSTRAINT CK_GlAccount_Validity CHECK (ValidFrom IS NULL OR ValidTo IS NULL OR ValidTo >= ValidFrom)
);
CREATE INDEX IX_GlAccount_ChartId_ParentId ON fin.GlAccount (ChartId, ParentId);
CREATE INDEX IX_GlAccount_ChartId_IsPostable ON fin.GlAccount (ChartId, IsPostable) INCLUDE (Code, Name);
CREATE INDEX IX_GlAccount_ParentId ON fin.GlAccount (ParentId) WHERE ParentId IS NOT NULL;
CREATE INDEX IX_GlAccount_CompanyId ON fin.GlAccount (CompanyId) WHERE CompanyId IS NOT NULL;

CREATE TABLE fin.GlAccountNameHistory (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GlAccountNameHistory PRIMARY KEY,
    AccountId   int               NOT NULL CONSTRAINT FK_GlAccountNameHistory_Account REFERENCES fin.GlAccount (Id),
    Name        nvarchar(200)     NOT NULL,
    ValidFrom   datetime2(3)      NOT NULL,
    ValidTo     datetime2(3)      NOT NULL,
    ChangedBy   int               NOT NULL
);
CREATE INDEX IX_GlAccountNameHistory_AccountId_ValidFrom ON fin.GlAccountNameHistory (AccountId, ValidFrom);

CREATE TABLE fin.GlAccountDimensionRule (
    AccountId      int           NOT NULL CONSTRAINT FK_GlAccountDimensionRule_Account REFERENCES fin.GlAccount (Id),
    DimensionType  varchar(20)   NOT NULL CONSTRAINT FK_GlAccountDimensionRule_Type REFERENCES core.DimensionType (Code),
    Mode           tinyint       NOT NULL,
    CreatedAt      datetime2(3)  NOT NULL CONSTRAINT DF_GlAccountDimensionRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int           NOT NULL,
    CONSTRAINT PK_GlAccountDimensionRule PRIMARY KEY (AccountId, DimensionType),
    CONSTRAINT CK_GlAccountDimensionRule_Mode CHECK (Mode BETWEEN 0 AND 2)
);
CREATE INDEX IX_GlAccountDimensionRule_DimensionType ON fin.GlAccountDimensionRule (DimensionType);

CREATE TABLE fin.GlAccountRoleAssignment (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_GlAccountRoleAssignment PRIMARY KEY,
    CompanyId     int               NULL CONSTRAINT FK_GlAccountRoleAssignment_Company REFERENCES core.Company (Id),
    Role          varchar(40)       NOT NULL,
    AccountId     int               NOT NULL CONSTRAINT FK_GlAccountRoleAssignment_Account REFERENCES fin.GlAccount (Id),
    PartnerKind   tinyint           NULL,
    CurrencyCode  char(3)           NULL CONSTRAINT FK_GlAccountRoleAssignment_Currency REFERENCES core.Currency (Code),
    ValidFrom     date              NOT NULL,
    ValidTo       date              NULL,
    CreatedAt     datetime2(3)      NOT NULL CONSTRAINT DF_GlAccountRoleAssignment_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy     int               NOT NULL,
    ModifiedAt    datetime2(3)      NULL,
    ModifiedBy    int               NULL,
    RowVersion    rowversion        NOT NULL,
    -- Tipul partenerului: 1 persoana juridica romana, 2 UE, 3 non-UE, 4 persoana fizica; NULL = oricare.
    CONSTRAINT CK_GlAccountRoleAssignment_PartnerKind CHECK (PartnerKind IS NULL OR PartnerKind BETWEEN 1 AND 4),
    CONSTRAINT CK_GlAccountRoleAssignment_Validity CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
CREATE UNIQUE INDEX UX_GlAccountRoleAssignment_Key ON fin.GlAccountRoleAssignment (CompanyId, Role, PartnerKind, CurrencyCode, ValidFrom);
CREATE INDEX IX_GlAccountRoleAssignment_AccountId ON fin.GlAccountRoleAssignment (AccountId);
CREATE INDEX IX_GlAccountRoleAssignment_CurrencyCode ON fin.GlAccountRoleAssignment (CurrencyCode) WHERE CurrencyCode IS NOT NULL;
