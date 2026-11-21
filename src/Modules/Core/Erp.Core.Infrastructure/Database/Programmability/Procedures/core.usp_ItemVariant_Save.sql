CREATE OR ALTER PROCEDURE core.usp_ItemVariant_Save
    @Id         int,
    @ItemId     int,
    @Code       nvarchar(50),
    @Name       nvarchar(250),
    @IsActive   bit,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Codul, denumirea si starea unei variante (02.01, 3.6 punctul 13); valorile ei nu se schimba (alta combinatie = alta varianta).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM core.ItemVariant WITH (UPDLOCK, HOLDLOCK) WHERE ItemId = @ItemId AND CodeKey = UPPER(@Code) AND Id <> @Id)
        THROW 50018, N'Există deja o variantă cu acest cod la articol.', 1;

    UPDATE core.ItemVariant SET Code = @Code, Name = @Name, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND ItemId = @ItemId AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM core.ItemVariant WHERE Id = @Id AND ItemId = @ItemId)
            THROW 50027, N'Varianta nu există la acest articol.', 1;
        THROW 50006, N'Varianta a fost modificată de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion FROM core.ItemVariant WHERE Id = @Id;
END;
