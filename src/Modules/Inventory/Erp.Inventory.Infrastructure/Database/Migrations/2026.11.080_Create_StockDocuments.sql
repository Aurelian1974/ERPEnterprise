-- Stocuri S2 (02.05, S2 3.3, 3.4, 3.6; 7): documentele proprii ale modulului (un singur tabel, cu DocumentType), liniile lor si expeditia in
-- tranzit a transferurilor in doua etape. DocumentType: NP nota de predare, NR nota de primire, NI incarcare, ND descarcare, BC bon de consum,
-- PF nota de predare produse, CI / CE custodie. Stornarea e un document de acelasi tip, cu IsReversal = 1 si cantitatile cu semn opus.

CREATE TABLE inv.StockDocument (
    Id                   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDocument PRIMARY KEY,
    CompanyId            int               NOT NULL CONSTRAINT FK_StockDocument_Company REFERENCES core.Company (Id),
    DocumentType         varchar(2)        NOT NULL,
    Number               nvarchar(40)      NULL,
    DocumentDate         date              NOT NULL,
    AccountingDate       date              NOT NULL,
    FromDepotId          int               NULL CONSTRAINT FK_StockDocument_Depot_From REFERENCES inv.Depot (Id),
    FromWarehouseId      int               NULL CONSTRAINT FK_StockDocument_Warehouse_From REFERENCES inv.Warehouse (Id),
    ToDepotId            int               NULL CONSTRAINT FK_StockDocument_Depot_To REFERENCES inv.Depot (Id),
    ToWarehouseId        int               NULL CONSTRAINT FK_StockDocument_Warehouse_To REFERENCES inv.Warehouse (Id),
    ReasonId             int               NULL CONSTRAINT FK_StockDocument_MovementReason REFERENCES inv.MovementReason (Id),
    PartnerId            int               NULL CONSTRAINT FK_StockDocument_Partner REFERENCES core.Partner (Id),
    CostCenterId         int               NULL CONSTRAINT FK_StockDocument_DimensionValue_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId            int               NULL CONSTRAINT FK_StockDocument_DimensionValue_Project REFERENCES core.DimensionValue (Id),
    EmployeeId           int               NULL CONSTRAINT FK_StockDocument_Employee REFERENCES core.Employee (Id),
    TransferMode         tinyint           NULL,      -- 1 o etapa, 2 doua etape (NP)
    TransitShipmentId    int               NULL,
    RelatedDocumentId    int               NULL CONSTRAINT FK_StockDocument_Related REFERENCES inv.StockDocument (Id),
    IsReversal           bit               NOT NULL CONSTRAINT DF_StockDocument_IsReversal DEFAULT (0),
    Status               tinyint           NOT NULL CONSTRAINT DF_StockDocument_Status DEFAULT (1),   -- 1 Ciorna, 2 Validat, 3 Stornat, 4 Anulat
    ApprovalStatus       tinyint           NOT NULL CONSTRAINT DF_StockDocument_ApprovalStatus DEFAULT (0),   -- 0 nu, 1 de aprobat, 2 aprobat, 3 respins
    ApprovalNote         nvarchar(500)     NULL,
    ApprovedBy           int               NULL CONSTRAINT FK_StockDocument_User_ApprovedBy REFERENCES core.[User] (Id),
    ApprovedAt           datetime2(3)      NULL,
    Notes                nvarchar(1000)    NULL,
    IsForcedNegative     bit               NOT NULL CONSTRAINT DF_StockDocument_IsForcedNegative DEFAULT (0),
    TotalCost            decimal(19,2)     NULL,
    ValidatedAt          datetime2(3)      NULL,
    ValidatedBy          int               NULL CONSTRAINT FK_StockDocument_User_ValidatedBy REFERENCES core.[User] (Id),
    ReversedByDocumentId int               NULL CONSTRAINT FK_StockDocument_ReversedBy REFERENCES inv.StockDocument (Id),
    CreatedAt            datetime2(3)      NOT NULL CONSTRAINT DF_StockDocument_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy            int               NOT NULL,
    ModifiedAt           datetime2(3)      NULL,
    ModifiedBy           int               NULL,
    RowVersion           rowversion        NOT NULL,
    CONSTRAINT CK_StockDocument_Type CHECK (DocumentType IN ('NP', 'NR', 'NI', 'ND', 'BC', 'PF', 'CI', 'CE')),
    CONSTRAINT CK_StockDocument_Status CHECK (Status BETWEEN 1 AND 4),
    CONSTRAINT CK_StockDocument_Approval CHECK (ApprovalStatus BETWEEN 0 AND 3),
    CONSTRAINT CK_StockDocument_TransferMode CHECK (TransferMode IS NULL OR TransferMode IN (1, 2)),
    CONSTRAINT CK_StockDocument_Number CHECK (Status IN (1, 4) OR Number IS NOT NULL)
);
CREATE UNIQUE INDEX UX_StockDocument_Number ON inv.StockDocument (CompanyId, DocumentType, Number) WHERE Number IS NOT NULL;
CREATE INDEX IX_StockDocument_Company_Date ON inv.StockDocument (CompanyId, DocumentDate);
CREATE INDEX IX_StockDocument_Company_Status_Type ON inv.StockDocument (CompanyId, Status, DocumentType);
GO

