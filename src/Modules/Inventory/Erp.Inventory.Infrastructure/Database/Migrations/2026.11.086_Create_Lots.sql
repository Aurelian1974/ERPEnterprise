-- Stocuri S4.1 (02.05, S4 2, 3.1, 3.2, 3.3, 7): loturile (CKU = SKU + cod de lot + termen), istoricul starii lor, setarile de lot pe model si
-- legatura miscarilor, straturilor si soldului cu lotul. O linie de document se poate imparti pe loturi (alocarea FEFO), deci cheia de
-- idempotenta a miscarilor include si lotul (si seria, pentru S4.3).

ALTER TABLE inv.ItemStockSettings ADD
    ExpiryKind                tinyint NOT NULL CONSTRAINT DF_ItemStockSettings_ExpiryKind DEFAULT (1)
        CONSTRAINT CK_ItemStockSettings_ExpiryKind CHECK (ExpiryKind IN (1, 2)),
    ShelfLifeDays             int     NULL CONSTRAINT CK_ItemStockSettings_ShelfLife CHECK (ShelfLifeDays IS NULL OR ShelfLifeDays > 0),
    MinRemainingShelfLifeDays int     NOT NULL CONSTRAINT DF_ItemStockSettings_MinRemaining DEFAULT (0)
        CONSTRAINT CK_ItemStockSettings_MinRemaining CHECK (MinRemainingShelfLifeDays >= 0),
    ReceiptQuarantine         bit     NOT NULL CONSTRAINT DF_ItemStockSettings_ReceiptQuarantine DEFAULT (0),
    LotNumbering              tinyint NOT NULL CONSTRAINT DF_ItemStockSettings_LotNumbering DEFAULT (1)
        CONSTRAINT CK_ItemStockSettings_LotNumbering CHECK (LotNumbering IN (1, 2)),
    SerialCaptureMode         tinyint NOT NULL CONSTRAINT DF_ItemStockSettings_SerialCapture DEFAULT (1)
        CONSTRAINT CK_ItemStockSettings_SerialCapture CHECK (SerialCaptureMode IN (1, 2)),
    WarrantyMonths            int     NULL CONSTRAINT CK_ItemStockSettings_Warranty CHECK (WarrantyMonths IS NULL OR WarrantyMonths >= 0);
ALTER TABLE inv.StockSettings ADD UnidentifiedLotCloseCheck tinyint NOT NULL CONSTRAINT DF_StockSettings_UnidentifiedLot DEFAULT (1)
    CONSTRAINT CK_StockSettings_UnidentifiedLot CHECK (UnidentifiedLotCloseCheck IN (0, 1, 2));
GO

CREATE TABLE inv.Lot (
    Id                     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Lot PRIMARY KEY,
    CompanyId              int            NOT NULL CONSTRAINT FK_Lot_Company REFERENCES core.Company (Id),
    ItemVariantId          int            NOT NULL CONSTRAINT FK_Lot_ItemVariant REFERENCES core.ItemVariant (Id),
    LotCode                nvarchar(40)   NOT NULL,     -- comparat exact (collation-ul bazei, case sensitive)
    LotCodeSearch          AS CAST(LotCode AS nvarchar(40)) COLLATE Latin1_General_100_CI_AI_SC_UTF8 PERSISTED,
    CkuCode                nvarchar(120)  NOT NULL,     -- {SKU}·{lot}·{AAMMZZ}, generat la creare
    CkuCodeSearch          AS CAST(CkuCode AS nvarchar(120)) COLLATE Latin1_General_100_CI_AI_SC_UTF8 PERSISTED,
    CkuUid                 uniqueidentifier NOT NULL CONSTRAINT DF_Lot_CkuUid DEFAULT (NEWID()),
    SupplierLotCode        nvarchar(40)   NULL,
    SupplierPartnerId      int            NULL CONSTRAINT FK_Lot_Supplier REFERENCES core.Partner (Id),
    ProductionDate         date           NULL,
    ExpiryDate             date           NULL,
    ExpiryKey              AS ISNULL(ExpiryDate, CONVERT(date, '19000101', 112)) PERSISTED,
    ExpiryKind             tinyint        NULL,         -- copiat din ItemStockSettings la creare: 1 data limita de consum, 2 durabilitate minimala
    CountryOfOriginCode    char(2)        NULL CONSTRAINT FK_Lot_Country REFERENCES core.Country (Code),
    Status                 tinyint        NOT NULL CONSTRAINT DF_Lot_Status DEFAULT (1),   -- 1 Disponibil, 2 Carantina, 3 Blocat, 4 Expirat, 5 Retras
    IsTechnical            bit            NOT NULL CONSTRAINT DF_Lot_IsTechnical DEFAULT (0),   -- 1 = NEIDENTIFICAT
    FirstReceiptMovementId bigint         NULL CONSTRAINT FK_Lot_FirstReceipt REFERENCES inv.StockMovement (Id),
    FirstReceiptDate       date           NULL,
    StatusReason           nvarchar(400)  NULL,
    CreatedAt              datetime2(3)   NOT NULL CONSTRAINT DF_Lot_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy              int            NOT NULL,
    ModifiedAt             datetime2(3)   NULL,
    ModifiedBy             int            NULL,
    RowVersion             rowversion     NOT NULL,
    CONSTRAINT UX_Lot_Cku UNIQUE (CompanyId, ItemVariantId, LotCode, ExpiryKey),
    CONSTRAINT UX_Lot_CkuUid UNIQUE (CkuUid),
    CONSTRAINT CK_Lot_Status CHECK (Status IN (1, 2, 3, 4, 5)),
    CONSTRAINT CK_Lot_ExpiryKind CHECK (ExpiryKind IS NULL OR ExpiryKind IN (1, 2)),
    CONSTRAINT CK_Lot_Dates CHECK (ProductionDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate > ProductionDate),
    CONSTRAINT CK_Lot_Technical CHECK (IsTechnical = 0 OR (Status = 1 AND ExpiryDate IS NULL))
);
CREATE UNIQUE INDEX UX_Lot_Technical ON inv.Lot (CompanyId, ItemVariantId) WHERE IsTechnical = 1;
CREATE INDEX IX_Lot_Pick ON inv.Lot (ItemVariantId, Status, ExpiryDate) INCLUDE (LotCode, FirstReceiptDate);
CREATE INDEX IX_Lot_SupplierLot ON inv.Lot (SupplierPartnerId, SupplierLotCode) WHERE SupplierLotCode IS NOT NULL;
CREATE INDEX IX_Lot_Search ON inv.Lot (LotCodeSearch) INCLUDE (ItemVariantId, CompanyId);
CREATE INDEX IX_Lot_CkuSearch ON inv.Lot (CkuCodeSearch) INCLUDE (ItemVariantId, CompanyId);
GO

