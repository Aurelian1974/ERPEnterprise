-- Stocuri S4.3 (02.05, S4 3.4, 7): seriile (o serie = o bucata, unica pe varianta in toata baza) si identificatorii configurabili ai bucatii
-- (IMEI, MAC, VIN...): tipurile cu masca si verificarea speciala, regulile pe model sau pe grupa de articole, valorile cu unicitate pe tip si
-- istoricul corectarilor. Miscarile seriilor au SerialId (coloana exista din S2), cu cantitatea ±1.

CREATE TABLE inv.SerialIdentifierType (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SerialIdentifierType PRIMARY KEY,
    Code       nvarchar(20)   NOT NULL,
    CodeKey    AS UPPER(Code) PERSISTED,
    Name       nvarchar(100)  NOT NULL,
    Mask       nvarchar(200)  NULL,      -- expresie regulata, verificata in C# (SerialIdentifierRules)
    CheckKind  tinyint        NOT NULL CONSTRAINT DF_SerialIdentifierType_CheckKind DEFAULT (0),   -- 0 fara, 1 cifra de control Luhn (IMEI), 2 VIN
    IsSystem   bit            NOT NULL CONSTRAINT DF_SerialIdentifierType_IsSystem DEFAULT (0),
    IsActive   bit            NOT NULL CONSTRAINT DF_SerialIdentifierType_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)   NOT NULL CONSTRAINT DF_SerialIdentifierType_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int            NOT NULL,
    ModifiedAt datetime2(3)   NULL,
    ModifiedBy int            NULL,
    RowVersion rowversion     NOT NULL,
    CONSTRAINT CK_SerialIdentifierType_CheckKind CHECK (CheckKind IN (0, 1, 2))
);
CREATE UNIQUE INDEX UX_SerialIdentifierType_CodeKey ON inv.SerialIdentifierType (CodeKey);
GO

CREATE TABLE inv.ItemSerialIdentifierRule (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemSerialIdentifierRule PRIMARY KEY,
    ItemGroupId       int            NULL CONSTRAINT FK_ItemSerialIdentifierRule_ItemGroup REFERENCES core.ItemGroup (Id),
    ItemId            int            NULL CONSTRAINT FK_ItemSerialIdentifierRule_Item REFERENCES core.Item (Id),
    ItemGroupKey      AS ISNULL(ItemGroupId, 0) PERSISTED,
    ItemKey           AS ISNULL(ItemId, 0) PERSISTED,
    IdentifierTypeId  int            NOT NULL CONSTRAINT FK_ItemSerialIdentifierRule_Type REFERENCES inv.SerialIdentifierType (Id),
    SortOrder         int            NOT NULL CONSTRAINT DF_ItemSerialIdentifierRule_SortOrder DEFAULT (100),
    RequiredAtReceipt bit            NOT NULL CONSTRAINT DF_ItemSerialIdentifierRule_RequiredAtReceipt DEFAULT (0),
    RequiredAtIssue   bit            NOT NULL CONSTRAINT DF_ItemSerialIdentifierRule_RequiredAtIssue DEFAULT (0),
    IsUnique          bit            NOT NULL CONSTRAINT DF_ItemSerialIdentifierRule_IsUnique DEFAULT (0),
    OnDocuments       bit            NOT NULL CONSTRAINT DF_ItemSerialIdentifierRule_OnDocuments DEFAULT (0),
    CreatedAt         datetime2(3)   NOT NULL CONSTRAINT DF_ItemSerialIdentifierRule_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int            NOT NULL,
    ModifiedAt        datetime2(3)   NULL,
    ModifiedBy        int            NULL,
    RowVersion        rowversion     NOT NULL,
    CONSTRAINT UX_ItemSerialIdentifierRule UNIQUE (ItemGroupKey, ItemKey, IdentifierTypeId),
    CONSTRAINT CK_ItemSerialIdentifierRule_Target CHECK ((ItemGroupId IS NULL AND ItemId IS NOT NULL) OR (ItemGroupId IS NOT NULL AND ItemId IS NULL))
);
CREATE INDEX IX_ItemSerialIdentifierRule_ItemId ON inv.ItemSerialIdentifierRule (ItemId) WHERE ItemId IS NOT NULL;
CREATE INDEX IX_ItemSerialIdentifierRule_ItemGroupId ON inv.ItemSerialIdentifierRule (ItemGroupId) WHERE ItemGroupId IS NOT NULL;
GO

