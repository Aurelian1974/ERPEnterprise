CREATE OR ALTER PROCEDURE core.usp_ItemVariant_Generate
    @ItemId   int,
    @ValueIds core.IdList READONLY,
    @UserId   int
AS
BEGIN
    -- Genereaza variantele articolului din valorile alese (02.01, 3.6 punctul 13): toate combinatiile cu cate o valoare din fiecare
    -- atribut ales, fara cele care exista deja (ValueKey). Codul variantei = codurile valorilor unite cu „-” (de exemplu M-ROSU), denumirea
    -- = denumirea articolului urmata de valori. Toate variantele unui articol folosesc aceleasi atribute (50021); cel mult 500 (50021).
    -- Intoarce numarul variantelor noi.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ItemName nvarchar(250) = (SELECT Name FROM core.Item WITH (UPDLOCK, HOLDLOCK) WHERE Id = @ItemId);
    IF @ItemName IS NULL
        THROW 50027, N'Articolul nu există.', 1;

    DECLARE @V TABLE (ValueId int NOT NULL, AttributeId int NOT NULL, Code nvarchar(20) NOT NULL, Name nvarchar(100) NOT NULL, Rn int NOT NULL,
        ValueOrder int NOT NULL);
    INSERT INTO @V (ValueId, AttributeId, Code, Name, Rn, ValueOrder)
    SELECT v.Id, v.AttributeId, v.Code, v.Name, DENSE_RANK() OVER (ORDER BY v.AttributeId), v.SortOrder
    FROM @ValueIds AS x
    JOIN core.ItemAttributeValue AS v ON v.Id = x.Id AND v.IsActive = 1
    JOIN core.ItemAttribute AS a ON a.Id = v.AttributeId AND a.IsActive = 1;

    IF NOT EXISTS (SELECT 1 FROM @V)
        THROW 50021, N'Alegeți cel puțin o valoare activă a unui atribut.', 1;
    IF EXISTS (SELECT 1 FROM core.ItemVariant WHERE ItemId = @ItemId)
       AND (EXISTS (SELECT x.AttributeId FROM core.ItemVariantValue AS x JOIN core.ItemVariant AS iv ON iv.Id = x.VariantId WHERE iv.ItemId = @ItemId
                    EXCEPT SELECT AttributeId FROM @V)
            OR EXISTS (SELECT AttributeId FROM @V
                       EXCEPT SELECT x.AttributeId FROM core.ItemVariantValue AS x JOIN core.ItemVariant AS iv ON iv.Id = x.VariantId WHERE iv.ItemId = @ItemId))
        THROW 50021, N'Variantele existente ale articolului folosesc alte atribute; alegeți valori din aceleași atribute.', 1;

    DECLARE @Levels int = (SELECT MAX(Rn) FROM @V);
    DECLARE @Combinations TABLE (ValueKey varchar(200) NOT NULL PRIMARY KEY, Code nvarchar(200) NOT NULL, NamePart nvarchar(400) NOT NULL);

    WITH Combo AS (
        SELECT 1 AS Level, CAST(CAST(v.ValueId AS varchar(12)) AS varchar(200)) AS ValueKey, CAST(v.Code AS nvarchar(200)) AS Code,
               CAST(v.Name AS nvarchar(400)) AS NamePart
        FROM @V AS v WHERE v.Rn = 1
        UNION ALL
        SELECT c.Level + 1, CAST(c.ValueKey + ',' + CAST(v.ValueId AS varchar(12)) AS varchar(200)), CAST(c.Code + N'-' + v.Code AS nvarchar(200)),
               CAST(c.NamePart + N' / ' + v.Name AS nvarchar(400))
        FROM Combo AS c JOIN @V AS v ON v.Rn = c.Level + 1
    )
    INSERT INTO @Combinations (ValueKey, Code, NamePart)
    SELECT c.ValueKey, c.Code, c.NamePart FROM Combo AS c WHERE c.Level = @Levels;

    IF (SELECT COUNT(*) FROM @Combinations) > 500
        THROW 50021, N'Combinațiile alese ar crea peste 500 de variante; alegeți mai puține valori.', 1;

    DELETE c FROM @Combinations AS c WHERE EXISTS (SELECT 1 FROM core.ItemVariant AS v WHERE v.ItemId = @ItemId AND v.ValueKey = c.ValueKey);
    IF EXISTS (SELECT 1 FROM @Combinations AS c JOIN core.ItemVariant AS v ON v.ItemId = @ItemId AND v.CodeKey = UPPER(LEFT(c.Code, 50)))
        THROW 50018, N'O variantă nouă ar avea codul unei variante existente; schimbați codul variantei existente.', 1;

    INSERT INTO core.ItemVariant (ItemId, Code, Name, ValueKey, CreatedBy)
    SELECT @ItemId, LEFT(c.Code, 50), LEFT(@ItemName + N' — ' + c.NamePart, 250), c.ValueKey, @UserId FROM @Combinations AS c;

    INSERT INTO core.ItemVariantValue (VariantId, AttributeId, ValueId)
    SELECT iv.Id, v.AttributeId, v.ValueId
    FROM core.ItemVariant AS iv
    JOIN @Combinations AS c ON c.ValueKey = iv.ValueKey
    JOIN @V AS v ON CONCAT(',', iv.ValueKey, ',') LIKE CONCAT('%,', v.ValueId, ',%')
    WHERE iv.ItemId = @ItemId;

    SELECT COUNT(*) AS Created FROM @Combinations;
END;
