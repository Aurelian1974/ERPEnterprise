-- Dimensiunile (02.01, 3.5; macheta 07k; decizia lui Aurelian din 21.11.2026): tipuri fixe (centre de cost, proiecte, gestiuni,
-- santiere) si valorile lor, pe firma. Valorile au parinte (rapoarte pe niveluri, acelasi tip si aceeasi firma), responsabil si
-- valabilitate. Codul e unic pe firma si tip, fara diferenta de majuscule (SQL-24). O valoare folosita nu se sterge, se dezactiveaza.
CREATE TABLE core.DimensionType (
    Code      varchar(20)   NOT NULL CONSTRAINT PK_DimensionType PRIMARY KEY,
    Name      nvarchar(100) NOT NULL,
    SortOrder int           NOT NULL
);
GO

CREATE TABLE core.DimensionValue (
    Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DimensionValue PRIMARY KEY,
    CompanyId         int               NOT NULL CONSTRAINT FK_DimensionValue_Company REFERENCES core.Company (Id),
    DimensionType     varchar(20)       NOT NULL CONSTRAINT FK_DimensionValue_Type REFERENCES core.DimensionType (Code),
    Code              nvarchar(30)      NOT NULL,
    Name              nvarchar(200)     NOT NULL,
    ParentId          int               NULL,
    ResponsibleUserId int               NULL CONSTRAINT FK_DimensionValue_Responsible REFERENCES core.[User] (Id),
    ValidFrom         date              NULL,
    ValidTo           date              NULL,
    IsActive          bit               NOT NULL CONSTRAINT DF_DimensionValue_IsActive DEFAULT (1),
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_DimensionValue_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CodeKey           AS UPPER(Code) PERSISTED,
    -- Parintele e in aceeasi firma si de acelasi tip (cheia compusa de mai jos).
    CONSTRAINT UQ_DimensionValue_Id_Company_Type UNIQUE (Id, CompanyId, DimensionType),
    CONSTRAINT FK_DimensionValue_Parent FOREIGN KEY (ParentId, CompanyId, DimensionType)
        REFERENCES core.DimensionValue (Id, CompanyId, DimensionType),
    CONSTRAINT CK_DimensionValue_NotOwnParent CHECK (ParentId IS NULL OR ParentId <> Id),
    CONSTRAINT CK_DimensionValue_Validity CHECK (ValidTo IS NULL OR ValidFrom IS NULL OR ValidTo >= ValidFrom)
);
GO

CREATE UNIQUE INDEX UX_DimensionValue_Company_Type_CodeKey ON core.DimensionValue (CompanyId, DimensionType, CodeKey);
CREATE INDEX IX_DimensionValue_ParentId ON core.DimensionValue (ParentId) WHERE ParentId IS NOT NULL;
CREATE INDEX IX_DimensionValue_Type_Company ON core.DimensionValue (DimensionType, CompanyId) INCLUDE (Code, Name, IsActive);
GO
