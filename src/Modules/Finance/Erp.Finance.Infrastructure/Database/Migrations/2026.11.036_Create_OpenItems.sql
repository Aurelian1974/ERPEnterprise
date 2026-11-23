-- Decontarile cu partenerii (02.02, F3 3.1–3.3, 3.7, 7): partidele deschise, scadentele lor, profilul financiar al partenerului, alocarile
-- si TVA-ul devenit exigibil la incasare. Conventia de semn pe rol: debit (+), credit (−); restul de plata are semnul valorii (regula 3).

-- Partida deschisa (F3 3.1). Role: 1 Client, 2 Furnizor. Kind: 1 Factura, 2 Factura storno / credit, 3 Factura de avans, 4 Nota de debit /
-- penalitati, 5 Incasare / plata, 6 Sold initial, 7 Compensare, 8 Efect comercial, 9 Ajustare, 10 Plata prin angajat (decont).
-- EInvoiceStatus: 0 Nu se aplica, 1 Trimisa, 2 Acceptata, 3 Respinsa, 4 Primita din SPV, 5 Neprimita din SPV.
CREATE TABLE fin.OpenItem (
    Id                   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OpenItem PRIMARY KEY,
    CompanyId            int                NOT NULL CONSTRAINT FK_OpenItem_Company REFERENCES core.Company (Id),
    PartnerId            int                NOT NULL CONSTRAINT FK_OpenItem_Partner REFERENCES core.Partner (Id),
    Role                 tinyint            NOT NULL,
    Kind                 tinyint            NOT NULL,
    SourceModule         varchar(20)        NOT NULL,
    SourceDocumentType   varchar(50)        NOT NULL,
    SourceDocumentId     bigint             NOT NULL,
    SourceLineKey        nvarchar(50)       NOT NULL CONSTRAINT DF_OpenItem_SourceLineKey DEFAULT (N''),
    DocumentNo           nvarchar(50)       NOT NULL,
    DocumentDate         date               NOT NULL,
    AccountingDate       date               NOT NULL,
    DueDate              date               NOT NULL,
    CurrencyCode         char(3)            NOT NULL CONSTRAINT FK_OpenItem_Currency REFERENCES core.Currency (Code),
    ExchangeRate         decimal(19,6)      NOT NULL,
    Amount               decimal(19,2)      NOT NULL,
    AmountRon            decimal(19,2)      NOT NULL,
    OpenAmount           decimal(19,2)      NOT NULL,
    VatAmount            decimal(19,2)      NOT NULL CONSTRAINT DF_OpenItem_VatAmount DEFAULT (0),
    VatAmountRon         decimal(19,2)      NOT NULL CONSTRAINT DF_OpenItem_VatAmountRon DEFAULT (0),
    IsVatOnCollection    bit                NOT NULL CONSTRAINT DF_OpenItem_IsVatOnCollection DEFAULT (0),
    GlAccountCode        nvarchar(20)       NULL,
    EInvoiceStatus       tinyint            NOT NULL CONSTRAINT DF_OpenItem_EInvoiceStatus DEFAULT (0),
    EInvoiceIndexNo      varchar(30)        NULL,
    IsDisputed           bit                NOT NULL CONSTRAINT DF_OpenItem_IsDisputed DEFAULT (0),
    DisputeNote          nvarchar(500)      NULL,
    IsBlockedForPayment  bit                NOT NULL CONSTRAINT DF_OpenItem_IsBlockedForPayment DEFAULT (0),
    CostCenterId         int                NULL CONSTRAINT FK_OpenItem_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId            int                NULL CONSTRAINT FK_OpenItem_Project REFERENCES core.DimensionValue (Id),
    ClosedAt             date               NULL,
    ReversalOfId         bigint             NULL CONSTRAINT FK_OpenItem_OpenItem_ReversalOf REFERENCES fin.OpenItem (Id),
    CreatedAt            datetime2(3)       NOT NULL CONSTRAINT DF_OpenItem_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int                NOT NULL,
    ModifiedAt           datetime2(3)       NULL,
    ModifiedBy           int                NULL,
    RowVersion           rowversion         NOT NULL,
    CONSTRAINT CK_OpenItem_Role CHECK (Role IN (1, 2)),
    CONSTRAINT CK_OpenItem_Kind CHECK (Kind BETWEEN 1 AND 10),
    CONSTRAINT CK_OpenItem_EInvoiceStatus CHECK (EInvoiceStatus BETWEEN 0 AND 5),
    CONSTRAINT CK_OpenItem_AmountNotZero CHECK (Amount <> 0),
    -- Restul de plata are semnul valorii si nu o depaseste (regula 3 din sectiunea 2).
    CONSTRAINT CK_OpenItem_OpenAmountRange CHECK (
        (Amount > 0 AND OpenAmount BETWEEN 0 AND Amount) OR
        (Amount < 0 AND OpenAmount BETWEEN Amount AND 0)),
    CONSTRAINT CK_OpenItem_ClosedAt CHECK ((OpenAmount = 0 AND ClosedAt IS NOT NULL) OR (OpenAmount <> 0 AND ClosedAt IS NULL))
);
CREATE UNIQUE INDEX UX_OpenItem_Source
    ON fin.OpenItem (SourceDocumentType, SourceDocumentId, SourceLineKey, Role);
