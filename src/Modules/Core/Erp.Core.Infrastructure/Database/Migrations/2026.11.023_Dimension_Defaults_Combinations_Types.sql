-- Dimensiuni: tipuri definite de client, valori implicite cu reguli si combinatii blocate (02.01, 3.6 punctele 14 si 15; decizia lui
-- Aurelian din 21.11.2026).
-- 1. core.DimensionType primeste IsSystem (cele patru livrate, care nu se dezactiveaza) si IsActive; clientul adauga tipuri noi.
-- 2. core.DefaultDimension: pe firma, pentru un partener (SourceType 1), un articol (2) sau o locatie (3), fiecare tip are o regula
--    ValuePosting: 1 optionala (valoare propusa), 2 obligatorie (o valoare trebuie aleasa), 3 fixa (doar valoarea data).
-- 3. core.DimensionCombination: doua tipuri care nu se folosesc impreuna (fara valori) sau o pereche de valori blocata.
ALTER TABLE core.DimensionType ADD
    IsSystem   bit        NOT NULL CONSTRAINT DF_DimensionType_IsSystem DEFAULT (0),
    IsActive   bit        NOT NULL CONSTRAINT DF_DimensionType_IsActive DEFAULT (1),
    RowVersion rowversion NOT NULL;
GO

UPDATE core.DimensionType SET IsSystem = 1 WHERE Code IN ('COST_CENTER', 'PROJECT', 'WAREHOUSE', 'SITE');
GO

CREATE TABLE core.DefaultDimension (
    Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DefaultDimension PRIMARY KEY,
    CompanyId        int               NOT NULL CONSTRAINT FK_DefaultDimension_Company REFERENCES core.Company (Id),
    SourceType       tinyint           NOT NULL CONSTRAINT CK_DefaultDimension_SourceType CHECK (SourceType IN (1, 2, 3)),
    SourceId         int               NOT NULL,
    DimensionType    varchar(20)       NOT NULL CONSTRAINT FK_DefaultDimension_Type REFERENCES core.DimensionType (Code),
    DimensionValueId int               NULL,
    ValuePosting     tinyint           NOT NULL CONSTRAINT CK_DefaultDimension_ValuePosting CHECK (ValuePosting IN (1, 2, 3)),
    ModifiedAt       datetime2(3)      NOT NULL,
    ModifiedBy       int               NOT NULL CONSTRAINT FK_DefaultDimension_User REFERENCES core.[User] (Id),
    CONSTRAINT FK_DefaultDimension_Value FOREIGN KEY (DimensionValueId, CompanyId, DimensionType)
        REFERENCES core.DimensionValue (Id, CompanyId, DimensionType),
    CONSTRAINT CK_DefaultDimension_Fixed CHECK (ValuePosting <> 3 OR DimensionValueId IS NOT NULL),
    CONSTRAINT UQ_DefaultDimension UNIQUE (CompanyId, SourceType, SourceId, DimensionType)
);
GO

CREATE TYPE core.DefaultDimensionList AS TABLE (
    DimensionType    varchar(20) NOT NULL PRIMARY KEY,
    DimensionValueId int         NULL,
    ValuePosting     tinyint     NOT NULL
);
GO

CREATE TABLE core.DimensionCombination (
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DimensionCombination PRIMARY KEY,
    CompanyId  int               NOT NULL CONSTRAINT FK_DimensionCombination_Company REFERENCES core.Company (Id),
    TypeA      varchar(20)       NOT NULL CONSTRAINT FK_DimensionCombination_TypeA REFERENCES core.DimensionType (Code),
    TypeB      varchar(20)       NOT NULL CONSTRAINT FK_DimensionCombination_TypeB REFERENCES core.DimensionType (Code),
    ValueAId   int               NULL,
    ValueBId   int               NULL,
    Note       nvarchar(200)     NULL,
    CreatedAt  datetime2(3)      NOT NULL CONSTRAINT DF_DimensionCombination_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int               NOT NULL,
    CONSTRAINT FK_DimensionCombination_ValueA FOREIGN KEY (ValueAId, CompanyId, TypeA) REFERENCES core.DimensionValue (Id, CompanyId, DimensionType),
    CONSTRAINT FK_DimensionCombination_ValueB FOREIGN KEY (ValueBId, CompanyId, TypeB) REFERENCES core.DimensionValue (Id, CompanyId, DimensionType),
    CONSTRAINT CK_DimensionCombination_Order CHECK (TypeA < TypeB),
    CONSTRAINT CK_DimensionCombination_Values CHECK ((ValueAId IS NULL AND ValueBId IS NULL) OR (ValueAId IS NOT NULL AND ValueBId IS NOT NULL))
);
CREATE UNIQUE INDEX UX_DimensionCombination ON core.DimensionCombination (CompanyId, TypeA, TypeB, ValueAId, ValueBId);
GO
