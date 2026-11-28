-- Stocuri S3 (02.05, S3 2, 3.1–3.5, 3.8; 7): straturile de cost (FIFO, LIFO, identificare directa), consumul lor, ajustarile de valoare fara
-- cantitate, coada de recalculare si costul standard al modelului. Costul il calculeaza motorul din Erp.Inventory.Domain (CostEngine); SQL
-- blocheaza cheile, citeste starea si persista rezultatul. Abatere declarata de la SQL-11 pentru CostLayer, CostLayerConsumption, CostAdjustment:
-- fara RowVersion, nu se editeaza din interfata.

ALTER TABLE inv.ItemStockSettings ADD StandardCost decimal(19,6) NULL CONSTRAINT CK_ItemStockSettings_StandardCost CHECK (StandardCost IS NULL OR StandardCost >= 0);
GO

CREATE TABLE inv.CostLayer (
    Id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostLayer PRIMARY KEY,
    CompanyId      int            NOT NULL CONSTRAINT FK_CostLayer_Company REFERENCES core.Company (Id),
    WarehouseId    int            NOT NULL CONSTRAINT FK_CostLayer_Warehouse REFERENCES inv.Warehouse (Id),
    DepotId        int            NULL CONSTRAINT FK_CostLayer_Depot REFERENCES inv.Depot (Id),
    DepotKey       AS ISNULL(DepotId, 0) PERSISTED,
    ItemVariantId  int            NOT NULL CONSTRAINT FK_CostLayer_ItemVariant REFERENCES core.ItemVariant (Id),
    LotId          int            NULL,
    LotKey         AS ISNULL(LotId, 0) PERSISTED,
    MovementId     bigint         NOT NULL CONSTRAINT FK_CostLayer_StockMovement REFERENCES inv.StockMovement (Id),   -- intrarea; la stratul negativ, iesirea
    OriginLayerId  bigint         NULL,      -- stratul din care vine, la transfer (fara FK: straturile se refac la recalculare)
    LayerDate      date           NOT NULL,
    PostingSeq     bigint         NOT NULL,
    QtyIn          decimal(19,6)  NOT NULL,
    QtyRemaining   decimal(19,6)  NOT NULL,
    ValueRemaining decimal(19,2)  NOT NULL,
    UnitCost       decimal(19,6)  NOT NULL,
    IsNegative     bit            NOT NULL CONSTRAINT DF_CostLayer_IsNegative DEFAULT (0),
    CreatedAt      datetime2(3)   NOT NULL CONSTRAINT DF_CostLayer_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int            NOT NULL,
    ModifiedAt     datetime2(3)   NULL,
    CONSTRAINT CK_CostLayer_Qty CHECK ((IsNegative = 0 AND QtyIn > 0 AND QtyRemaining BETWEEN 0 AND QtyIn) OR (IsNegative = 1 AND QtyRemaining <= 0)),
    CONSTRAINT CK_CostLayer_UnitCost CHECK (UnitCost >= 0)
);
CREATE INDEX IX_CostLayer_Open ON inv.CostLayer (WarehouseId, DepotKey, ItemVariantId, LotKey, LayerDate, PostingSeq)
    INCLUDE (QtyRemaining, ValueRemaining, UnitCost) WHERE QtyRemaining <> 0;
CREATE INDEX IX_CostLayer_MovementId ON inv.CostLayer (MovementId);
GO

CREATE TABLE inv.CostLayerConsumption (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostLayerConsumption PRIMARY KEY,
    OutboundMovementId bigint         NOT NULL CONSTRAINT FK_CostLayerConsumption_StockMovement REFERENCES inv.StockMovement (Id),
    CostLayerId        bigint         NOT NULL CONSTRAINT FK_CostLayerConsumption_CostLayer REFERENCES inv.CostLayer (Id),
    Qty                decimal(19,6)  NOT NULL,
    CostAmount         decimal(19,2)  NOT NULL,
    CONSTRAINT CK_CostLayerConsumption_Qty CHECK (Qty > 0)
);
CREATE INDEX IX_CostLayerConsumption_Outbound ON inv.CostLayerConsumption (OutboundMovementId);
CREATE INDEX IX_CostLayerConsumption_Layer ON inv.CostLayerConsumption (CostLayerId);
GO

CREATE TABLE inv.CostAdjustment (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostAdjustment PRIMARY KEY,
    CompanyId          int            NOT NULL CONSTRAINT FK_CostAdjustment_Company REFERENCES core.Company (Id),
    WarehouseId        int            NOT NULL CONSTRAINT FK_CostAdjustment_Warehouse REFERENCES inv.Warehouse (Id),
    DepotId            int            NULL,
    ItemVariantId      int            NOT NULL CONSTRAINT FK_CostAdjustment_ItemVariant REFERENCES core.ItemVariant (Id),
    LotId              int            NULL,
    MovementId         bigint         NULL CONSTRAINT FK_CostAdjustment_StockMovement REFERENCES inv.StockMovement (Id),
    Kind               tinyint        NOT NULL,   -- 1 Recalculare, 2 Regularizare negativ, 3 Cost adaugat, 4 Diferenta de pret, 5 Modificare pret amanunt
    InStock            bit            NOT NULL,   -- 1 = schimba valoarea stocului (nu e in miscari); 0 = urma unei schimbari deja scrise pe miscare
    Amount             decimal(19,2)  NOT NULL,
    AccountingDate     date           NOT NULL,
    SourceDocumentType varchar(30)    NULL,
    SourceDocumentId   bigint         NULL,
    CreatedAt          datetime2(3)   NOT NULL CONSTRAINT DF_CostAdjustment_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int            NOT NULL,
    CONSTRAINT CK_CostAdjustment_Amount CHECK (Amount <> 0),
    CONSTRAINT CK_CostAdjustment_Kind CHECK (Kind BETWEEN 1 AND 5)
);
CREATE INDEX IX_CostAdjustment_Company_Date ON inv.CostAdjustment (CompanyId, AccountingDate);
CREATE INDEX IX_CostAdjustment_MovementId ON inv.CostAdjustment (MovementId);
CREATE INDEX IX_CostAdjustment_Key ON inv.CostAdjustment (WarehouseId, ItemVariantId);
GO