CREATE TABLE inv.StockDocumentLine (
    Id                   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDocumentLine PRIMARY KEY,
    DocumentId           int               NOT NULL CONSTRAINT FK_StockDocumentLine_Document REFERENCES inv.StockDocument (Id),
    LineNumber               int               NOT NULL,
    ItemVariantId        int               NOT NULL CONSTRAINT FK_StockDocumentLine_ItemVariant REFERENCES core.ItemVariant (Id),
    ItemNameSnapshot     nvarchar(250)     NOT NULL,
    UomId                int               NULL CONSTRAINT FK_StockDocumentLine_Uom REFERENCES core.UnitOfMeasure (Id),
    Qty                  decimal(19,6)     NOT NULL,
    UomFactor            decimal(19,6)     NOT NULL CONSTRAINT DF_StockDocumentLine_UomFactor DEFAULT (1),
    QtyBase              decimal(19,6)     NOT NULL,
    UnitCost             decimal(19,6)     NULL,
    LotId                int               NULL,
    FromDepotId          int               NULL CONSTRAINT FK_StockDocumentLine_Depot_From REFERENCES inv.Depot (Id),
    FromWarehouseId      int               NULL CONSTRAINT FK_StockDocumentLine_Warehouse_From REFERENCES inv.Warehouse (Id),
    ToDepotId            int               NULL CONSTRAINT FK_StockDocumentLine_Depot_To REFERENCES inv.Depot (Id),
    ToWarehouseId        int               NULL CONSTRAINT FK_StockDocumentLine_Warehouse_To REFERENCES inv.Warehouse (Id),
    CostCenterId         int               NULL CONSTRAINT FK_StockDocumentLine_DimensionValue_CostCenter REFERENCES core.DimensionValue (Id),
    ProjectId            int               NULL CONSTRAINT FK_StockDocumentLine_DimensionValue_Project REFERENCES core.DimensionValue (Id),
    ReceivedQtyBase      decimal(19,6)     NULL,
    DifferenceQtyBase    decimal(19,6)     NULL,
    DifferenceResolution tinyint           NULL,      -- 1 ramane in tranzit, 2 retur, 3 lipsa imputabila, 4 lipsa neimputabila, 5 plus
    ReversedLineId       bigint            NULL CONSTRAINT FK_StockDocumentLine_ReversedLine REFERENCES inv.StockDocumentLine (Id),
    ShipmentLineId       bigint            NULL CONSTRAINT FK_StockDocumentLine_ShipmentLine REFERENCES inv.StockDocumentLine (Id),   -- NR: linia NP primita
    UnitCostPosted       decimal(19,6)     NULL,
    CostAmountPosted     decimal(19,2)     NULL,
    CONSTRAINT CK_StockDocumentLine_Qty CHECK (Qty <> 0 AND QtyBase <> 0 AND UomFactor > 0),
    CONSTRAINT CK_StockDocumentLine_Resolution CHECK (DifferenceResolution IS NULL OR DifferenceResolution BETWEEN 1 AND 5),
    CONSTRAINT CK_StockDocumentLine_UnitCost CHECK (UnitCost IS NULL OR UnitCost >= 0)
);
CREATE INDEX IX_StockDocumentLine_DocumentId ON inv.StockDocumentLine (DocumentId);
CREATE INDEX IX_StockDocumentLine_ItemVariantId ON inv.StockDocumentLine (ItemVariantId);
CREATE INDEX IX_StockDocumentLine_ShipmentLineId ON inv.StockDocumentLine (ShipmentLineId) WHERE ShipmentLineId IS NOT NULL;
GO