-- Doar de adaugare (abatere SQL-11, fara RowVersion).
CREATE TABLE inv.LotStatusHistory (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LotStatusHistory PRIMARY KEY,
    LotId              int            NOT NULL CONSTRAINT FK_LotStatusHistory_Lot REFERENCES inv.Lot (Id),
    FromStatus         tinyint        NULL,
    ToStatus           tinyint        NOT NULL,
    Reason             nvarchar(400)  NULL,
    SourceDocumentType varchar(30)    NULL,
    SourceDocumentId   bigint         NULL,
    ChangedAt          datetime2(3)   NOT NULL CONSTRAINT DF_LotStatusHistory_ChangedAt DEFAULT (sysutcdatetime()),
    ChangedBy          int            NOT NULL
);
CREATE INDEX IX_LotStatusHistory_Lot ON inv.LotStatusHistory (LotId, ChangedAt);
GO

-- Miscarile, straturile si soldul trimit la lot; o linie sursa poate avea mai multe miscari, cate una pe lot (si pe serie).
ALTER TABLE inv.StockMovement ADD
    LotKey    AS ISNULL(LotId, 0) PERSISTED,
    SerialKey AS ISNULL(SerialId, 0) PERSISTED,
    LotOverrideReason tinyint NULL;
GO
DROP INDEX UX_StockMovement_Source ON inv.StockMovement;
CREATE UNIQUE INDEX UX_StockMovement_Source ON inv.StockMovement (SourceModule, SourceLineId, SourceRole, ReversalKey, LotKey, SerialKey);
CREATE INDEX IX_StockMovement_LotId ON inv.StockMovement (LotId) WHERE LotId IS NOT NULL;
ALTER TABLE inv.StockMovement ADD CONSTRAINT FK_StockMovement_Lot FOREIGN KEY (LotId) REFERENCES inv.Lot (Id);
ALTER TABLE inv.Stock ADD CONSTRAINT FK_Stock_Lot FOREIGN KEY (LotId) REFERENCES inv.Lot (Id);
ALTER TABLE inv.CostLayer ADD CONSTRAINT FK_CostLayer_Lot FOREIGN KEY (LotId) REFERENCES inv.Lot (Id);
GO

-- Liniile de rezolvat ale unui apel (inv.usp_Lot_ResolveForMovements).
CREATE TYPE inv.tt_LotRequest AS TABLE (
    RowNo             int            NOT NULL PRIMARY KEY,
    SourceLineId      bigint         NOT NULL,
    SourceRole        tinyint        NOT NULL,
    MovementTypeCode  varchar(30)    NOT NULL,
    WarehouseId       int            NOT NULL,
    DepotId           int            NULL,
    ItemVariantId     int            NOT NULL,
    QtyBase           decimal(19,6)  NOT NULL,
    LotId             int            NULL,
    CkuUid            uniqueidentifier NULL,
    NewLotCode        nvarchar(40)   NULL,
    ExpiryDate        date           NULL,
    ProductionDate    date           NULL,
    SupplierLotCode   nvarchar(40)   NULL,
    CountryOfOriginCode char(2)      NULL,
    ConfirmNewExpiry  bit            NOT NULL,
    ReversalOfId      bigint         NULL,
    IsCounter         bit            NOT NULL
);
GO
