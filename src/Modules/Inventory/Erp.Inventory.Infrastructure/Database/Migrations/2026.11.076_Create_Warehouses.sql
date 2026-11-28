-- Stocuri S1 (02.05, S1 3.1 si 3.3; 7): setarile de stoc ale firmei si gestiunile. Gestiunea e si valoare a dimensiunii „gestiune” din Nucleu
-- (1:1). Lista de preturi cu amanuntul (RetailPriceListId) primeste cheia straina in migrarea modulului Vanzari.

CREATE TABLE inv.StockSettings (
    Id                         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockSettings PRIMARY KEY,
    CompanyId                  int               NOT NULL CONSTRAINT FK_StockSettings_Company REFERENCES core.Company (Id),
    DefaultValuationMethod     tinyint           NOT NULL CONSTRAINT DF_StockSettings_DefaultValuationMethod DEFAULT (2),
    StockKeepingLevel          tinyint           NOT NULL CONSTRAINT DF_StockSettings_StockKeepingLevel DEFAULT (1),
    DefaultNegativeStockPolicy tinyint           NOT NULL CONSTRAINT DF_StockSettings_DefaultNegativeStockPolicy DEFAULT (1),
    RequireCountOnHandover     bit               NOT NULL CONSTRAINT DF_StockSettings_RequireCountOnHandover DEFAULT (1),
    BackdatingDays             smallint          NOT NULL CONSTRAINT DF_StockSettings_BackdatingDays DEFAULT (5),
    BackdatingWithRight        bit               NOT NULL CONSTRAINT DF_StockSettings_BackdatingWithRight DEFAULT (1),
    TransferMode               tinyint           NOT NULL CONSTRAINT DF_StockSettings_TransferMode DEFAULT (1),
    CostRecalcScope            tinyint           NOT NULL CONSTRAINT DF_StockSettings_CostRecalcScope DEFAULT (1),
    LandedCostAllocation       tinyint           NOT NULL CONSTRAINT DF_StockSettings_LandedCostAllocation DEFAULT (1),
    ExpiryPickRule             tinyint           NOT NULL CONSTRAINT DF_StockSettings_ExpiryPickRule DEFAULT (1),
    ExpiryWarningDays          smallint          NOT NULL CONSTRAINT DF_StockSettings_ExpiryWarningDays DEFAULT (30),
    ReservationExpiryDays      smallint          NOT NULL CONSTRAINT DF_StockSettings_ReservationExpiryDays DEFAULT (14),
    CountFreezeMode            tinyint           NOT NULL CONSTRAINT DF_StockSettings_CountFreezeMode DEFAULT (1),
    CreatedAt                  datetime2(3)      NOT NULL CONSTRAINT DF_StockSettings_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy                  int               NOT NULL,
    ModifiedAt                 datetime2(3)      NULL,
    ModifiedBy                 int               NULL,
    RowVersion                 rowversion        NOT NULL,
    CONSTRAINT UQ_StockSettings_Company UNIQUE (CompanyId),
    CONSTRAINT CK_StockSettings_DefaultValuationMethod CHECK (DefaultValuationMethod BETWEEN 1 AND 4),
    CONSTRAINT CK_StockSettings_StockKeepingLevel CHECK (StockKeepingLevel IN (1, 2)),
    CONSTRAINT CK_StockSettings_DefaultNegativeStockPolicy CHECK (DefaultNegativeStockPolicy IN (1, 2)),
    CONSTRAINT CK_StockSettings_BackdatingDays CHECK (BackdatingDays BETWEEN 0 AND 366),
    CONSTRAINT CK_StockSettings_TransferMode CHECK (TransferMode IN (1, 2, 3)),
    CONSTRAINT CK_StockSettings_CostRecalcScope CHECK (CostRecalcScope IN (1, 2)),
    CONSTRAINT CK_StockSettings_LandedCostAllocation CHECK (LandedCostAllocation IN (1, 2, 3)),
    CONSTRAINT CK_StockSettings_ExpiryPickRule CHECK (ExpiryPickRule IN (1, 2)),
    CONSTRAINT CK_StockSettings_ExpiryWarningDays CHECK (ExpiryWarningDays BETWEEN 0 AND 3650),
    CONSTRAINT CK_StockSettings_ReservationExpiryDays CHECK (ReservationExpiryDays BETWEEN 0 AND 3650),
    CONSTRAINT CK_StockSettings_CountFreezeMode CHECK (CountFreezeMode IN (1, 2))
);
GO

