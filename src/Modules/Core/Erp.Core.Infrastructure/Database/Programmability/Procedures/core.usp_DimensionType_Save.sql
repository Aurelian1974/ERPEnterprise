CREATE OR ALTER PROCEDURE core.usp_DimensionType_Save
    @Code       varchar(20),
    @Name       nvarchar(100),
    @SortOrder  int,
    @IsActive   bit,
    @IsNew      bit,
    @RowVersion binary(8) = NULL
AS
BEGIN
    -- Tip de dimensiune definit de client (02.01, 3.6 punctul 15): codul e fix dupa creare (50018 la duplicat); tipurile de sistem
    -- nu se dezactiveaza (50021). Un tip cu valori active nu se dezactiveaza (50010).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @IsNew = 1
    BEGIN
        IF EXISTS (SELECT 1 FROM core.DimensionType WITH (UPDLOCK, HOLDLOCK) WHERE UPPER(Code) = UPPER(@Code))
            THROW 50018, N'Există deja un tip de dimensiune cu acest cod.', 1;
        INSERT INTO core.DimensionType (Code, Name, SortOrder, IsSystem, IsActive) VALUES (@Code, @Name, @SortOrder, 0, @IsActive);
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.DimensionType WHERE Code = @Code AND IsSystem = 1)
            THROW 50021, N'Tipurile de dimensiuni livrate nu se dezactivează.', 1;
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.DimensionValue WHERE DimensionType = @Code AND IsActive = 1)
            THROW 50010, N'Tipul are valori active; dezactivați-le întâi.', 1;
        UPDATE core.DimensionType SET Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Code = @Code AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.DimensionType WHERE Code = @Code)
                THROW 50027, N'Tipul de dimensiune nu există.', 1;
            THROW 50006, N'Tipul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT 0 AS Id, RowVersion FROM core.DimensionType WHERE Code = @Code;
END;
