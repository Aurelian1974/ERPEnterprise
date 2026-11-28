-- Stocuri S4.4 (02.05, S4 3.6, 3.7, 7): documentul de schimbare a lotului (SL), retragerea (RL) cu instantaneul clientilor si genealogia
-- loturilor (scrisa de Productie), pentru trasabilitate.

CREATE TABLE inv.LotChange (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LotChange PRIMARY KEY,
    CompanyId    int            NOT NULL CONSTRAINT FK_LotChange_Company REFERENCES core.Company (Id),
    Number       nvarchar(40)   NULL,
    WarehouseId  int            NOT NULL CONSTRAINT FK_LotChange_Warehouse REFERENCES inv.Warehouse (Id),
    DepotId      int            NULL CONSTRAINT FK_LotChange_Depot REFERENCES inv.Depot (Id),
    DocumentDate date           NOT NULL,
    Status       tinyint        NOT NULL CONSTRAINT DF_LotChange_Status DEFAULT (1),   -- 1 Ciorna, 2 Validat, 4 Anulat
    Notes        nvarchar(1000) NULL,
    ValidatedAt  datetime2(3)   NULL,
    ValidatedBy  int            NULL CONSTRAINT FK_LotChange_User_ValidatedBy REFERENCES core.[User] (Id),
    CreatedAt    datetime2(3)   NOT NULL CONSTRAINT DF_LotChange_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int            NOT NULL,
    ModifiedAt   datetime2(3)   NULL,
    ModifiedBy   int            NULL,
    RowVersion   rowversion     NOT NULL,
    CONSTRAINT CK_LotChange_Status CHECK (Status IN (1, 2, 4)),
    CONSTRAINT CK_LotChange_Number CHECK (Status <> 2 OR Number IS NOT NULL)
);
CREATE UNIQUE INDEX UX_LotChange_Number ON inv.LotChange (CompanyId, Number) WHERE Number IS NOT NULL;
CREATE INDEX IX_LotChange_Warehouse_Date ON inv.LotChange (WarehouseId, DocumentDate);
GO

CREATE TABLE inv.LotChangeLine (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LotChangeLine PRIMARY KEY,
    LotChangeId   int            NOT NULL CONSTRAINT FK_LotChangeLine_LotChange REFERENCES inv.LotChange (Id) ON DELETE CASCADE,
    LineNumber    int            NOT NULL,
    ItemVariantId int            NOT NULL CONSTRAINT FK_LotChangeLine_ItemVariant REFERENCES core.ItemVariant (Id),
    FromLotId     int            NOT NULL CONSTRAINT FK_LotChangeLine_FromLot REFERENCES inv.Lot (Id),
    ToLotId       int            NULL CONSTRAINT FK_LotChangeLine_ToLot REFERENCES inv.Lot (Id),
    NewLotCode    nvarchar(40)   NULL,
    NewExpiryDate date           NULL,
    QtyBase       decimal(19,6)  NOT NULL,
    CONSTRAINT CK_LotChangeLine_Qty CHECK (QtyBase > 0),
    CONSTRAINT CK_LotChangeLine_Target CHECK (ToLotId IS NOT NULL OR NewLotCode IS NOT NULL)
);
GO

CREATE TYPE inv.tt_LotChangeLine AS TABLE (
    LineNumber    int            NOT NULL PRIMARY KEY,
    ItemVariantId int            NOT NULL,
    FromLotId     int            NOT NULL,
    ToLotId       int            NULL,
    NewLotCode    nvarchar(40)   NULL,
    NewExpiryDate date           NULL,
    QtyBase       decimal(19,6)  NOT NULL
);
GO

