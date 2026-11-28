-- Stocuri S2 (02.05, S2 3.1, 3.2, 3.5; 7): registrul unic de miscari (sursa de adevar), soldul curent si soldurile pe zile, derivate si
-- actualizate in aceeasi tranzactie, tipurile de miscare livrate si motivele de incarcare si descarcare. Abatere declarata de la SQL-19 si
-- SQL-11 pentru StockMovement si StockDayBalance (fara ModifiedAt, RowVersion): registrul nu se modifica, soldul pe zile e derivat.

CREATE TABLE inv.MovementType (
    Id                smallint      NOT NULL CONSTRAINT PK_MovementType PRIMARY KEY,
    Code              varchar(30)   NOT NULL CONSTRAINT UQ_MovementType_Code UNIQUE,
    Name              nvarchar(100) NOT NULL,
    Direction         smallint      NOT NULL,
    PostingDocumentType varchar(40) NULL,
    CONSTRAINT CK_MovementType_Direction CHECK (Direction IN (-1, 1))
);
GO

CREATE TABLE inv.MovementReason (
    Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MovementReason PRIMARY KEY,
    CompanyId          int               NULL CONSTRAINT FK_MovementReason_Company REFERENCES core.Company (Id),
    Code               varchar(30)       NOT NULL,
    CodeKey            AS UPPER(Code) PERSISTED,
    Name               nvarchar(200)     NOT NULL,
    Direction          smallint          NOT NULL,
    PostingVariant     varchar(30)       NULL,
    VatTreatment       tinyint           NOT NULL CONSTRAINT DF_MovementReason_VatTreatment DEFAULT (0),
    IsDeductible       bit               NOT NULL CONSTRAINT DF_MovementReason_IsDeductible DEFAULT (1),
    RequiresAttachment bit               NOT NULL CONSTRAINT DF_MovementReason_RequiresAttachment DEFAULT (0),
    ApprovalThreshold  decimal(19,2)     NULL,
    ApproverRoleCode   nvarchar(50)      NULL,
    NoPosting          bit               NOT NULL CONSTRAINT DF_MovementReason_NoPosting DEFAULT (0),
    CostSource         tinyint           NOT NULL CONSTRAINT DF_MovementReason_CostSource DEFAULT (1),
    IsSystem           bit               NOT NULL CONSTRAINT DF_MovementReason_IsSystem DEFAULT (0),
    IsActive           bit               NOT NULL CONSTRAINT DF_MovementReason_IsActive DEFAULT (1),
    CreatedAt          datetime2(3)      NOT NULL CONSTRAINT DF_MovementReason_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int               NOT NULL,
    ModifiedAt         datetime2(3)      NULL,
    ModifiedBy         int               NULL,
    RowVersion         rowversion        NOT NULL,
    CompanyKey         AS ISNULL(CompanyId, 0) PERSISTED,
    CONSTRAINT CK_MovementReason_Direction CHECK (Direction IN (-1, 1)),
    CONSTRAINT CK_MovementReason_VatTreatment CHECK (VatTreatment IN (0, 1, 2)),
    CONSTRAINT CK_MovementReason_CostSource CHECK (CostSource IN (1, 2, 3)),
    CONSTRAINT CK_MovementReason_Threshold CHECK (ApprovalThreshold IS NULL OR ApprovalThreshold >= 0)
);
-- Motivele livrate au CompanyId NULL si sunt comune; clientul isi adauga motive pe firma.
CREATE UNIQUE INDEX UX_MovementReason_CodeKey ON inv.MovementReason (CompanyKey, CodeKey);
GO

CREATE SEQUENCE inv.seq_StockPosting AS bigint START WITH 1 INCREMENT BY 1 CACHE 100;
GO

