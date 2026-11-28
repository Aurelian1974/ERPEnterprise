-- Stocuri S3.3 (02.05, S3 3.7): gestiunile la pret cu amanuntul. Metoda de descarcare (1 lunar prin K, 2 pe fiecare vanzare) pe firma, cu
-- suprascriere pe gestiune; pretul cu amanuntul in vigoare pe gestiune si varianta (stabilit de documentul MP); TVA-ul inclus in valoarea cu
-- amanuntul a fiecarei miscari; valoarea cu amanuntul a soldului; documentele MP (modificarea pretului) si DA (descarcarea lunara).

ALTER TABLE inv.StockSettings ADD RetailDischargeMethod tinyint NOT NULL CONSTRAINT DF_StockSettings_RetailDischargeMethod DEFAULT (1)
    CONSTRAINT CK_StockSettings_RetailDischargeMethod CHECK (RetailDischargeMethod IN (1, 2));
ALTER TABLE inv.Warehouse ADD RetailDischargeMethod tinyint NULL
    CONSTRAINT CK_Warehouse_RetailDischargeMethod CHECK (RetailDischargeMethod IS NULL OR RetailDischargeMethod IN (1, 2));
ALTER TABLE inv.Stock ADD RetailValueOnHand decimal(19,2) NOT NULL CONSTRAINT DF_Stock_RetailValueOnHand DEFAULT (0);
ALTER TABLE inv.StockMovement ADD RetailVatAmount decimal(19,2) NULL;
GO

CREATE TABLE inv.RetailPriceChange (
    Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RetailPriceChange PRIMARY KEY,
    CompanyId      int            NOT NULL CONSTRAINT FK_RetailPriceChange_Company REFERENCES core.Company (Id),
    WarehouseId    int            NOT NULL CONSTRAINT FK_RetailPriceChange_Warehouse REFERENCES inv.Warehouse (Id),
    Number         nvarchar(40)   NULL,
    DocumentDate   date           NOT NULL,
    Status         tinyint        NOT NULL CONSTRAINT DF_RetailPriceChange_Status DEFAULT (1),   -- 1 Ciorna, 2 Validat, 4 Anulat
    Notes          nvarchar(1000) NULL,
    DiffAmount     decimal(19,2)  NULL,      -- dupa validare: diferenta cu TVA pe stocul existent
    DiffVatAmount  decimal(19,2)  NULL,
    ValidatedAt    datetime2(3)   NULL,
    ValidatedBy    int            NULL CONSTRAINT FK_RetailPriceChange_User_ValidatedBy REFERENCES core.[User] (Id),
    CreatedAt      datetime2(3)   NOT NULL CONSTRAINT DF_RetailPriceChange_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy      int            NOT NULL,
    ModifiedAt     datetime2(3)   NULL,
    ModifiedBy     int            NULL,
    RowVersion     rowversion     NOT NULL,
    CONSTRAINT CK_RetailPriceChange_Status CHECK (Status IN (1, 2, 4)),
    CONSTRAINT CK_RetailPriceChange_Number CHECK (Status <> 2 OR Number IS NOT NULL)
);
CREATE UNIQUE INDEX UX_RetailPriceChange_Number ON inv.RetailPriceChange (CompanyId, Number) WHERE Number IS NOT NULL;
CREATE INDEX IX_RetailPriceChange_Warehouse_Date ON inv.RetailPriceChange (WarehouseId, DocumentDate);
GO