CREATE TABLE inv.Warehouse (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouse PRIMARY KEY,
    CompanyId            int               NOT NULL CONSTRAINT FK_Warehouse_Company REFERENCES core.Company (Id),
    Code                 nvarchar(20)      NOT NULL,
    CodeKey              AS UPPER(Code) PERSISTED,
    Name                 nvarchar(200)     NOT NULL,
    NameSearch           AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,
    LocationId           int               NULL CONSTRAINT FK_Warehouse_Location REFERENCES core.Location (Id),
    Kind                 tinyint           NOT NULL,   -- 1 Depozit, 2 Magazin, 3 Tranzit, 4 Custodie, 5 La terti
    ValuationBasis       tinyint           NOT NULL CONSTRAINT DF_Warehouse_ValuationBasis DEFAULT (1),   -- 1 Cost, 2 Pret cu amanuntul
    RetailPriceListId    int               NULL,
    IsOwnGoods           bit               NOT NULL CONSTRAINT DF_Warehouse_IsOwnGoods DEFAULT (1),
    ThirdPartyId         int               NULL CONSTRAINT FK_Warehouse_Partner_ThirdParty REFERENCES core.Partner (Id),
    NegativeStockPolicy  tinyint           NOT NULL CONSTRAINT DF_Warehouse_NegativeStockPolicy DEFAULT (1),
    DimensionValueId     int               NOT NULL CONSTRAINT FK_Warehouse_DimensionValue REFERENCES core.DimensionValue (Id),
    DefaultCostCenterId  int               NULL CONSTRAINT FK_Warehouse_DimensionValue_CostCenter REFERENCES core.DimensionValue (Id),
    IsBlocked            bit               NOT NULL CONSTRAINT DF_Warehouse_IsBlocked DEFAULT (0),
    BlockReason          nvarchar(250)     NULL,
    BlockedAt            datetime2(3)      NULL,
    BlockedBy            int               NULL CONSTRAINT FK_Warehouse_User_BlockedBy REFERENCES core.[User] (Id),
    DeliveryOrderMode    tinyint           NOT NULL CONSTRAINT DF_Warehouse_DeliveryOrderMode DEFAULT (1),
    ReturnWarehouseId    int               NULL CONSTRAINT FK_Warehouse_ReturnWarehouse REFERENCES inv.Warehouse (Id),
    Purpose              tinyint           NOT NULL CONSTRAINT DF_Warehouse_Purpose DEFAULT (1),   -- 1 Obisnuita, 2 Retur de la clienti, 3 Ambalaje SGR
    IsActive             bit               NOT NULL CONSTRAINT DF_Warehouse_IsActive DEFAULT (1),
    FirstMovementAt      datetime2(3)      NULL,
    CreatedAt            datetime2(3)      NOT NULL CONSTRAINT DF_Warehouse_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int               NOT NULL,
    ModifiedAt           datetime2(3)      NULL,
    ModifiedBy           int               NULL,
    RowVersion           rowversion        NOT NULL,
    CONSTRAINT UQ_Warehouse_DimensionValue UNIQUE (DimensionValueId),
    CONSTRAINT CK_Warehouse_Kind CHECK (Kind IN (1, 2, 3, 4, 5)),
    CONSTRAINT CK_Warehouse_ValuationBasis CHECK (ValuationBasis IN (1, 2)),
    CONSTRAINT CK_Warehouse_RetailOnlyStore CHECK (ValuationBasis = 1 OR (Kind = 2 AND RetailPriceListId IS NOT NULL)),
    -- 1, 2, 4: locatia e obligatorie la nivelul „gestiune”, goala la nivelul „depozit” (verificat in procedura).
    CONSTRAINT CK_Warehouse_Location CHECK (Kind IN (1, 2, 4) OR LocationId IS NULL),
    CONSTRAINT CK_Warehouse_ThirdParty CHECK ((Kind IN (4, 5) AND ThirdPartyId IS NOT NULL) OR (Kind IN (1, 2, 3) AND ThirdPartyId IS NULL)),
    CONSTRAINT CK_Warehouse_OwnGoods CHECK (IsOwnGoods = CASE WHEN Kind = 4 THEN 0 ELSE 1 END),
    CONSTRAINT CK_Warehouse_NegativeStockPolicy CHECK (NegativeStockPolicy IN (1, 2) AND (Kind IN (1, 2, 5) OR NegativeStockPolicy = 1)),
    CONSTRAINT CK_Warehouse_Blocked CHECK (IsBlocked = 0 OR (BlockReason IS NOT NULL AND BlockedAt IS NOT NULL)),
    CONSTRAINT CK_Warehouse_DeliveryOrderMode CHECK (DeliveryOrderMode IN (0, 1, 2)),
    CONSTRAINT CK_Warehouse_Purpose CHECK (Purpose IN (1, 2, 3) AND (Purpose <> 2 OR Kind = 1)),
    CONSTRAINT CK_Warehouse_ReturnWarehouse CHECK (ReturnWarehouseId IS NULL OR ReturnWarehouseId <> Id)
);
CREATE UNIQUE INDEX UX_Warehouse_CodeKey ON inv.Warehouse (CompanyId, CodeKey);
CREATE UNIQUE INDEX UX_Warehouse_Transit ON inv.Warehouse (CompanyId) WHERE Kind = 3;
CREATE INDEX IX_Warehouse_NameSearch ON inv.Warehouse (CompanyId, NameSearch) INCLUDE (Code, Kind, IsActive);
CREATE INDEX IX_Warehouse_LocationId ON inv.Warehouse (LocationId);
GO
