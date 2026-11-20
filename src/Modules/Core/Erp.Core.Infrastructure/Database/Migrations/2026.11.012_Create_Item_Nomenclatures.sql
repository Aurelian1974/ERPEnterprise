-- Articolele (02.01, 3.3) si nomenclatoarele lor (3.5): cote TVA, unitati de masura, seturi de UM, grupe de articole.
-- Toate sunt la nivelul instalarii (comune grupului).

-- Cotele TVA (P6): o cota noua se adauga cu „valabil de la”; cea veche primeste „valabil pana la”, nu se modifica.
-- Category: categoria UBL pentru e-Factura (S standard, Z zero, E scutit, AE taxare inversa, K livrare intracomunitara,
-- G export, O neimpozabil).
CREATE TABLE core.VatRate (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_VatRate PRIMARY KEY,
    Code       nvarchar(10)      NOT NULL,
    Name       nvarchar(100)     NOT NULL,
    [Percent]  decimal(5,2)      NOT NULL,
    Category   varchar(2)        NOT NULL,
    ValidFrom  date              NOT NULL,
    ValidTo    date              NULL,
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_VatRate_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CONSTRAINT UQ_VatRate_Code_ValidFrom UNIQUE (Code, ValidFrom),
    CONSTRAINT CK_VatRate_Percent CHECK ([Percent] BETWEEN 0 AND 100),
    CONSTRAINT CK_VatRate_Category CHECK (Category IN ('S', 'Z', 'E', 'AE', 'K', 'G', 'O')),
    CONSTRAINT CK_VatRate_Validity CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
    CONSTRAINT CK_VatRate_ZeroCategory CHECK (Category = 'S' OR [Percent] = 0)
);
GO

-- Unitatile de masura; codul UN/ECE (Recomandarile 20 si 21) e obligatoriu pentru e-Factura.
CREATE TABLE core.UnitOfMeasure (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UnitOfMeasure PRIMARY KEY,
    Code       nvarchar(10)      NOT NULL,
    Name       nvarchar(100)     NOT NULL,
    UneceCode  varchar(3)        NOT NULL,
    IsActive   bit               NOT NULL CONSTRAINT DF_UnitOfMeasure_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_UnitOfMeasure_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CodeKey    AS UPPER(Code) PERSISTED,
    CONSTRAINT UQ_UnitOfMeasure_UneceCode UNIQUE (UneceCode),
    CONSTRAINT CK_UnitOfMeasure_UneceCode CHECK (UneceCode NOT LIKE '%[^A-Z0-9]%' AND LEN(UneceCode) BETWEEN 2 AND 3)
);
CREATE UNIQUE INDEX UX_UnitOfMeasure_CodeKey ON core.UnitOfMeasure (CodeKey);
GO

-- Seturile de UM (adaugate pe 14.11.2026): conversiile definite o data si refolosite de articolele cu acelasi ambalaj.
CREATE TABLE core.UomSet (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UomSet PRIMARY KEY,
    Code       nvarchar(20)      NOT NULL,
    Name       nvarchar(100)     NOT NULL,
    IsActive   bit               NOT NULL CONSTRAINT DF_UomSet_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_UomSet_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CodeKey    AS UPPER(Code) PERSISTED
);
CREATE UNIQUE INDEX UX_UomSet_CodeKey ON core.UomSet (CodeKey);
GO