CREATE TABLE inv.RetailPriceChangeLine (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RetailPriceChangeLine PRIMARY KEY,
    RetailPriceChangeId int            NOT NULL CONSTRAINT FK_RetailPriceChangeLine_Document REFERENCES inv.RetailPriceChange (Id) ON DELETE CASCADE,
    LineNumber          int            NOT NULL,
    ItemVariantId       int            NOT NULL CONSTRAINT FK_RetailPriceChangeLine_ItemVariant REFERENCES core.ItemVariant (Id),
    NewPrice            decimal(19,6)  NOT NULL,
    OldPrice            decimal(19,6)  NULL,      -- la validare
    Qty                 decimal(19,6)  NULL,      -- stocul gestiunii la validare
    VatPercent          decimal(5,2)   NULL,
    DiffAmount          decimal(19,2)  NULL,
    DiffVatAmount       decimal(19,2)  NULL,
    CONSTRAINT UX_RetailPriceChangeLine_Variant UNIQUE (RetailPriceChangeId, ItemVariantId),
    CONSTRAINT CK_RetailPriceChangeLine_Price CHECK (NewPrice >= 0)
);
GO

CREATE TABLE inv.RetailPrice (
    Id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RetailPrice PRIMARY KEY,
    CompanyId           int            NOT NULL CONSTRAINT FK_RetailPrice_Company REFERENCES core.Company (Id),
    WarehouseId         int            NOT NULL CONSTRAINT FK_RetailPrice_Warehouse REFERENCES inv.Warehouse (Id),
    ItemVariantId       int            NOT NULL CONSTRAINT FK_RetailPrice_ItemVariant REFERENCES core.ItemVariant (Id),
    Price               decimal(19,6)  NOT NULL,   -- cu TVA
    ValidFrom           date           NOT NULL,
    RetailPriceChangeId int            NULL CONSTRAINT FK_RetailPrice_RetailPriceChange REFERENCES inv.RetailPriceChange (Id),
    CreatedAt           datetime2(3)   NOT NULL CONSTRAINT DF_RetailPrice_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy           int            NOT NULL,
    CONSTRAINT UX_RetailPrice_Key UNIQUE (WarehouseId, ItemVariantId, ValidFrom),
    CONSTRAINT CK_RetailPrice_Price CHECK (Price >= 0)
);
GO

CREATE TABLE inv.RetailDischarge (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RetailDischarge PRIMARY KEY,
    CompanyId       int            NOT NULL CONSTRAINT FK_RetailDischarge_Company REFERENCES core.Company (Id),
    WarehouseId     int            NOT NULL CONSTRAINT FK_RetailDischarge_Warehouse REFERENCES inv.Warehouse (Id),
    PeriodStart     date           NOT NULL,
    Number          nvarchar(40)   NOT NULL,
    OpeningRetail   decimal(19,2)  NOT NULL,   -- Si 371
    RetailIn        decimal(19,2)  NOT NULL,   -- Rd 371
    RetailOut       decimal(19,2)  NOT NULL,   -- Rc 371
    OpeningMarkup   decimal(19,2)  NOT NULL,   -- Si 378
    MarkupIn        decimal(19,2)  NOT NULL,   -- Rc 378
    OpeningVat      decimal(19,2)  NOT NULL,   -- Si 4428
    VatIn           decimal(19,2)  NOT NULL,   -- Rc 4428
    SalesVat        decimal(19,2)  NOT NULL,
    SalesNet        decimal(19,2)  NOT NULL,
    K               decimal(9,6)   NOT NULL,
    Markup          decimal(19,2)  NOT NULL,
    Cost            decimal(19,2)  NOT NULL,
    ValidatedAt     datetime2(3)   NOT NULL CONSTRAINT DF_RetailDischarge_ValidatedAt DEFAULT (sysutcdatetime()),
    ValidatedBy     int            NOT NULL,
    CONSTRAINT UX_RetailDischarge_Period UNIQUE (WarehouseId, PeriodStart),
    CONSTRAINT UX_RetailDischarge_Number UNIQUE (CompanyId, Number)
);
GO

CREATE TYPE inv.tt_RetailPriceChangeLine AS TABLE (
    LineNumber    int            NOT NULL,
    ItemVariantId int            NOT NULL PRIMARY KEY,
    NewPrice      decimal(19,6)  NOT NULL
);
GO