CREATE TABLE inv.CostRecalcQueue (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostRecalcQueue PRIMARY KEY,
    CompanyId     int            NOT NULL CONSTRAINT FK_CostRecalcQueue_Company REFERENCES core.Company (Id),
    WarehouseId   int            NOT NULL CONSTRAINT FK_CostRecalcQueue_Warehouse REFERENCES inv.Warehouse (Id),
    DepotId       int            NULL,
    DepotKey      AS ISNULL(DepotId, 0) PERSISTED,
    ItemVariantId int            NOT NULL,
    LotId         int            NULL,
    LotKey        AS ISNULL(LotId, 0) PERSISTED,
    FromDate      date           NOT NULL,
    EnqueuedAt    datetime2(3)   NOT NULL CONSTRAINT DF_CostRecalcQueue_EnqueuedAt DEFAULT (sysutcdatetime())
);
CREATE UNIQUE INDEX UX_CostRecalcQueue_Key ON inv.CostRecalcQueue (WarehouseId, DepotKey, ItemVariantId, LotKey);
CREATE INDEX IX_CostRecalcQueue_FromDate ON inv.CostRecalcQueue (CompanyId, FromDate);
GO

CREATE TYPE inv.BigIdList AS TABLE (Id bigint NOT NULL PRIMARY KEY);
GO

CREATE TYPE inv.tt_CostKey AS TABLE (
    WarehouseId   int NOT NULL,
    DepotId       int NULL,
    ItemVariantId int NOT NULL,
    LotId         int NULL
);
GO

-- Miscarile costate de motor (inlocuieste inv.tt_StockMovementRequest, pastrat pentru compatibilitate).
CREATE TYPE inv.tt_StockMovementCosted AS TABLE (
    RowNo           int            NOT NULL PRIMARY KEY,
    SourceLineId    bigint         NOT NULL,
    SourceRole      tinyint        NOT NULL,
    MovementTypeCode varchar(30)   NOT NULL,
    DepotId         int            NULL,
    WarehouseId     int            NOT NULL,
    ItemVariantId   int            NOT NULL,
    QtyBase         decimal(19,6)  NOT NULL,
    UnitCost        decimal(19,6)  NOT NULL,
    CostAmount      decimal(19,2)  NOT NULL,
    LotId           int            NULL,
    SerialId        int            NULL,
    CostCenterId    int            NULL,
    ProjectId       int            NULL,
    ReasonId        int            NULL,
    CounterRowNo    int            NULL,
    ReversalOfId    bigint         NULL
);
GO

CREATE TYPE inv.tt_CostKeyState AS TABLE (
    WarehouseId   int            NOT NULL,
    DepotId       int            NULL,
    ItemVariantId int            NOT NULL,
    LotId         int            NULL,
    Qty           decimal(19,6)  NOT NULL,
    Value         decimal(19,2)  NOT NULL,
    LastUnitCost  decimal(19,6)  NULL
);
GO

CREATE TYPE inv.tt_CostLayerWrite AS TABLE (
    LayerRef       int            NULL,      -- stratul nou din calcul
    LayerId        bigint         NULL,      -- stratul existent actualizat
    RowNo          int            NULL,      -- miscarea din apel care l-a creat
    MovementId     bigint         NULL,      -- miscarea existenta care l-a creat (recalculare)
    WarehouseId    int            NOT NULL,
    DepotId        int            NULL,
    ItemVariantId  int            NOT NULL,
    LotId          int            NULL,
    LayerDate      date           NOT NULL,
    SeqOverride    bigint         NULL,      -- ordinea stratului mutat prin transfer (altfel PostingSeq-ul miscarii)
    QtyIn          decimal(19,6)  NOT NULL,
    QtyRemaining   decimal(19,6)  NOT NULL,
    ValueRemaining decimal(19,2)  NOT NULL,
    UnitCost       decimal(19,6)  NOT NULL,
    IsNegative     bit            NOT NULL,
    OriginLayerId  bigint         NULL
);
GO

CREATE TYPE inv.tt_CostConsumptionWrite AS TABLE (
    RowNo      int            NULL,
    MovementId bigint         NULL,
    LayerRef   int            NULL,
    LayerId    bigint         NULL,
    Qty        decimal(19,6)  NOT NULL,
    CostAmount decimal(19,2)  NOT NULL
);
GO

CREATE TYPE inv.tt_CostAdjustmentWrite AS TABLE (
    RowNo         int            NULL,
    MovementId    bigint         NULL,
    WarehouseId   int            NOT NULL,
    DepotId       int            NULL,
    ItemVariantId int            NOT NULL,
    LotId         int            NULL,
    Kind          tinyint        NOT NULL,
    InStock       bit            NOT NULL,
    Amount        decimal(19,2)  NOT NULL
);
GO

CREATE TYPE inv.tt_MovementCostUpdate AS TABLE (
    MovementId bigint         NOT NULL PRIMARY KEY,
    UnitCost   decimal(19,6)  NOT NULL,
    CostAmount decimal(19,2)  NOT NULL
);
GO

-- Stornarea se costeaza acum in motor: procedura veche dispare (08, 5.9).
DROP PROCEDURE IF EXISTS inv.usp_StockMovement_Reverse;
GO
