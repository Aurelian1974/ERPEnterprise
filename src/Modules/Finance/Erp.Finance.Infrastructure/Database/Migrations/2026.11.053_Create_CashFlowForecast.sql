-- 02.02 Finante, F5.2: prognoza de cash-flow (F5 3.4, 3.5). Intervalele de intarziere pe firma (cele livrate B0–B5 se creeaza la prima
-- folosire), istoricul de plata pe client si pe firma (PartnerId NULL), recalculat de job din alocari, nomenclatorul de categorii (seed
-- 08_CashFlowCategories) si sumele planificate cu recurenta.

CREATE TABLE fin.PaymentDelayBucket (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentDelayBucket PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_PaymentDelayBucket_Company REFERENCES core.Company (Id),
    Code             nvarchar(10)      NOT NULL,
    Name             nvarchar(100)     NOT NULL,
    DaysFrom         int               NULL,
    DaysTo           int               NULL,
    SortOrder        tinyint           NOT NULL,
    IsUncollectible  bit               NOT NULL CONSTRAINT DF_PaymentDelayBucket_IsUncollectible DEFAULT (0),
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_PaymentDelayBucket_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CodeKey          AS UPPER(Code) PERSISTED,
    CONSTRAINT UQ_PaymentDelayBucket_CompanyId_CodeKey UNIQUE (CompanyId, CodeKey),
    CONSTRAINT UQ_PaymentDelayBucket_CompanyId_SortOrder UNIQUE (CompanyId, SortOrder),
    CONSTRAINT CK_PaymentDelayBucket_Range CHECK (DaysFrom IS NULL OR DaysTo IS NULL OR DaysTo >= DaysFrom)
);

CREATE TABLE fin.PartnerPaymentBehavior (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerPaymentBehavior PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_PartnerPaymentBehavior_Company REFERENCES core.Company (Id),
    PartnerId        int               NULL CONSTRAINT FK_PartnerPaymentBehavior_Partner REFERENCES core.Partner (Id),
    SampleInvoices   int               NOT NULL,
    SampleAmountRon  decimal(19,2)     NOT NULL,
    AvgDaysLate      decimal(9,2)      NOT NULL,
    CalculatedAt     datetime2(3)      NOT NULL
);
CREATE UNIQUE INDEX UX_PartnerPaymentBehavior_CompanyId_PartnerId ON fin.PartnerPaymentBehavior (CompanyId, PartnerId) WHERE PartnerId IS NOT NULL;
CREATE UNIQUE INDEX UX_PartnerPaymentBehavior_CompanyId_Company ON fin.PartnerPaymentBehavior (CompanyId) WHERE PartnerId IS NULL;

CREATE TABLE fin.PartnerPaymentBehaviorBucket (
    Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerPaymentBehaviorBucket PRIMARY KEY,
    BehaviorId   int               NOT NULL CONSTRAINT FK_PartnerPaymentBehaviorBucket_Behavior REFERENCES fin.PartnerPaymentBehavior (Id) ON DELETE CASCADE,
    BucketId     int               NOT NULL CONSTRAINT FK_PartnerPaymentBehaviorBucket_Bucket REFERENCES fin.PaymentDelayBucket (Id),
    AmountShare  decimal(9,6)      NOT NULL,
    AvgDaysLate  decimal(9,2)      NOT NULL,
    CONSTRAINT UQ_PartnerPaymentBehaviorBucket_Behavior_Bucket UNIQUE (BehaviorId, BucketId),
    CONSTRAINT CK_PartnerPaymentBehaviorBucket_Share CHECK (AmountShare BETWEEN 0 AND 1)
);
CREATE INDEX IX_PartnerPaymentBehaviorBucket_BucketId ON fin.PartnerPaymentBehaviorBucket (BucketId);

