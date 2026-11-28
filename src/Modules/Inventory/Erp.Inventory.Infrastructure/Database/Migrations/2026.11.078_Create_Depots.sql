-- Stocuri S1 (02.05, S1 3.8; 7): nivelul de evidenta suprascris pe punct de lucru sau locatie, depozitele, gestiunile din depozite si regulile
-- de propunere a gestiunii liniei. Depozitul e si valoare a dimensiunii „depozit” din Nucleu (1:1).

CREATE TABLE inv.StockLevelOverride (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockLevelOverride PRIMARY KEY,
    CompanyId         int               NOT NULL CONSTRAINT FK_StockLevelOverride_Company REFERENCES core.Company (Id),
    BranchId          int               NULL CONSTRAINT FK_StockLevelOverride_Branch REFERENCES core.Branch (Id),
    LocationId        int               NULL CONSTRAINT FK_StockLevelOverride_Location REFERENCES core.Location (Id),
    StockKeepingLevel tinyint           NOT NULL,   -- 1 Gestiune, 2 Depozit
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_StockLevelOverride_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CONSTRAINT CK_StockLevelOverride_Target CHECK ((BranchId IS NULL AND LocationId IS NOT NULL) OR (BranchId IS NOT NULL AND LocationId IS NULL)),
    CONSTRAINT CK_StockLevelOverride_Level CHECK (StockKeepingLevel IN (1, 2))
);
CREATE UNIQUE INDEX UX_StockLevelOverride_Branch ON inv.StockLevelOverride (BranchId) WHERE BranchId IS NOT NULL;
CREATE UNIQUE INDEX UX_StockLevelOverride_Location ON inv.StockLevelOverride (LocationId) WHERE LocationId IS NOT NULL;
GO

CREATE TABLE inv.Depot (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Depot PRIMARY KEY,
    CompanyId          int               NOT NULL CONSTRAINT FK_Depot_Company REFERENCES core.Company (Id),
    Code               nvarchar(20)      NOT NULL,
    CodeKey            AS UPPER(Code) PERSISTED,
    Name               nvarchar(200)     NOT NULL,
    LocationId         int               NOT NULL CONSTRAINT FK_Depot_Location REFERENCES core.Location (Id),
    DefaultWarehouseId int               NULL CONSTRAINT FK_Depot_Warehouse REFERENCES inv.Warehouse (Id),
    DimensionValueId   int               NOT NULL CONSTRAINT FK_Depot_DimensionValue REFERENCES core.DimensionValue (Id),
    IsBlocked          bit               NOT NULL CONSTRAINT DF_Depot_IsBlocked DEFAULT (0),
    BlockReason        nvarchar(250)     NULL,
    IsActive           bit               NOT NULL CONSTRAINT DF_Depot_IsActive DEFAULT (1),
    CreatedAt          datetime2(3)      NOT NULL CONSTRAINT DF_Depot_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int               NOT NULL,
    ModifiedAt         datetime2(3)      NULL,
    ModifiedBy         int               NULL,
    RowVersion         rowversion        NOT NULL,
    CONSTRAINT UQ_Depot_DimensionValue UNIQUE (DimensionValueId),
    CONSTRAINT CK_Depot_Blocked CHECK (IsBlocked = 0 OR BlockReason IS NOT NULL)
);
CREATE UNIQUE INDEX UX_Depot_CodeKey ON inv.Depot (CompanyId, CodeKey);
CREATE INDEX IX_Depot_LocationId ON inv.Depot (LocationId);
GO

CREATE TABLE inv.DepotWarehouse (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DepotWarehouse PRIMARY KEY,
    DepotId     int               NOT NULL CONSTRAINT FK_DepotWarehouse_Depot REFERENCES inv.Depot (Id),
    WarehouseId int               NOT NULL CONSTRAINT FK_DepotWarehouse_Warehouse REFERENCES inv.Warehouse (Id),
    IsActive    bit               NOT NULL CONSTRAINT DF_DepotWarehouse_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_DepotWarehouse_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    CONSTRAINT UQ_DepotWarehouse UNIQUE (DepotId, WarehouseId)
);
CREATE INDEX IX_DepotWarehouse_WarehouseId ON inv.DepotWarehouse (WarehouseId);
GO

CREATE TABLE inv.DepotWarehouseRule (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DepotWarehouseRule PRIMARY KEY,
    DepotId     int               NOT NULL CONSTRAINT FK_DepotWarehouseRule_Depot REFERENCES inv.Depot (Id),
    ItemGroupId int               NULL CONSTRAINT FK_DepotWarehouseRule_ItemGroup REFERENCES core.ItemGroup (Id),
    ItemId      int               NULL CONSTRAINT FK_DepotWarehouseRule_Item REFERENCES core.Item (Id),
    WarehouseId int               NOT NULL CONSTRAINT FK_DepotWarehouseRule_Warehouse REFERENCES inv.Warehouse (Id),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_DepotWarehouseRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    CONSTRAINT CK_DepotWarehouseRule_Target CHECK ((ItemGroupId IS NULL AND ItemId IS NOT NULL) OR (ItemGroupId IS NOT NULL AND ItemId IS NULL))
);
CREATE UNIQUE INDEX UX_DepotWarehouseRule_Group ON inv.DepotWarehouseRule (DepotId, ItemGroupId) WHERE ItemId IS NULL;
CREATE UNIQUE INDEX UX_DepotWarehouseRule_Item ON inv.DepotWarehouseRule (DepotId, ItemId) WHERE ItemId IS NOT NULL;
GO
