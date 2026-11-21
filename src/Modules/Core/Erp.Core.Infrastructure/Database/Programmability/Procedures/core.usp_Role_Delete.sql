CREATE OR ALTER PROCEDURE core.usp_Role_Delete
    @Id         int,
    @RowVersion binary(8)
AS
BEGIN
    -- Stergerea unui rol al clientului (02.01, 8): rolurile de sistem nu se sterg (50035), iar un rol atribuit unor
    -- utilizatori nu se sterge (50010). Permisiunile rolului se sterg odata cu el.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @IsSystem bit;
    SELECT @IsSystem = IsSystem FROM core.Role WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;

    IF @IsSystem IS NULL
        THROW 50027, N'Rolul nu există.', 1;
    IF @IsSystem = 1
        THROW 50035, N'Rolurile de sistem nu se șterg; se pot copia.', 1;
    IF EXISTS (SELECT 1 FROM core.UserRoleAssignment WITH (UPDLOCK, HOLDLOCK) WHERE RoleId = @Id)
        THROW 50010, N'Rolul e atribuit unor utilizatori; scoateți-l întâi din fișele lor.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Role WHERE Id = @Id AND RowVersion = @RowVersion)
        THROW 50006, N'Rolul a fost modificat de alt utilizator; reîncărcați datele.', 1;

    DELETE FROM core.RolePermission WHERE RoleId = @Id;
    DELETE FROM core.Role WHERE Id = @Id;
END;
