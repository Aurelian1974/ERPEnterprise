CREATE OR ALTER PROCEDURE core.usp_ItemAttribute_Save
    @Id         int = NULL,
    @Code       nvarchar(20),
    @Name       nvarchar(100),
    @SortOrder  int,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @Values     core.ItemAttributeValueList READONLY
AS
BEGIN
    -- Atributul de varianta cu valorile lui (02.01, 3.6 punctul 13): codul atributului e unic (50018), codurile valorilor unice in
    -- atribut (50018). Valorile primite se adauga sau se modifica; cele lipsa din lista raman (se dezactiveaza explicit).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM core.ItemAttribute WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Există deja un atribut cu acest cod.', 1;
    IF EXISTS (SELECT UPPER(Code) FROM @Values GROUP BY UPPER(Code) HAVING COUNT(*) > 1)
        THROW 50018, N'Un cod de valoare apare de două ori.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.ItemAttribute (Code, Name, SortOrder, IsActive) VALUES (@Code, @Name, @SortOrder, @IsActive);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.ItemAttribute SET Code = @Code, Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive WHERE Id = @Id AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.ItemAttribute WHERE Id = @Id)
                THROW 50027, N'Atributul nu există.', 1;
            THROW 50006, N'Atributul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    IF EXISTS (SELECT 1 FROM @Values AS x JOIN core.ItemAttributeValue AS v ON v.AttributeId = @Id AND v.CodeKey = UPPER(x.Code)
               WHERE x.Id IS NULL OR x.Id <> v.Id)
        THROW 50018, N'Există deja o valoare cu acest cod la atribut.', 1;
    IF EXISTS (SELECT 1 FROM @Values AS x WHERE x.Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.ItemAttributeValue AS v WHERE v.Id = x.Id AND v.AttributeId = @Id))
        THROW 50021, N'O valoare nu aparține atributului.', 1;

    UPDATE v SET Code = x.Code, Name = x.Name, SortOrder = x.SortOrder, IsActive = x.IsActive
    FROM core.ItemAttributeValue AS v JOIN @Values AS x ON x.Id = v.Id;
    INSERT INTO core.ItemAttributeValue (AttributeId, Code, Name, SortOrder, IsActive)
    SELECT @Id, x.Code, x.Name, x.SortOrder, x.IsActive FROM @Values AS x WHERE x.Id IS NULL;

    SELECT Id, RowVersion FROM core.ItemAttribute WHERE Id = @Id;
END;