-- O singura linie de baza (factor 1) pe set; cel mult o linie implicita pe fiecare circuit (vanzare, achizitie, POS).
CREATE TABLE core.UomSetLine (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_UomSetLine PRIMARY KEY,
    UomSetId          int               NOT NULL CONSTRAINT FK_UomSetLine_UomSet REFERENCES core.UomSet (Id),
    UomId             int               NOT NULL CONSTRAINT FK_UomSetLine_Uom REFERENCES core.UnitOfMeasure (Id),
    FactorToBase      decimal(19,6)     NOT NULL,
    IsBase            bit               NOT NULL CONSTRAINT DF_UomSetLine_IsBase DEFAULT (0),
    IsDefaultSales    bit               NOT NULL CONSTRAINT DF_UomSetLine_IsDefaultSales DEFAULT (0),
    IsDefaultPurchase bit               NOT NULL CONSTRAINT DF_UomSetLine_IsDefaultPurchase DEFAULT (0),
    IsDefaultPos      bit               NOT NULL CONSTRAINT DF_UomSetLine_IsDefaultPos DEFAULT (0),
    SortOrder         int               NOT NULL CONSTRAINT DF_UomSetLine_SortOrder DEFAULT (100),
    CONSTRAINT UQ_UomSetLine_Uom UNIQUE (UomSetId, UomId),
    CONSTRAINT CK_UomSetLine_Factor CHECK (FactorToBase > 0),
    CONSTRAINT CK_UomSetLine_Base CHECK (IsBase = 0 OR FactorToBase = 1)
);
CREATE UNIQUE INDEX UX_UomSetLine_Base ON core.UomSetLine (UomSetId) WHERE IsBase = 1;
CREATE UNIQUE INDEX UX_UomSetLine_Sales ON core.UomSetLine (UomSetId) WHERE IsDefaultSales = 1;
CREATE UNIQUE INDEX UX_UomSetLine_Purchase ON core.UomSetLine (UomSetId) WHERE IsDefaultPurchase = 1;
CREATE UNIQUE INDEX UX_UomSetLine_Pos ON core.UomSetLine (UomSetId) WHERE IsDefaultPos = 1;
GO

-- Grupele de articole, arbore pe mai multe niveluri.
CREATE TABLE core.ItemGroup (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemGroup PRIMARY KEY,
    Code       nvarchar(20)      NOT NULL,
    Name       nvarchar(100)     NOT NULL,
    ParentId   int               NULL CONSTRAINT FK_ItemGroup_Parent REFERENCES core.ItemGroup (Id),
    SortOrder  int               NOT NULL CONSTRAINT DF_ItemGroup_SortOrder DEFAULT (100),
    IsActive   bit               NOT NULL CONSTRAINT DF_ItemGroup_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_ItemGroup_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CodeKey    AS UPPER(Code) PERSISTED,
    CONSTRAINT CK_ItemGroup_Parent CHECK (ParentId IS NULL OR ParentId <> Id)
);
CREATE UNIQUE INDEX UX_ItemGroup_CodeKey ON core.ItemGroup (CodeKey);
CREATE INDEX IX_ItemGroup_ParentId ON core.ItemGroup (ParentId);
GO

-- Articolul (3.3). ItemType: 1 marfa, 2 materie prima, 3 material, 4 produs finit, 5 semifabricat, 6 serviciu, 7 ambalaj,
-- 8 mijloc fix. BaseUomId e copiat din set la salvare (linia de baza). Greutatile si volumul sunt pe UM de baza.
CREATE TABLE core.Item (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Item PRIMARY KEY,
    Code              nvarchar(50)      NOT NULL,
    Name              nvarchar(250)     NOT NULL,
    ItemType          tinyint           NOT NULL,
    UomSetId          int               NOT NULL CONSTRAINT FK_Item_UomSet REFERENCES core.UomSet (Id),
    BaseUomId         int               NOT NULL CONSTRAINT FK_Item_BaseUom REFERENCES core.UnitOfMeasure (Id),
    DefaultVatRateId  int               NOT NULL CONSTRAINT FK_Item_VatRate REFERENCES core.VatRate (Id),
    ItemGroupId       int               NULL CONSTRAINT FK_Item_ItemGroup REFERENCES core.ItemGroup (Id),
    PricePrecision    tinyint           NULL,
    QuantityPrecision tinyint           NULL,
    IsStockable       bit               NOT NULL,
    CnCode            nvarchar(10)      NULL,
    DepositItemId     int               NULL CONSTRAINT FK_Item_DepositItem REFERENCES core.Item (Id),
    NetWeightKg       decimal(19,6)     NULL,
    GrossWeightKg     decimal(19,6)     NULL,
    VolumeM3          decimal(19,6)     NULL,
    IsActive          bit               NOT NULL CONSTRAINT DF_Item_IsActive DEFAULT (1),
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_Item_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CodeKey           AS UPPER(Code) PERSISTED,                                         -- SQL-24
    NameSearch        AS (Name COLLATE Latin1_General_100_CI_AI_SC_UTF8) PERSISTED,     -- SQL-25
    CONSTRAINT CK_Item_Type CHECK (ItemType BETWEEN 1 AND 8),
    CONSTRAINT CK_Item_Precision CHECK ((PricePrecision IS NULL OR PricePrecision <= 6) AND (QuantityPrecision IS NULL OR QuantityPrecision <= 6)),
    CONSTRAINT CK_Item_Service CHECK (ItemType <> 6 OR IsStockable = 0),
    CONSTRAINT CK_Item_Deposit CHECK (DepositItemId IS NULL OR DepositItemId <> Id),
    CONSTRAINT CK_Item_Measures CHECK ((NetWeightKg IS NULL OR NetWeightKg >= 0) AND (GrossWeightKg IS NULL OR GrossWeightKg >= 0)
        AND (VolumeM3 IS NULL OR VolumeM3 >= 0))
);
CREATE UNIQUE INDEX UX_Item_CodeKey ON core.Item (CodeKey);
CREATE INDEX IX_Item_NameSearch ON core.Item (NameSearch) INCLUDE (Code, Name, ItemType, IsStockable, IsActive);
CREATE INDEX IX_Item_ItemGroupId ON core.Item (ItemGroupId) WHERE ItemGroupId IS NOT NULL;
GO

