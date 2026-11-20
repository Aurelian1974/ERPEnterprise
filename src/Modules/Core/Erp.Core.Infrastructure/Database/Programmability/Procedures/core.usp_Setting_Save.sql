CREATE OR ALTER PROCEDURE core.usp_Setting_Save
    @Scope      varchar(20),
    @ScopeId    int = NULL,
    @Key        varchar(100),
    @Value      nvarchar(max) = NULL,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Creeaza sau modifica o setare (02.01, 3.5), cu concurenta optimista (SQL-18): o setare existenta cere versiunea vazuta
    -- de utilizator; una noua se creeaza fara versiune. Intoarce versiunea noua.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id int;
    SELECT @Id = Id FROM core.Setting WITH (UPDLOCK, HOLDLOCK)
    WHERE Scope = @Scope AND ScopeId IS NOT DISTINCT FROM @ScopeId AND [Key] = @Key;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Setting (Scope, ScopeId, [Key], Value, CreatedBy)
        VALUES (@Scope, @ScopeId, @Key, @Value, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.Setting
        SET Value = @Value, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 50006, N'Setarea a fost modificată de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT RowVersion FROM core.Setting WHERE Id = @Id;
END;