CREATE INDEX IX_OpenItem_CompanyId_PartnerId_Role_Open
    ON fin.OpenItem (CompanyId, PartnerId, Role, CurrencyCode)
    INCLUDE (DueDate, OpenAmount, DocumentNo, IsDisputed)
    WHERE OpenAmount <> 0;
CREATE INDEX IX_OpenItem_CompanyId_Role_DueDate_Open
    ON fin.OpenItem (CompanyId, Role, DueDate) INCLUDE (PartnerId, OpenAmount, CurrencyCode)
    WHERE OpenAmount <> 0;
CREATE INDEX IX_OpenItem_CompanyId_DocumentDate ON fin.OpenItem (CompanyId, DocumentDate);
CREATE INDEX IX_OpenItem_CompanyId_PartnerId_Role_DocumentDate ON fin.OpenItem (CompanyId, PartnerId, Role, DocumentDate);

-- Scadentele in rate (F3 3.1): alocarea stinge ratele in ordinea scadentei; OpenAmount se recalculeaza la fiecare alocare si desfacere.
CREATE TABLE fin.OpenItemSchedule (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OpenItemSchedule PRIMARY KEY,
    OpenItemId  bigint        NOT NULL CONSTRAINT FK_OpenItemSchedule_OpenItem REFERENCES fin.OpenItem (Id),
    DueDate     date          NOT NULL,
    Amount      decimal(19,2) NOT NULL,
    OpenAmount  decimal(19,2) NOT NULL,
    CONSTRAINT CK_OpenItemSchedule_AmountNotZero CHECK (Amount <> 0)
);
CREATE INDEX IX_OpenItemSchedule_OpenItemId ON fin.OpenItemSchedule (OpenItemId);

-- Profilul financiar al partenerului (F3 3.2), creat la prima partida cu valorile implicite. AutoAllocationMode: 1 Doar pe factura indicata,
-- 2 Factura indicata, apoi FIFO (implicit, decizia din 15.11.2026), 3 Fara alocare automata. PenaltyRatePerDay = 0: dobanda legala (3.10).
CREATE TABLE fin.PartnerFinanceProfile (
    Id                       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PartnerFinanceProfile PRIMARY KEY,
    CompanyId                int            NOT NULL CONSTRAINT FK_PartnerFinanceProfile_Company REFERENCES core.Company (Id),
    PartnerId                int            NOT NULL CONSTRAINT FK_PartnerFinanceProfile_Partner REFERENCES core.Partner (Id),
    Role                     tinyint        NOT NULL,
    AutoAllocationMode       tinyint        NOT NULL CONSTRAINT DF_PartnerFinanceProfile_AutoAllocationMode DEFAULT (2),
    PenaltyRatePerDay        decimal(9,6)   NOT NULL CONSTRAINT DF_PartnerFinanceProfile_PenaltyRatePerDay DEFAULT (0),
    PenaltyGraceDays         int            NOT NULL CONSTRAINT DF_PartnerFinanceProfile_PenaltyGraceDays DEFAULT (0),
    PenaltyCapAtPrincipal    bit            NOT NULL CONSTRAINT DF_PartnerFinanceProfile_PenaltyCapAtPrincipal DEFAULT (0),
    ApplyFixedRecoveryCost   bit            NOT NULL CONSTRAINT DF_PartnerFinanceProfile_ApplyFixedRecoveryCost DEFAULT (0),
    IsPaymentBlocked         bit            NOT NULL CONSTRAINT DF_PartnerFinanceProfile_IsPaymentBlocked DEFAULT (0),
    BlockReason              nvarchar(500)  NULL,
    BalanceConfirmationEmail nvarchar(200)  NULL,
    CreatedAt                datetime2(3)   NOT NULL CONSTRAINT DF_PartnerFinanceProfile_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy                int            NOT NULL,
    ModifiedAt               datetime2(3)   NULL,
    ModifiedBy               int            NULL,
    RowVersion               rowversion     NOT NULL,
    CONSTRAINT UQ_PartnerFinanceProfile_CompanyId_PartnerId_Role UNIQUE (CompanyId, PartnerId, Role),
    CONSTRAINT CK_PartnerFinanceProfile_Role CHECK (Role IN (1, 2)),
    CONSTRAINT CK_PartnerFinanceProfile_AutoAllocationMode CHECK (AutoAllocationMode IN (1, 2, 3)),
    CONSTRAINT CK_PartnerFinanceProfile_Penalty CHECK (PenaltyRatePerDay >= 0 AND PenaltyGraceDays >= 0)
);