CREATE TABLE inv.Serial (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Serial PRIMARY KEY,
    CompanyId         int            NOT NULL CONSTRAINT FK_Serial_Company REFERENCES core.Company (Id),
    ItemVariantId     int            NOT NULL CONSTRAINT FK_Serial_ItemVariant REFERENCES core.ItemVariant (Id),
    SerialNo          nvarchar(60)   NOT NULL,     -- comparat exact (collation-ul bazei)
    SerialNoSearch    AS CAST(SerialNo AS nvarchar(60)) COLLATE Latin1_General_100_CI_AI_SC_UTF8 PERSISTED,
    LotId             int            NULL CONSTRAINT FK_Serial_Lot REFERENCES inv.Lot (Id),
    Status            tinyint        NOT NULL CONSTRAINT DF_Serial_Status DEFAULT (1),   -- 1 In stoc, 2 In tranzit, 3 Vanduta, 4 Returnata, 5 Casata, 6 Blocata
    WarehouseId       int            NULL CONSTRAINT FK_Serial_Warehouse REFERENCES inv.Warehouse (Id),
    DepotId           int            NULL CONSTRAINT FK_Serial_Depot REFERENCES inv.Depot (Id),
    IsCapturedAtIssue bit            NOT NULL CONSTRAINT DF_Serial_IsCapturedAtIssue DEFAULT (0),
    SoldAt            date           NULL,
    SoldToPartnerId   int            NULL CONSTRAINT FK_Serial_SoldTo REFERENCES core.Partner (Id),
    WarrantyUntil     date           NULL,
    CreatedAt         datetime2(3)   NOT NULL CONSTRAINT DF_Serial_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int            NOT NULL,
    ModifiedAt        datetime2(3)   NULL,
    ModifiedBy        int            NULL,
    RowVersion        rowversion     NOT NULL,
    CONSTRAINT UX_Serial_VariantSerial UNIQUE (ItemVariantId, SerialNo),
    CONSTRAINT CK_Serial_Status CHECK (Status BETWEEN 1 AND 6),
    CONSTRAINT CK_Serial_Place CHECK (Status IN (3, 5) OR WarehouseId IS NOT NULL)
);
CREATE INDEX IX_Serial_Stock ON inv.Serial (CompanyId, WarehouseId, Status) INCLUDE (ItemVariantId);
CREATE INDEX IX_Serial_Search ON inv.Serial (SerialNoSearch) INCLUDE (ItemVariantId, CompanyId);
GO

CREATE TABLE inv.SerialIdentifier (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SerialIdentifier PRIMARY KEY,
    SerialId         int            NOT NULL CONSTRAINT FK_SerialIdentifier_Serial REFERENCES inv.Serial (Id),
    IdentifierTypeId int            NOT NULL CONSTRAINT FK_SerialIdentifier_Type REFERENCES inv.SerialIdentifierType (Id),
    Value            nvarchar(100)  NOT NULL,
    ValueSearch      AS CAST(Value AS nvarchar(100)) COLLATE Latin1_General_100_CI_AI_SC_UTF8 PERSISTED,
    IsUnique         bit            NOT NULL,
    CreatedAt        datetime2(3)   NOT NULL CONSTRAINT DF_SerialIdentifier_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy        int            NOT NULL,
    CONSTRAINT UX_SerialIdentifier_SerialType UNIQUE (SerialId, IdentifierTypeId)
);
CREATE UNIQUE INDEX UX_SerialIdentifier_Value ON inv.SerialIdentifier (IdentifierTypeId, Value) WHERE IsUnique = 1;
CREATE INDEX IX_SerialIdentifier_Search ON inv.SerialIdentifier (ValueSearch) INCLUDE (SerialId);
GO

-- Doar de adaugare (abatere SQL-11).
CREATE TABLE inv.SerialIdentifierHistory (
    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SerialIdentifierHistory PRIMARY KEY,
    SerialId         int            NOT NULL CONSTRAINT FK_SerialIdentifierHistory_Serial REFERENCES inv.Serial (Id),
    IdentifierTypeId int            NOT NULL CONSTRAINT FK_SerialIdentifierHistory_Type REFERENCES inv.SerialIdentifierType (Id),
    OldValue         nvarchar(100)  NULL,
    NewValue         nvarchar(100)  NULL,
    Reason           nvarchar(400)  NOT NULL,
    ChangedAt        datetime2(3)   NOT NULL CONSTRAINT DF_SerialIdentifierHistory_ChangedAt DEFAULT (sysutcdatetime()),
    ChangedBy        int            NOT NULL
);
CREATE INDEX IX_SerialIdentifierHistory_Serial ON inv.SerialIdentifierHistory (SerialId);
GO

CREATE INDEX IX_StockMovement_SerialId ON inv.StockMovement (SerialId) WHERE SerialId IS NOT NULL;
ALTER TABLE inv.StockMovement ADD CONSTRAINT FK_StockMovement_Serial FOREIGN KEY (SerialId) REFERENCES inv.Serial (Id);
GO

CREATE TYPE inv.tt_SerialRequest AS TABLE (
    RowNo            int            NOT NULL PRIMARY KEY,
    MovementTypeCode varchar(30)    NOT NULL,
    WarehouseId      int            NOT NULL,
    DepotId          int            NULL,
    ItemVariantId    int            NOT NULL,
    QtyBase          decimal(19,6)  NOT NULL,
    SerialId         int            NULL,
    SerialNo         nvarchar(60)   NULL,
    ReversalOfId     bigint         NULL,
    IsCounter        bit            NOT NULL
);
GO

CREATE TYPE inv.tt_SerialIdentifierValue AS TABLE (
    RowNo    int            NOT NULL,
    TypeCode nvarchar(20)   NOT NULL,
    Value    nvarchar(100)  NOT NULL,
    PRIMARY KEY (RowNo, TypeCode)
);
GO
