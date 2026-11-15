CREATE OR ALTER PROCEDURE core.usp_CompanyGroup_Save
    @Id              int = NULL,
    @Code            nvarchar(20),
    @Name            nvarchar(200),
    @ParentGroupId   int = NULL,
    @ParentCompanyId int = NULL,
    @IsActive        bit,
    @RowVersion      binary(8) = NULL,
    @UserId          int
AS
BEGIN
    -- Grup din structura organizatorica (02.01, 3.1): oricate niveluri, fara cicluri; firma-mama trebuie sa fie in grup
    -- sau intr-un subgrup; un grup cu copii activi nu se dezactiveaza. Ruleaza in tranzactia comenzii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ParentGroupId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.CompanyGroup WHERE Id = @ParentGroupId AND IsActive = 1)
        THROW 50021, N'Grupul părinte nu există sau este inactiv.', 1;

    IF @Id IS NOT NULL
    BEGIN
        -- Parintele nu poate fi grupul insusi sau un descendent al lui.
        WITH Descendants AS (
            SELECT Id FROM core.CompanyGroup WHERE Id = @Id
            UNION ALL
            SELECT g.Id FROM core.CompanyGroup AS g JOIN Descendants AS d ON g.ParentGroupId = d.Id
        )
        SELECT @ParentGroupId = @ParentGroupId
        WHERE NOT EXISTS (SELECT 1 FROM Descendants WHERE Id = @ParentGroupId);
        IF @@ROWCOUNT = 0
            THROW 50021, N'Un grup nu poate fi mutat sub el însuși sau sub un subgrup al lui.', 1;
    END;

    IF @ParentCompanyId IS NOT NULL
    BEGIN
        IF @Id IS NULL
            THROW 50021, N'Firma-mamă se alege după ce firmele sunt adăugate în grup.', 1;

        WITH Subtree AS (
            SELECT Id FROM core.CompanyGroup WHERE Id = @Id
            UNION ALL
            SELECT g.Id FROM core.CompanyGroup AS g JOIN Subtree AS s ON g.ParentGroupId = s.Id
        )
        SELECT @ParentCompanyId = @ParentCompanyId
        FROM core.Company AS c
        WHERE c.Id = @ParentCompanyId AND c.GroupId IN (SELECT Id FROM Subtree);
        IF @@ROWCOUNT = 0
            THROW 50021, N'Firma-mamă trebuie să aparțină grupului sau unui subgrup al lui.', 1;
    END;

    IF EXISTS (SELECT 1 FROM core.CompanyGroup WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul grupului există deja.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.CompanyGroup (Code, Name, ParentGroupId, ParentCompanyId, IsActive, CreatedBy)
        VALUES (@Code, @Name, @ParentGroupId, NULL, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND (
            EXISTS (SELECT 1 FROM core.CompanyGroup WHERE ParentGroupId = @Id AND IsActive = 1)
            OR EXISTS (SELECT 1 FROM core.Company WHERE GroupId = @Id AND IsActive = 1))
            THROW 50020, N'Grupul are subgrupuri sau firme active; dezactivați-le întâi.', 1;

        UPDATE core.CompanyGroup
        SET Code = @Code, Name = @Name, ParentGroupId = @ParentGroupId, ParentCompanyId = @ParentCompanyId,
            IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.CompanyGroup WHERE Id = @Id)
                THROW 50027, N'Grupul nu există.', 1;
            THROW 50006, N'Grupul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.CompanyGroup WHERE Id = @Id;
END;