CREATE TABLE inv.TransitShipment (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransitShipment PRIMARY KEY,
    CompanyId       int               NOT NULL CONSTRAINT FK_TransitShipment_Company REFERENCES core.Company (Id),
    NpDocumentId    int               NOT NULL CONSTRAINT FK_TransitShipment_StockDocument REFERENCES inv.StockDocument (Id),
    FromWarehouseId int               NOT NULL CONSTRAINT FK_TransitShipment_Warehouse_From REFERENCES inv.Warehouse (Id),
    FromDepotId     int               NULL CONSTRAINT FK_TransitShipment_Depot_From REFERENCES inv.Depot (Id),
    ToWarehouseId   int               NOT NULL CONSTRAINT FK_TransitShipment_Warehouse_To REFERENCES inv.Warehouse (Id),
    ToDepotId       int               NULL CONSTRAINT FK_TransitShipment_Depot_To REFERENCES inv.Depot (Id),
    ShippedDate     date              NOT NULL,
    ExpectedDate    date              NULL,
    Status          tinyint           NOT NULL CONSTRAINT DF_TransitShipment_Status DEFAULT (1),   -- 1 Trimis, 2 Primit partial, 3 Primit, 4 Cu diferente, 5 Inchis
    UitCode         varchar(30)       NULL,
    CreatedAt       datetime2(3)      NOT NULL CONSTRAINT DF_TransitShipment_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy       int               NOT NULL,
    ModifiedAt      datetime2(3)      NULL,
    ModifiedBy      int               NULL,
    RowVersion      rowversion        NOT NULL,
    CONSTRAINT UQ_TransitShipment_NpDocument UNIQUE (NpDocumentId),
    CONSTRAINT CK_TransitShipment_Status CHECK (Status BETWEEN 1 AND 5)
);
CREATE INDEX IX_TransitShipment_ToWarehouse_Status ON inv.TransitShipment (ToWarehouseId, Status);
GO

ALTER TABLE inv.StockDocument ADD CONSTRAINT FK_StockDocument_TransitShipment FOREIGN KEY (TransitShipmentId) REFERENCES inv.TransitShipment (Id);
GO

-- Cantitatile primite pe liniile unei note de primire si rezolvarea diferentelor (S2 3.4), ca TVP.
CREATE TYPE inv.tt_ReceiptLine AS TABLE (
    LineId          bigint         NOT NULL PRIMARY KEY,
    ReceivedQtyBase decimal(19,6)  NOT NULL,
    Resolution      tinyint        NULL
);
GO

-- Liniile unui document salvat (ciorna), ca TVP (SP-07).
CREATE TYPE inv.tt_StockDocumentLine AS TABLE (
    LineNumber          int            NOT NULL PRIMARY KEY,
    ItemVariantId   int            NOT NULL,
    UomId           int            NULL,
    Qty             decimal(19,6)  NOT NULL,
    UomFactor       decimal(19,6)  NOT NULL,
    UnitCost        decimal(19,6)  NULL,
    FromDepotId     int            NULL,
    FromWarehouseId int            NULL,
    ToDepotId       int            NULL,
    ToWarehouseId   int            NULL,
    CostCenterId    int            NULL,
    ProjectId       int            NULL
);
GO
