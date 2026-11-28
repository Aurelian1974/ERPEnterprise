-- Stocuri S1 (02.05, S1 3.2 si 3.4; 7): gestionarii si predarea-primirea gestiunii, setarile de stoc ale modelului pe firma si pragurile pe
-- gestiune. Inventarul de predare (InventoryCountId) primeste cheia straina odata cu inventarul (S6).

CREATE TYPE inv.IdList AS TABLE (Id int NOT NULL PRIMARY KEY);
GO

CREATE TABLE inv.WarehouseHandover (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WarehouseHandover PRIMARY KEY,
    WarehouseId      int               NOT NULL CONSTRAINT FK_WarehouseHandover_Warehouse REFERENCES inv.Warehouse (Id),
    HandoverDate     date              NOT NULL,
    FromEmployeeId   int               NULL CONSTRAINT FK_WarehouseHandover_Employee_From REFERENCES core.Employee (Id),
    ToEmployeeId     int               NOT NULL CONSTRAINT FK_WarehouseHandover_Employee_To REFERENCES core.Employee (Id),
    RequiresCount    bit               NOT NULL,
    InventoryCountId int               NULL,
    Status           tinyint           NOT NULL,   -- 1 Ciorna, 2 Inventar in curs, 3 Finalizata, 4 Anulata
    Notes            nvarchar(1000)    NULL,
    CompletedAt      datetime2(3)      NULL,
    CreatedAt        datetime2(3)      NOT NULL CONSTRAINT DF_WarehouseHandover_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int               NOT NULL,
    ModifiedAt       datetime2(3)      NULL,
    ModifiedBy       int               NULL,
    RowVersion       rowversion        NOT NULL,
    CONSTRAINT CK_WarehouseHandover_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_WarehouseHandover_Employees CHECK (FromEmployeeId IS NULL OR FromEmployeeId <> ToEmployeeId)
);
CREATE INDEX IX_WarehouseHandover_WarehouseId_HandoverDate ON inv.WarehouseHandover (WarehouseId, HandoverDate);
-- O singura predare deschisa (ciorna sau inventar in curs) pe gestiune.
CREATE UNIQUE INDEX UX_WarehouseHandover_Open ON inv.WarehouseHandover (WarehouseId) WHERE Status IN (1, 2);
GO