-- Alocarea (F3 3.3): leaga o partida de debit cu una de credit. Source: 1 Automat la plata, 2 Manual, 3 Compensare, 4 Factura storno,
-- 5 Efect comercial, 6 Ajustare, 7 Import. Status: 1 Activa, 2 Desfacuta. Documentele F3 (compensare, ajustare, efect) primesc FK cand apar.
CREATE TABLE fin.Allocation (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Allocation PRIMARY KEY,
    CompanyId          int            NOT NULL CONSTRAINT FK_Allocation_Company REFERENCES core.Company (Id),
    DebitOpenItemId    bigint         NOT NULL CONSTRAINT FK_Allocation_OpenItem_Debit REFERENCES fin.OpenItem (Id),
    CreditOpenItemId   bigint         NOT NULL CONSTRAINT FK_Allocation_OpenItem_Credit REFERENCES fin.OpenItem (Id),
    DebitAmount        decimal(19,2)  NOT NULL,
    CreditAmount       decimal(19,2)  NOT NULL,
    AmountRonDebit     decimal(19,2)  NOT NULL,
    AmountRonCredit    decimal(19,2)  NOT NULL,
    AllocationDate     date           NOT NULL,
    Source             tinyint        NOT NULL,
    CompensationId     int            NULL,
    WriteOffId         int            NULL,
    CommercialPaperId  int            NULL,
    Status             tinyint        NOT NULL CONSTRAINT DF_Allocation_Status DEFAULT (1),
    UndoneAt           datetime2(3)   NULL,
    UndoneBy           int            NULL,
    UndoReason         nvarchar(500)  NULL,
    CreatedAt          datetime2(3)   NOT NULL CONSTRAINT DF_Allocation_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int            NOT NULL,
    RowVersion         rowversion     NOT NULL,
    CONSTRAINT CK_Allocation_Amounts CHECK (DebitAmount > 0 AND CreditAmount > 0),
    CONSTRAINT CK_Allocation_Source CHECK (Source BETWEEN 1 AND 7),
    CONSTRAINT CK_Allocation_Status CHECK ((Status = 1 AND UndoneAt IS NULL) OR (Status = 2 AND UndoneAt IS NOT NULL)),
    CONSTRAINT CK_Allocation_Sides CHECK (DebitOpenItemId <> CreditOpenItemId)
);
CREATE INDEX IX_Allocation_DebitOpenItemId ON fin.Allocation (DebitOpenItemId) INCLUDE (DebitAmount, AllocationDate, Status);
CREATE INDEX IX_Allocation_CreditOpenItemId ON fin.Allocation (CreditOpenItemId) INCLUDE (CreditAmount, AllocationDate, Status);
CREATE INDEX IX_Allocation_CompanyId_AllocationDate ON fin.Allocation (CompanyId, AllocationDate);

-- TVA-ul devenit exigibil la incasare (F3 3.7). Side: 1 Colectat, 2 Deductibil. Desfacerea alocarii scrie randul invers (sume negative).
CREATE TABLE fin.VatCashEntry (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_VatCashEntry PRIMARY KEY,
    CompanyId        int            NOT NULL CONSTRAINT FK_VatCashEntry_Company REFERENCES core.Company (Id),
    OpenItemId       bigint         NOT NULL CONSTRAINT FK_VatCashEntry_OpenItem REFERENCES fin.OpenItem (Id),
    AllocationId     bigint         NOT NULL CONSTRAINT FK_VatCashEntry_Allocation REFERENCES fin.Allocation (Id),
    Side             tinyint        NOT NULL,
    ExigibilityDate  date           NOT NULL,
    BaseAmountRon    decimal(19,2)  NOT NULL,
    VatAmountRon     decimal(19,2)  NOT NULL,
    IsReversal       bit            NOT NULL CONSTRAINT DF_VatCashEntry_IsReversal DEFAULT (0),
    CreatedAt        datetime2(3)   NOT NULL CONSTRAINT DF_VatCashEntry_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int            NOT NULL,
    CONSTRAINT CK_VatCashEntry_Side CHECK (Side IN (1, 2))
);
CREATE INDEX IX_VatCashEntry_CompanyId_ExigibilityDate_Side ON fin.VatCashEntry (CompanyId, ExigibilityDate, Side);
CREATE INDEX IX_VatCashEntry_OpenItemId ON fin.VatCashEntry (OpenItemId);

-- Lotul de alocari (F3 7, fin.usp_Allocation_CreateBatch); TVA-ul exigibil se calculeaza in procedura, sub blocarea partidelor.
CREATE TYPE fin.AllocationList AS TABLE (
    RowNo             int            NOT NULL PRIMARY KEY,
    DebitOpenItemId   bigint         NOT NULL,
    CreditOpenItemId  bigint         NOT NULL,
    Amount            decimal(19,2)  NOT NULL,
    AllocationDate    date           NULL,
    Source            tinyint        NOT NULL
);

-- Scadentele unei facturi primite prin eveniment (F3 4.1).
CREATE TYPE fin.OpenItemScheduleList AS TABLE (
    RowNo    int            NOT NULL PRIMARY KEY,
    DueDate  date           NOT NULL,
    Amount   decimal(19,2)  NOT NULL
);
