CREATE OR ALTER PROCEDURE core.usp_AddressType_Save
    @Id         int = NULL,
    @Code       nvarchar(30),
    @Name       nvarchar(100),
    @SortOrder  int,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Tipul de adresa al firmei (02.01, 3.1). Codul tehnic e unic si nu se schimba; tipurile de sistem nu se dezactiveaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM core.AddressType WHERE Code = @Code)
            THROW 50029, N'Există deja un tip de adresă cu acest cod.', 1;

        INSERT INTO core.AddressType (Code, Name, SortOrder, IsActive, CreatedBy)
        VALUES (@Code, @Name, @SortOrder, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @IsSystem bit, @CurrentCode nvarchar(30);
        SELECT @IsSystem = IsSystem, @CurrentCode = Code FROM core.AddressType WHERE Id = @Id;
        IF @CurrentCode IS NULL
            THROW 50027, N'Tipul de adresă nu există.', 1;
        IF @CurrentCode <> @Code
            THROW 50030, N'Codul unui tip de adresă nu se poate schimba.', 1;
        IF @IsSystem = 1 AND @IsActive = 0
            THROW 50030, N'Un tip de adresă de sistem nu poate fi dezactivat.', 1;

        UPDATE core.AddressType
        SET Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 50006, N'Tipul de adresă a fost modificat de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion FROM core.AddressType WHERE Id = @Id;
END;
