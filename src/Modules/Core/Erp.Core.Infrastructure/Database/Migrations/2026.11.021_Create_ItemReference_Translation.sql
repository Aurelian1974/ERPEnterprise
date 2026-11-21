-- Codurile articolelor la clienti si furnizori si traducerile denumirilor (02.01, 3.6 punctele 11 si 12; decizia lui Aurelian din
-- 21.11.2026). ReferenceType: 1 client, 2 furnizor. Codul e unic pe partener si tip, fara diferenta de majuscule (SQL-24); un articol
-- poate avea mai multe coduri la acelasi partener. Traducerile: denumirea articolului in alta limba decat romana (ISO 639-1), aleasa
-- pe documente dupa limba documentelor partenerului (Partner.DocumentLanguage, NULL = romana).
CREATE TABLE core.ItemPartnerReference (
    Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemPartnerReference PRIMARY KEY,
    ItemId        int               NOT NULL CONSTRAINT FK_ItemPartnerReference_Item REFERENCES core.Item (Id),
    PartnerId     int               NOT NULL CONSTRAINT FK_ItemPartnerReference_Partner REFERENCES core.Partner (Id),
    ReferenceType tinyint           NOT NULL CONSTRAINT CK_ItemPartnerReference_Type CHECK (ReferenceType IN (1, 2)),
    Code          nvarchar(50)      NOT NULL,
    CodeKey       AS UPPER(Code) PERSISTED,
    Description   nvarchar(250)     NULL,
    UomId         int               NULL CONSTRAINT FK_ItemPartnerReference_Uom REFERENCES core.UnitOfMeasure (Id),
    IsActive      bit               NOT NULL CONSTRAINT DF_ItemPartnerReference_IsActive DEFAULT (1),
    CreatedAt     datetime2(3)      NOT NULL CONSTRAINT DF_ItemPartnerReference_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy     int               NOT NULL,
    ModifiedAt    datetime2(3)      NULL,
    ModifiedBy    int               NULL,
    RowVersion    rowversion        NOT NULL,
    CONSTRAINT UQ_ItemPartnerReference_Code UNIQUE (PartnerId, ReferenceType, CodeKey)
);
CREATE INDEX IX_ItemPartnerReference_ItemId ON core.ItemPartnerReference (ItemId);
GO

CREATE TABLE core.ItemTranslation (
    ItemId       int            NOT NULL CONSTRAINT FK_ItemTranslation_Item REFERENCES core.Item (Id),
    LanguageCode char(2)        NOT NULL CONSTRAINT CK_ItemTranslation_Language CHECK (LanguageCode LIKE '[a-z][a-z]' AND LanguageCode <> 'ro'),
    Name         nvarchar(250)  NOT NULL,
    ModifiedAt   datetime2(3)   NOT NULL,
    ModifiedBy   int            NOT NULL,
    CONSTRAINT PK_ItemTranslation PRIMARY KEY (ItemId, LanguageCode)
);
GO

CREATE TYPE core.ItemTranslationList AS TABLE (
    LanguageCode char(2)       NOT NULL PRIMARY KEY,
    Name         nvarchar(250) NOT NULL
);
GO

ALTER TABLE core.Partner ADD DocumentLanguage char(2) NULL
    CONSTRAINT CK_Partner_DocumentLanguage CHECK (DocumentLanguage LIKE '[a-z][a-z]' AND DocumentLanguage <> 'ro');
GO
