CREATE OR ALTER PROCEDURE core.usp_ItemGroup_Save
    @Id         int = NULL,
    @Code       nvarchar(20),
    @Name       nvarchar(100),
    @ParentId   int = NULL,
    @SortOrder  int,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Grupa de articole (arbore, 02.01, 3.3): cod unic fara diferenta de majuscule; parintele nu poate fi grupa insasi sau un
    -- descendent al ei; o grupa cu subgrupe active sau articole active nu se dezactiveaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM core.ItemGroup WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul grupei există deja.', 1;

    IF @ParentId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.ItemGroup WHERE Id = @ParentId AND IsActive = 1)
        THROW 50021, N'Grupa părinte nu există sau este inactivă.', 1;

    IF @Id IS NOT NULL AND @ParentId IS NOT NULL
    BEGIN
        WITH Ancestors AS (
            SELECT Id, ParentId FROM core.ItemGroup WHERE Id = @ParentId
            UNION ALL
            SELECT g.Id, g.ParentId FROM core.ItemGroup AS g JOIN Ancestors AS a ON g.Id = a.ParentId
        )
        SELECT TOP (1) @ParentId = @ParentId FROM Ancestors WHERE Id = @Id;
        IF @@ROWCOUNT > 0
            THROW 50021, N'Grupa nu poate fi pusă sub ea însăși sau sub o subgrupă a ei.', 1;
    END;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.ItemGroup (Code, Name, ParentId, SortOrder, IsActive, CreatedBy)
        VALUES (@Code, @Name, @ParentId, @SortOrder, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.ItemGroup WHERE ParentId = @Id AND IsActive = 1)
            THROW 50020, N'Grupa are subgrupe active; dezactivați-le întâi.', 1;

        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.Item WHERE ItemGroupId = @Id AND IsActive = 1)
            THROW 50010, N'Grupa are articole active; mutați-le întâi în altă grupă.', 1;

        UPDATE core.ItemGroup
        SET Code = @Code, Name = @Name, ParentId = @ParentId, SortOrder = @SortOrder, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.ItemGroup WHERE Id = @Id)
                THROW 50027, N'Grupa nu există.', 1;
            THROW 50006, N'Grupa a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.ItemGroup WHERE Id = @Id;
END;