CREATE TABLE fin.CashFlowCategory (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashFlowCategory PRIMARY KEY,
    Code        nvarchar(30)      NOT NULL,
    Name        nvarchar(100)     NOT NULL,
    Direction   tinyint           NOT NULL,
    SortOrder   smallint          NOT NULL,
    ParentId    int               NULL CONSTRAINT FK_CashFlowCategory_Parent REFERENCES fin.CashFlowCategory (Id),
    IsSystem    bit               NOT NULL CONSTRAINT DF_CashFlowCategory_IsSystem DEFAULT (0),
    IsActive    bit               NOT NULL CONSTRAINT DF_CashFlowCategory_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_CashFlowCategory_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CodeKey     AS UPPER(Code) PERSISTED,
    -- Directia: 1 intrari, 2 iesiri, 3 ambele (transferurile intre conturi).
    CONSTRAINT CK_CashFlowCategory_Direction CHECK (Direction BETWEEN 1 AND 3)
);
CREATE UNIQUE INDEX UX_CashFlowCategory_CodeKey ON fin.CashFlowCategory (CodeKey);
CREATE INDEX IX_CashFlowCategory_ParentId ON fin.CashFlowCategory (ParentId) WHERE ParentId IS NOT NULL;

CREATE TABLE fin.CashFlowPlanItem (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashFlowPlanItem PRIMARY KEY,
    CompanyId          int               NOT NULL CONSTRAINT FK_CashFlowPlanItem_Company REFERENCES core.Company (Id),
    CategoryId         int               NOT NULL CONSTRAINT FK_CashFlowPlanItem_Category REFERENCES fin.CashFlowCategory (Id),
    Description        nvarchar(200)     NOT NULL,
    CurrencyCode       char(3)           NOT NULL CONSTRAINT FK_CashFlowPlanItem_Currency REFERENCES core.Currency (Code),
    Amount             decimal(19,2)     NOT NULL,
    TreasuryAccountId  int               NULL CONSTRAINT FK_CashFlowPlanItem_TreasuryAccount REFERENCES fin.TreasuryAccount (Id),
    FirstDate          date              NOT NULL,
    Recurrence         tinyint           NOT NULL CONSTRAINT DF_CashFlowPlanItem_Recurrence DEFAULT (0),
    RecurrenceDay      tinyint           NULL,
    EndDate            date              NULL,
    IsActive           bit               NOT NULL CONSTRAINT DF_CashFlowPlanItem_IsActive DEFAULT (1),
    Probability        tinyint           NOT NULL CONSTRAINT DF_CashFlowPlanItem_Probability DEFAULT (100),
    CreatedAt          datetime2(3)      NOT NULL CONSTRAINT DF_CashFlowPlanItem_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int               NOT NULL,
    ModifiedAt         datetime2(3)      NULL,
    ModifiedBy         int               NULL,
    RowVersion         rowversion        NOT NULL,
    -- Suma e pozitiva; directia vine din categorie (la transferuri, semnul sumei).
    CONSTRAINT CK_CashFlowPlanItem_Amount CHECK (Amount <> 0),
    CONSTRAINT CK_CashFlowPlanItem_Recurrence CHECK (Recurrence BETWEEN 0 AND 4),
    CONSTRAINT CK_CashFlowPlanItem_RecurrenceDay CHECK (RecurrenceDay IS NULL OR RecurrenceDay BETWEEN 1 AND 31),
    CONSTRAINT CK_CashFlowPlanItem_EndDate CHECK (EndDate IS NULL OR EndDate >= FirstDate),
    CONSTRAINT CK_CashFlowPlanItem_Probability CHECK (Probability BETWEEN 0 AND 100)
);
CREATE INDEX IX_CashFlowPlanItem_CompanyId_IsActive ON fin.CashFlowPlanItem (CompanyId, IsActive);
CREATE INDEX IX_CashFlowPlanItem_CategoryId ON fin.CashFlowPlanItem (CategoryId);
CREATE INDEX IX_CashFlowPlanItem_TreasuryAccountId ON fin.CashFlowPlanItem (TreasuryAccountId) WHERE TreasuryAccountId IS NOT NULL;
