CREATE OR ALTER PROCEDURE core.usp_Role_Save
    @Id          int = NULL,
    @Code        nvarchar(50),
    @Name        nvarchar(200),
    @Description nvarchar(500) = NULL,
    @Permissions core.PermissionIdList READONLY,
    @RowVersion  binary(8) = NULL,
    @UserId      int
AS
BEGIN
    -- Rolul si permisiunile lui (02.01, 8; macheta 07e): lista primita inlocuieste permisiunile rolului.
    -- Rolul de sistem ADMIN are toate drepturile si nu se modifica (50035); codul unui rol de sistem nu se schimba (50035).
    -- Codul e unic fara diferenta de majuscule (50018).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM @Permissions AS x WHERE NOT EXISTS (SELECT 1 FROM core.Permission AS p WHERE p.Id = x.PermissionId))
        THROW 50021, N'Un drept ales nu există.', 1;

    IF EXISTS (SELECT 1 FROM core.Role WITH (UPDLOCK, HOLDLOCK) WHERE UPPER(Code) = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul rolului există deja.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Role (Code, Name, Description, IsSystem, CreatedBy) VALUES (@Code, @Name, @Description, 0, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @OldCode nvarchar(50), @IsSystem bit;
        SELECT @OldCode = Code, @IsSystem = IsSystem FROM core.Role WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;

        IF @OldCode IS NULL
            THROW 50027, N'Rolul nu există.', 1;
        IF @IsSystem = 1 AND @OldCode = N'ADMIN'
            THROW 50035, N'Rolul Administrator are toate drepturile și nu se modifică.', 1;
        IF @IsSystem = 1 AND @OldCode <> @Code
            THROW 50035, N'Codul unui rol de sistem nu se schimbă.', 1;

        UPDATE core.Role
        SET Code = @Code, Name = @Name, Description = @Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 50006, N'Rolul a fost modificat de alt utilizator; reîncărcați datele.', 1;

        DELETE rp
        FROM core.RolePermission AS rp
        WHERE rp.RoleId = @Id AND NOT EXISTS (SELECT 1 FROM @Permissions AS x WHERE x.PermissionId = rp.PermissionId);
    END;

    INSERT INTO core.RolePermission (RoleId, PermissionId)
    SELECT @Id, x.PermissionId
    FROM @Permissions AS x
    WHERE NOT EXISTS (SELECT 1 FROM core.RolePermission AS rp WHERE rp.RoleId = @Id AND rp.PermissionId = x.PermissionId);

    SELECT Id, RowVersion FROM core.Role WHERE Id = @Id;
END;