CREATE TABLE inv.StockMovement (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovement PRIMARY KEY,
    CompanyId          int            NOT NULL CONSTRAINT FK_StockMovement_Company REFERENCES core.Company (Id),
    DepotId            int            NULL CONSTRAINT FK_StockMovement_Depot REFERENCES inv.Depot (Id),
    WarehouseId        int            NOT NULL CONSTRAINT FK_StockMovement_Warehouse REFERENCES inv.Warehouse (Id),
    ItemVariantId      int            NOT NULL CONSTRAINT FK_StockMovement_ItemVariant REFERENCES core.ItemVariant (Id),
    LotId              int            NULL,
    SerialId           int            NULL,
    MovementDate       date           NOT NULL,
    AccountingDate     date           NOT NULL,
    PostingSeq         bigint         NOT NULL,
    MovementTypeId     smallint       NOT NULL CONSTRAINT FK_StockMovement_MovementType REFERENCES inv.MovementType (Id),
    Direction          smallint       NOT NULL,
    QtyBase            decimal(19,6)  NOT NULL,
    UnitCost           decimal(19,6)  NOT NULL,
    CostAmount         decimal(19,2)  NOT NULL,
    RetailPrice        decimal(19,6)  NULL,
    RetailAmount       decimal(19,2)  NULL,
    ReasonId           int            NULL CONSTRAINT FK_StockMovement_MovementReason REFERENCES inv.MovementReason (Id),
    PartnerId          int            NULL CONSTRAINT FK_StockMovement_Partner REFERENCES core.Partner (Id),
    CostCenterId       int            NULL CONSTRAINT FK_StockMovement_DimensionValue_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId          int            NULL CONSTRAINT FK_StockMovement_DimensionValue_Project REFERENCES core.DimensionValue (Id),
    EmployeeId         int            NULL CONSTRAINT FK_StockMovement_Employee REFERENCES core.Employee (Id),
    SourceModule       varchar(20)    NOT NULL,
    SourceDocumentType varchar(30)    NOT NULL,
    SourceDocumentId   bigint         NOT NULL,
    SourceDocumentNumber nvarchar(40) NULL,
    SourceLineId       bigint         NOT NULL,
    SourceRole         tinyint        NOT NULL CONSTRAINT DF_StockMovement_SourceRole DEFAULT (1),
    CounterMovementId  bigint         NULL,
    ReversalOfId       bigint         NULL CONSTRAINT FK_StockMovement_ReversalOf REFERENCES inv.StockMovement (Id),
    ReversalKey        AS ISNULL(ReversalOfId, 0) PERSISTED,
    IsForcedNegative   bit            NOT NULL CONSTRAINT DF_StockMovement_IsForcedNegative DEFAULT (0),
    CreatedAt          datetime2(3)   NOT NULL CONSTRAINT DF_StockMovement_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy          int            NOT NULL,
    CONSTRAINT CK_StockMovement_Direction CHECK (Direction IN (-1, 1)),
    CONSTRAINT CK_StockMovement_QtySign CHECK (QtyBase <> 0 AND SIGN(QtyBase) = Direction),
    CONSTRAINT CK_StockMovement_SourceRole CHECK (SourceRole IN (1, 2)),
    CONSTRAINT CK_StockMovement_Retail CHECK ((RetailPrice IS NULL AND RetailAmount IS NULL) OR (RetailPrice IS NOT NULL AND RetailAmount IS NOT NULL))
);
CREATE UNIQUE INDEX UX_StockMovement_Source ON inv.StockMovement (SourceModule, SourceLineId, SourceRole, ReversalKey);
CREATE INDEX IX_StockMovement_Key_Date ON inv.StockMovement (WarehouseId, ItemVariantId, MovementDate, PostingSeq) INCLUDE (DepotId, LotId, QtyBase, CostAmount);
CREATE INDEX IX_StockMovement_Company_AccountingDate ON inv.StockMovement (CompanyId, AccountingDate);
CREATE INDEX IX_StockMovement_SourceDocument ON inv.StockMovement (SourceModule, SourceDocumentId);
GO

CREATE TRIGGER inv.trg_StockMovement_NoChange ON inv.StockMovement AFTER UPDATE, DELETE AS
BEGIN
    -- S2-A12: miscarile nu se modifica si nu se sterg. Exceptie: S3 actualizeaza doar UnitCost si CostAmount la recalcularea costului, cu
    -- SESSION_CONTEXT(N'inv.CostRecalc') = 1.
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 54199, N'Mișcările de stoc nu se șterg.', 1;
    IF UPDATE(QtyBase) OR UPDATE(WarehouseId) OR UPDATE(ItemVariantId) OR UPDATE(MovementDate) OR UPDATE(DepotId) OR UPDATE(LotId)
       OR ISNULL(CAST(SESSION_CONTEXT(N'inv.CostRecalc') AS int), 0) <> 1
        THROW 54199, N'Mișcările de stoc nu se modifică; doar costul se recalculează prin S3.', 1;
