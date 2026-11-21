-- Variantele articolelor (02.01, 3.6 punctul 13; decizia lui Aurelian din 21.11.2026): atribute comune instalarii (marime, culoare,
-- aroma) cu valorile lor; un articol foloseste unele atribute, iar variantele lui sunt combinatii de valori, fiecare cu cod (sufix
-- unic pe articol), denumire si coduri de bare proprii. Stocul si pretul pe varianta tin de Stocuri si Vanzari. Atributele si
-- valorile folosite nu se sterg, se dezactiveaza.
CREATE TABLE core.ItemAttribute (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemAttribute PRIMARY KEY,
    Code       nvarchar(20)      NOT NULL,
    CodeKey    AS UPPER(Code) PERSISTED,
    Name       nvarchar(100)     NOT NULL,
    SortOrder  int               NOT NULL,
    IsActive   bit               NOT NULL CONSTRAINT DF_ItemAttribute_IsActive DEFAULT (1),
    RowVersion rowversion        NOT NULL
);
CREATE UNIQUE INDEX UX_ItemAttribute_CodeKey ON core.ItemAttribute (CodeKey);
GO

CREATE TABLE core.ItemAttributeValue (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemAttributeValue PRIMARY KEY,
    AttributeId int               NOT NULL CONSTRAINT FK_ItemAttributeValue_Attribute REFERENCES core.ItemAttribute (Id),
    Code        nvarchar(20)      NOT NULL,
    CodeKey     AS UPPER(Code) PERSISTED,
    Name        nvarchar(100)     NOT NULL,
    SortOrder   int               NOT NULL,
    IsActive    bit               NOT NULL CONSTRAINT DF_ItemAttributeValue_IsActive DEFAULT (1),
    CONSTRAINT UQ_ItemAttributeValue_Attribute UNIQUE (Id, AttributeId)
);
CREATE UNIQUE INDEX UX_ItemAttributeValue_Code ON core.ItemAttributeValue (AttributeId, CodeKey);
GO

CREATE TYPE core.ItemAttributeValueList AS TABLE (
    Id        int           NULL,
    Code      nvarchar(20)  NOT NULL,
    Name      nvarchar(100) NOT NULL,
    SortOrder int           NOT NULL,
    IsActive  bit           NOT NULL
);
GO

CREATE TABLE core.ItemVariant (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemVariant PRIMARY KEY,
    ItemId     int               NOT NULL CONSTRAINT FK_ItemVariant_Item REFERENCES core.Item (Id),
    Code       nvarchar(50)      NOT NULL,
    CodeKey    AS UPPER(Code) PERSISTED,
    Name       nvarchar(250)     NOT NULL,
    IsActive   bit               NOT NULL CONSTRAINT DF_ItemVariant_IsActive DEFAULT (1),
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_ItemVariant_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    ModifiedAt datetime2(3)      NULL,
    ModifiedBy int               NULL,
    RowVersion rowversion        NOT NULL,
    CONSTRAINT UQ_ItemVariant_Item UNIQUE (Id, ItemId)
);
CREATE UNIQUE INDEX UX_ItemVariant_Code ON core.ItemVariant (ItemId, CodeKey);
GO

CREATE TABLE core.ItemVariantValue (
    VariantId   int NOT NULL CONSTRAINT FK_ItemVariantValue_Variant REFERENCES core.ItemVariant (Id),
    AttributeId int NOT NULL,
    ValueId     int NOT NULL,
    CONSTRAINT PK_ItemVariantValue PRIMARY KEY (VariantId, AttributeId),
    CONSTRAINT FK_ItemVariantValue_Value FOREIGN KEY (ValueId, AttributeId) REFERENCES core.ItemAttributeValue (Id, AttributeId)
);
GO

-- Combinatia de valori a unei variante, ca text ordonat („3,12”), unica pe articol: aceeasi combinatie nu se creeaza de doua ori.
ALTER TABLE core.ItemVariant ADD ValueKey varchar(200) NOT NULL CONSTRAINT DF_ItemVariant_ValueKey DEFAULT ('');
GO
CREATE UNIQUE INDEX UX_ItemVariant_ValueKey ON core.ItemVariant (ItemId, ValueKey) WHERE ValueKey <> '';
GO

ALTER TABLE core.ItemBarcode ADD VariantId int NULL;
ALTER TABLE core.ItemBarcode ADD CONSTRAINT FK_ItemBarcode_Variant FOREIGN KEY (VariantId, ItemId) REFERENCES core.ItemVariant (Id, ItemId);
GO
