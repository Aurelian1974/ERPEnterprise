CREATE OR ALTER PROCEDURE core.usp_DimensionCombination_Save
    @CompanyId int,
    @TypeA     varchar(20),
    @ValueAId  int = NULL,
    @TypeB     varchar(20),
    @ValueBId  int = NULL,
    @Note      nvarchar(200) = NULL,
    @UserId    int
AS
BEGIN
    -- O combinatie blocata (02.01, 3.6 punctul 14). Tipurile se ordoneaza (TypeA < TypeB), ca aceeasi pereche sa nu apara de doua
    -- ori; valorile sunt ale firmei si de tipul lor (cheile compuse). Duplicat = 50018.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @TypeA = @TypeB
        THROW 50021, N'Alegeți două tipuri de dimensiuni diferite.', 1;
    IF @TypeA > @TypeB
    BEGIN
        DECLARE @T varchar(20) = @TypeA, @V int = @ValueAId;
        SELECT @TypeA = @TypeB, @ValueAId = @ValueBId, @TypeB = @T, @ValueBId = @V;
    END;
    IF (@ValueAId IS NULL AND @ValueBId IS NOT NULL) OR (@ValueAId IS NOT NULL AND @ValueBId IS NULL)
        THROW 50021, N'Alegeți ambele valori sau niciuna (atunci tipurile nu se folosesc împreună).', 1;
    IF (@ValueAId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue WHERE Id = @ValueAId AND CompanyId = @CompanyId AND DimensionType = @TypeA))
       OR (@ValueBId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue WHERE Id = @ValueBId AND CompanyId = @CompanyId AND DimensionType = @TypeB))
        THROW 50021, N'O valoare aleasă nu e a firmei sau nu e de tipul ales.', 1;
    IF EXISTS (SELECT 1 FROM core.DimensionCombination WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND TypeA = @TypeA AND TypeB = @TypeB
               AND ValueAId IS NOT DISTINCT FROM @ValueAId AND ValueBId IS NOT DISTINCT FROM @ValueBId)
        THROW 50018, N'Combinația este deja blocată.', 1;

    INSERT INTO core.DimensionCombination (CompanyId, TypeA, ValueAId, TypeB, ValueBId, Note, CreatedBy)
    VALUES (@CompanyId, @TypeA, @ValueAId, @TypeB, @ValueBId, @Note, @UserId);

    SELECT CAST(SCOPE_IDENTITY() AS int) AS Id, CAST(0x0000000000000000 AS binary(8)) AS RowVersion;
END;