END;
GO

CREATE TABLE inv.Stock (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Stock PRIMARY KEY,
    CompanyId     int            NOT NULL CONSTRAINT FK_Stock_Company REFERENCES core.Company (Id),
    WarehouseId   int            NOT NULL CONSTRAINT FK_Stock_Warehouse REFERENCES inv.Warehouse (Id),
    DepotId       int            NULL CONSTRAINT FK_Stock_Depot REFERENCES inv.Depot (Id),
    ItemVariantId int            NOT NULL CONSTRAINT FK_Stock_ItemVariant REFERENCES core.ItemVariant (Id),
    LotId         int            NULL,
    DepotKey      AS ISNULL(DepotId, 0) PERSISTED,
    LotKey        AS ISNULL(LotId, 0) PERSISTED,
    QtyOnHand     decimal(19,6)  NOT NULL CONSTRAINT DF_Stock_QtyOnHand DEFAULT (0),
    CostOnHand    decimal(19,2)  NOT NULL CONSTRAINT DF_Stock_CostOnHand DEFAULT (0),
    LastUnitCost  decimal(19,6)  NULL,
    LastMovementAt datetime2(3)  NULL
);
CREATE UNIQUE INDEX UX_Stock_Key ON inv.Stock (WarehouseId, DepotKey, ItemVariantId, LotKey);
CREATE INDEX IX_Stock_ItemVariantId ON inv.Stock (ItemVariantId) INCLUDE (QtyOnHand);
GO

CREATE TABLE inv.StockDayBalance (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDayBalance PRIMARY KEY,
    WarehouseId   int            NOT NULL CONSTRAINT FK_StockDayBalance_Warehouse REFERENCES inv.Warehouse (Id),
    DepotId       int            NULL,
    ItemVariantId int            NOT NULL,
    LotId         int            NULL,
    DepotKey      AS ISNULL(DepotId, 0) PERSISTED,
    LotKey        AS ISNULL(LotId, 0) PERSISTED,
    BalanceDate   date           NOT NULL,
    QtyIn         decimal(19,6)  NOT NULL CONSTRAINT DF_StockDayBalance_QtyIn DEFAULT (0),
    QtyOut        decimal(19,6)  NOT NULL CONSTRAINT DF_StockDayBalance_QtyOut DEFAULT (0)
);
CREATE UNIQUE INDEX UX_StockDayBalance_Key ON inv.StockDayBalance (WarehouseId, DepotKey, ItemVariantId, LotKey, BalanceDate) INCLUDE (QtyIn, QtyOut);
GO

-- Cererile de miscare ale unui apel (IStockLedger.PostAsync), ca TVP (SP-07).
CREATE TYPE inv.tt_StockMovementRequest AS TABLE (
    RowNo           int            NOT NULL PRIMARY KEY,
    SourceLineId    bigint         NOT NULL,
    SourceRole      tinyint        NOT NULL,
    MovementTypeCode varchar(30)   NOT NULL,
    DepotId         int            NULL,
    WarehouseId     int            NOT NULL,
    ItemVariantId   int            NOT NULL,
    QtyBase         decimal(19,6)  NOT NULL,
    InboundUnitCost decimal(19,6)  NULL,
    LotId           int            NULL,
    SerialId        int            NULL,
    CostCenterId    int            NULL,
    ProjectId       int            NULL,
    ReasonId        int            NULL,
    CounterRowNo    int            NULL,       -- intrarea unui transfer: randul iesirii pereche (acelasi cost)
    ReversalOfId    bigint         NULL,       -- stornarea: miscarea stornata
    FixedCostAmount decimal(19,2)  NULL        -- stornarea: valoarea originalului, cu semn opus
);
GO