-- Exceptiile articolului de la setul de UM (de exemplu bax de 12 in loc de 6); au prioritate fata de linia din set.
CREATE TABLE core.ItemUom (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemUom PRIMARY KEY,
    ItemId            int               NOT NULL CONSTRAINT FK_ItemUom_Item REFERENCES core.Item (Id),
    UomId             int               NOT NULL CONSTRAINT FK_ItemUom_Uom REFERENCES core.UnitOfMeasure (Id),
    FactorToBase      decimal(19,6)     NOT NULL,
    IsDefaultSales    bit               NOT NULL CONSTRAINT DF_ItemUom_IsDefaultSales DEFAULT (0),
    IsDefaultPurchase bit               NOT NULL CONSTRAINT DF_ItemUom_IsDefaultPurchase DEFAULT (0),
    IsDefaultPos      bit               NOT NULL CONSTRAINT DF_ItemUom_IsDefaultPos DEFAULT (0),
    CONSTRAINT UQ_ItemUom_Uom UNIQUE (ItemId, UomId),
    CONSTRAINT CK_ItemUom_Factor CHECK (FactorToBase > 0)
);
GO

-- Codurile de bare (EAN-13, EAN-8, GTIN-14 sau intern), optional legate de o UM: scanarea unui bax aduce direct 6 buc.
CREATE TABLE core.ItemBarcode (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemBarcode PRIMARY KEY,
    ItemId      int               NOT NULL CONSTRAINT FK_ItemBarcode_Item REFERENCES core.Item (Id),
    Barcode     varchar(50)       NOT NULL,
    BarcodeType tinyint           NOT NULL,
    UomId       int               NULL CONSTRAINT FK_ItemBarcode_Uom REFERENCES core.UnitOfMeasure (Id),
    IsActive    bit               NOT NULL CONSTRAINT DF_ItemBarcode_IsActive DEFAULT (1),
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_ItemBarcode_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT UQ_ItemBarcode_Barcode UNIQUE (Barcode),
    CONSTRAINT CK_ItemBarcode_Type CHECK (BarcodeType BETWEEN 1 AND 4)
);
CREATE INDEX IX_ItemBarcode_ItemId ON core.ItemBarcode (ItemId);
GO

-- Listele trimise la salvare (TVP).
CREATE TYPE core.VatRateList AS TABLE (
    Id         int           NULL,
    Code       nvarchar(10)  NOT NULL,
    Name       nvarchar(100) NOT NULL,
    [Percent]  decimal(5,2)  NOT NULL,
    Category   varchar(2)    NOT NULL,
    ValidFrom  date          NOT NULL,
    ValidTo    date          NULL,
    RowVersion binary(8)     NULL
);
GO
CREATE TYPE core.UnitOfMeasureList AS TABLE (
    Id         int           NULL,
    Code       nvarchar(10)  NOT NULL,
    Name       nvarchar(100) NOT NULL,
    UneceCode  varchar(3)    NOT NULL,
    IsActive   bit           NOT NULL,
    RowVersion binary(8)     NULL
);
GO
CREATE TYPE core.UomLineList AS TABLE (
    UomId             int           NOT NULL,
    FactorToBase      decimal(19,6) NOT NULL,
    IsBase            bit           NOT NULL,
    IsDefaultSales    bit           NOT NULL,
    IsDefaultPurchase bit           NOT NULL,
    IsDefaultPos      bit           NOT NULL,
    SortOrder         int           NOT NULL
);
GO