CREATE TABLE inv.Recall (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Recall PRIMARY KEY,
    CompanyId         int            NOT NULL CONSTRAINT FK_Recall_Company REFERENCES core.Company (Id),
    Number            nvarchar(40)   NULL,
    DocumentDate      date           NOT NULL,
    SourceKind        tinyint        NOT NULL,   -- 1 furnizorul, 2 autoritatea, 3 reclamatie proprie
    Reason            nvarchar(1000) NOT NULL,
    SupplierPartnerId int            NULL CONSTRAINT FK_Recall_Supplier REFERENCES core.Partner (Id),
    SupplierLotCode   nvarchar(40)   NULL,
    Status            tinyint        NOT NULL CONSTRAINT DF_Recall_Status DEFAULT (1),   -- 1 Ciorna, 2 Validat (deschis), 3 Inchis, 4 Anulat
    ValidatedAt       datetime2(3)   NULL,
    ValidatedBy       int            NULL CONSTRAINT FK_Recall_User_ValidatedBy REFERENCES core.[User] (Id),
    ClosedAt          datetime2(3)   NULL,
    ClosedBy          int            NULL CONSTRAINT FK_Recall_User_ClosedBy REFERENCES core.[User] (Id),
    CreatedAt         datetime2(3)   NOT NULL CONSTRAINT DF_Recall_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int            NOT NULL,
    ModifiedAt        datetime2(3)   NULL,
    ModifiedBy        int            NULL,
    RowVersion        rowversion     NOT NULL,
    CONSTRAINT CK_Recall_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_Recall_SourceKind CHECK (SourceKind BETWEEN 1 AND 3),
    CONSTRAINT CK_Recall_Number CHECK (Status IN (1, 4) OR Number IS NOT NULL)
);
CREATE UNIQUE INDEX UX_Recall_Number ON inv.Recall (CompanyId, Number) WHERE Number IS NOT NULL;
GO

CREATE TABLE inv.RecallLot (
    Id       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecallLot PRIMARY KEY,
    RecallId int NOT NULL CONSTRAINT FK_RecallLot_Recall REFERENCES inv.Recall (Id) ON DELETE CASCADE,
    LotId    int NOT NULL CONSTRAINT FK_RecallLot_Lot REFERENCES inv.Lot (Id),
    CONSTRAINT UX_RecallLot UNIQUE (RecallId, LotId)
);
CREATE INDEX IX_RecallLot_LotId ON inv.RecallLot (LotId);
GO

-- Instantaneul clientilor la validare (doar de adaugare, abatere SQL-11); rezultatul se noteaza la fiecare client.
CREATE TABLE inv.RecallCustomer (
    Id                   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecallCustomer PRIMARY KEY,
    RecallId             int            NOT NULL CONSTRAINT FK_RecallCustomer_Recall REFERENCES inv.Recall (Id),
    LotId                int            NOT NULL CONSTRAINT FK_RecallCustomer_Lot REFERENCES inv.Lot (Id),
    PartnerId            int            NULL CONSTRAINT FK_RecallCustomer_Partner REFERENCES core.Partner (Id),
    SourceDocumentType   varchar(30)    NOT NULL,
    SourceDocumentId     bigint         NOT NULL,
    SourceDocumentNumber nvarchar(40)   NULL,
    MovementDate         date           NOT NULL,
    QtyBase              decimal(19,6)  NOT NULL,
    Outcome              tinyint        NULL,     -- 1 returnat, 2 distrus la client, 3 nu mai are
    OutcomeNote          nvarchar(400)  NULL,
    OutcomeAt            datetime2(3)   NULL,
    OutcomeBy            int            NULL,
    CONSTRAINT CK_RecallCustomer_Outcome CHECK (Outcome IS NULL OR Outcome BETWEEN 1 AND 3)
);
CREATE INDEX IX_RecallCustomer_Recall ON inv.RecallCustomer (RecallId);
GO

-- Genealogia: loturile consumate -> lotul rezultat (productie, reambalare); doar de adaugare (abatere SQL-11).
CREATE TABLE inv.LotGenealogy (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LotGenealogy PRIMARY KEY,
    ParentLotId      int            NOT NULL CONSTRAINT FK_LotGenealogy_Parent REFERENCES inv.Lot (Id),
    ChildLotId       int            NOT NULL CONSTRAINT FK_LotGenealogy_Child REFERENCES inv.Lot (Id),
    SourceMovementId bigint         NOT NULL CONSTRAINT FK_LotGenealogy_Movement REFERENCES inv.StockMovement (Id),
    QtyBase          decimal(19,6)  NOT NULL,
    CreatedAt        datetime2(3)   NOT NULL CONSTRAINT DF_LotGenealogy_CreatedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT UX_LotGenealogy UNIQUE (ParentLotId, ChildLotId, SourceMovementId)
);
CREATE INDEX IX_LotGenealogy_Child ON inv.LotGenealogy (ChildLotId);
GO