CREATE TABLE inv.WarehouseKeeper (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WarehouseKeeper PRIMARY KEY,
    WarehouseId   int               NOT NULL CONSTRAINT FK_WarehouseKeeper_Warehouse REFERENCES inv.Warehouse (Id),
    EmployeeId    int               NOT NULL CONSTRAINT FK_WarehouseKeeper_Employee REFERENCES core.Employee (Id),
    Role          tinyint           NOT NULL,   -- 1 Titular, 2 Inlocuitor
    ValidFrom     date              NOT NULL,
    ValidTo       date              NULL,
    GuaranteeNote nvarchar(500)     NULL,
    HandoverId    int               NULL CONSTRAINT FK_WarehouseKeeper_Handover REFERENCES inv.WarehouseHandover (Id),
    CreatedAt     datetime2(3)      NOT NULL CONSTRAINT DF_WarehouseKeeper_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy     int               NOT NULL,
    ModifiedAt    datetime2(3)      NULL,
    ModifiedBy    int               NULL,
    RowVersion    rowversion        NOT NULL,
    CONSTRAINT CK_WarehouseKeeper_Role CHECK (Role IN (1, 2)),
    CONSTRAINT CK_WarehouseKeeper_Period CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
CREATE INDEX IX_WarehouseKeeper_WarehouseId_Role_ValidFrom ON inv.WarehouseKeeper (WarehouseId, Role, ValidFrom);
CREATE INDEX IX_WarehouseKeeper_EmployeeId ON inv.WarehouseKeeper (EmployeeId);
GO

CREATE TABLE inv.ItemStockSettings (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemStockSettings PRIMARY KEY,
    CompanyId           int               NOT NULL CONSTRAINT FK_ItemStockSettings_Company REFERENCES core.Company (Id),
    ItemId              int               NOT NULL CONSTRAINT FK_ItemStockSettings_Item REFERENCES core.Item (Id),
    DefaultWarehouseId  int               NULL CONSTRAINT FK_ItemStockSettings_Warehouse REFERENCES inv.Warehouse (Id),
    ValuationMethod     tinyint           NOT NULL,   -- 1 FIFO, 2 CMP, 3 LIFO, 4 Identificare directa
    TrackingMode        tinyint           NOT NULL CONSTRAINT DF_ItemStockSettings_TrackingMode DEFAULT (0),
    MinQty              decimal(19,6)     NULL,
    MaxQty              decimal(19,6)     NULL,
    ReplenishmentMethod tinyint           NOT NULL CONSTRAINT DF_ItemStockSettings_ReplenishmentMethod DEFAULT (0),
    IsBlockedForIssue   bit               NOT NULL CONSTRAINT DF_ItemStockSettings_IsBlockedForIssue DEFAULT (0),
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_ItemStockSettings_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int               NOT NULL,
    ModifiedAt          datetime2(3)      NULL,
    ModifiedBy          int               NULL,
    RowVersion          rowversion        NOT NULL,
    CONSTRAINT UQ_ItemStockSettings_Company_Item UNIQUE (CompanyId, ItemId),
    CONSTRAINT CK_ItemStockSettings_ValuationMethod CHECK (ValuationMethod BETWEEN 1 AND 4),
    CONSTRAINT CK_ItemStockSettings_TrackingMode CHECK (TrackingMode BETWEEN 0 AND 4),
    CONSTRAINT CK_ItemStockSettings_SpecificIdentification CHECK (ValuationMethod <> 4 OR TrackingMode <> 0),
    CONSTRAINT CK_ItemStockSettings_Qty CHECK ((MinQty IS NULL OR MinQty >= 0) AND (MaxQty IS NULL OR MaxQty >= 0)
        AND (MinQty IS NULL OR MaxQty IS NULL OR MaxQty >= MinQty)),
    CONSTRAINT CK_ItemStockSettings_ReplenishmentMethod CHECK (ReplenishmentMethod BETWEEN 0 AND 2)
);
CREATE INDEX IX_ItemStockSettings_DefaultWarehouseId ON inv.ItemStockSettings (DefaultWarehouseId);
GO

CREATE TABLE inv.ItemWarehouseSettings (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemWarehouseSettings PRIMARY KEY,
    WarehouseId         int               NOT NULL CONSTRAINT FK_ItemWarehouseSettings_Warehouse REFERENCES inv.Warehouse (Id),
    ItemId              int               NOT NULL CONSTRAINT FK_ItemWarehouseSettings_Item REFERENCES core.Item (Id),
    ItemVariantId       int               NULL,
    MinQty              decimal(19,6)     NULL,
    MaxQty              decimal(19,6)     NULL,
    ReorderQty          decimal(19,6)     NULL,
    ReplenishmentMethod tinyint           NULL,
    IsBlockedForIssue   bit               NOT NULL CONSTRAINT DF_ItemWarehouseSettings_IsBlockedForIssue DEFAULT (0),
    IsBlockedForReceipt bit               NOT NULL CONSTRAINT DF_ItemWarehouseSettings_IsBlockedForReceipt DEFAULT (0),
    IsListed            bit               NOT NULL CONSTRAINT DF_ItemWarehouseSettings_IsListed DEFAULT (1),
    CreatedAt           datetime2(3)      NOT NULL CONSTRAINT DF_ItemWarehouseSettings_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int               NOT NULL,
    ModifiedAt          datetime2(3)      NULL,
    ModifiedBy          int               NULL,
    RowVersion          rowversion        NOT NULL,
    -- Varianta e a modelului liniei (UQ_ItemVariant_Item).
    CONSTRAINT FK_ItemWarehouseSettings_ItemVariant FOREIGN KEY (ItemVariantId, ItemId) REFERENCES core.ItemVariant (Id, ItemId),
    CONSTRAINT CK_ItemWarehouseSettings_Qty CHECK ((MinQty IS NULL OR MinQty >= 0) AND (MaxQty IS NULL OR MaxQty >= 0)
        AND (ReorderQty IS NULL OR ReorderQty > 0) AND (MinQty IS NULL OR MaxQty IS NULL OR MaxQty >= MinQty)),
    CONSTRAINT CK_ItemWarehouseSettings_ReplenishmentMethod CHECK (ReplenishmentMethod IS NULL OR ReplenishmentMethod BETWEEN 0 AND 2)
);
CREATE UNIQUE INDEX UX_ItemWarehouseSettings_Item ON inv.ItemWarehouseSettings (WarehouseId, ItemId) WHERE ItemVariantId IS NULL;
CREATE UNIQUE INDEX UX_ItemWarehouseSettings_Variant ON inv.ItemWarehouseSettings (WarehouseId, ItemVariantId) WHERE ItemVariantId IS NOT NULL;
CREATE INDEX IX_ItemWarehouseSettings_ItemId ON inv.ItemWarehouseSettings (ItemId);
CREATE INDEX IX_ItemWarehouseSettings_ItemVariantId ON inv.ItemWarehouseSettings (ItemVariantId);
GO
