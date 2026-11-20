-- Bootstrap 003 (INSTALARE_ACTUALIZARE.md 5.9): jurnalul versiunilor scripturilor, pereche cu core.SchemaVersion (DbUp).
-- Un rand pentru fiecare script aplicat al carui continut s-a schimbat fata de randul anterior: migrare, obiect programabil
-- sau date livrate, cu versiunea din manifestul kitului (calculata din istoricul Git la construire), amprenta continutului,
-- versiunea produsului si cine/cand a aplicat. Ultimul rand al fiecarui script = ce e instalat acum (core.v_ScriptVersionCurrent).
--
-- Sta in bootstrap, nu intr-o migrare, din acelasi motiv ca schemele (5.6): trebuie sa existe inaintea primei migrari,
-- pentru ca si migratiile se noteaza aici. Idempotent. Structura nu se mai schimba aici: orice coloana noua vine printr-o
-- migrare a Nucleului, ca orice tabela.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'core.ScriptVersion', N'U') IS NULL
BEGIN
    CREATE TABLE core.ScriptVersion (
        Id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScriptVersion PRIMARY KEY,
        ScriptName     nvarchar(300)        NOT NULL,   -- ca in jurnalul DbUp: <schema>/<fisier>, ex. core/Procedures/core.usp_Company_Save.sql
        ScriptKind     tinyint              NOT NULL,   -- 1 migrare, 2 obiect programabil, 3 date livrate (enum ScriptKind)
        Version        int                  NOT NULL,   -- din manifest; 0 = necunoscuta (kit de test fara istoric Git)
        ContentHash    char(64)             NOT NULL,   -- SHA-256 hex, continut cu sfarsiturile si spatiile de capat de rand normalizate
        ProductVersion varchar(30)          NOT NULL,   -- versiunea kitului sau 'dev'
        AppliedAt      datetime2(3)         NOT NULL CONSTRAINT DF_ScriptVersion_AppliedAt DEFAULT (SYSUTCDATETIME()),
        AppliedBy      nvarchar(128)        NOT NULL CONSTRAINT DF_ScriptVersion_AppliedBy DEFAULT (ORIGINAL_LOGIN()),
        CONSTRAINT CK_ScriptVersion_ScriptKind CHECK (ScriptKind IN (1, 2, 3)),
        CONSTRAINT CK_ScriptVersion_Version CHECK (Version >= 0)
    );

    -- Ultimul rand al unui script (scrierea compara amprenta cu el; view-ul il afiseaza).
    CREATE INDEX IX_ScriptVersion_ScriptName_Id ON core.ScriptVersion (ScriptName, Id DESC) INCLUDE (ContentHash);
END;
