-- Licenta sta in baza (08, 4.1, decizia D18): LicenseFile contine octetii fisierului .lic, sursa de adevar; celelalte coloane
-- sunt proiectia lui pentru afisare si interogari. ValidUntil NULL = nelimitat (08, 4.4); UpdatesUntil = dreptul la actualizari
-- (08, 4.5). Semnatura e in fisier, deci coloana Signature dispare. Scriptul 2026.11.001 nu se modifica (MIG-02).
ALTER TABLE core.InstallationLicense ADD
    LicenseFile  varbinary(max) NULL,
    UpdatesUntil date           NULL;
GO

ALTER TABLE core.InstallationLicense DROP CONSTRAINT CK_InstallationLicense_Validity;
GO

ALTER TABLE core.InstallationLicense ALTER COLUMN ValidUntil date NULL;
GO

ALTER TABLE core.InstallationLicense ADD
    CONSTRAINT CK_InstallationLicense_Validity CHECK (ValidUntil IS NULL OR ValidUntil >= IssuedDate),
    CONSTRAINT CK_InstallationLicense_UpdatesUntil CHECK (UpdatesUntil IS NULL OR UpdatesUntil >= IssuedDate);
GO

ALTER TABLE core.InstallationLicense DROP COLUMN Signature;
GO

-- Setarile (02.01, 3.5 si 7): chei definite in cod, pe instalare (ScopeId NULL), firma sau utilizator.
-- Key e cuvant rezervat in T-SQL, deci coloana se scrie mereu [Key].
CREATE TABLE core.Setting (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Setting PRIMARY KEY,
    Scope       varchar(20)       NOT NULL,
    ScopeId     int               NULL,
    [Key]       varchar(100)      NOT NULL,
    Value       nvarchar(max)     NULL,
    CreatedAt   datetime2(3)      NOT NULL CONSTRAINT DF_Setting_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy   int               NOT NULL,
    ModifiedAt  datetime2(3)      NULL,
    ModifiedBy  int               NULL,
    RowVersion  rowversion        NOT NULL,
    CONSTRAINT CK_Setting_Scope CHECK (Scope IN ('Installation', 'Company', 'User')),
    CONSTRAINT CK_Setting_ScopeId CHECK ((Scope = 'Installation' AND ScopeId IS NULL) OR (Scope <> 'Installation' AND ScopeId IS NOT NULL)),
    CONSTRAINT UQ_Setting_Scope_Key UNIQUE (Scope, ScopeId, [Key])
);
