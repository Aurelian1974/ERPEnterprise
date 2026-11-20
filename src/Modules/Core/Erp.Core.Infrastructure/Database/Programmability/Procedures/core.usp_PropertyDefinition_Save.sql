CREATE OR ALTER PROCEDURE core.usp_PropertyDefinition_Save
    @Id           int = NULL,
    @EntityType   varchar(20),
    @Code         nvarchar(30),
    @Name         nvarchar(100),
    @DataType     tinyint,
    @AppliesTo    tinyint,
    @Section      nvarchar(50),
    @SortOrder    int,
    @IsRequired   bit,
    @IsFilterable bit,
    @CompanyId    int = NULL,
    @IsActive     bit,
    @Options      core.PropertyOptionList READONLY,
    @RowVersion   binary(8) = NULL,
    @UserId       int
AS
BEGIN
    -- Definitia unei proprietati configurabile (02.01, 3.2; macheta 07l). Codul e unic pe entitate si firma (fara majuscule) si
    -- nu se schimba dupa creare; tipul unei proprietati folosite nu se mai schimba (se dezactiveaza si se creeaza alta).
    -- Optiunile listelor: cele cu Id se modifica, cele fara Id se adauga; nu se sterg (o optiune folosita se dezactiveaza).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @CompanyId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma aleasă nu există.', 1;

    IF EXISTS (SELECT 1 FROM core.PropertyDefinition WITH (UPDLOCK, HOLDLOCK)
               WHERE EntityType = @EntityType AND CompanyId IS NOT DISTINCT FROM @CompanyId AND CodeKey = UPPER(@Code)
                 AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul proprietății există deja.', 1;

    IF EXISTS (SELECT CodeKey FROM (SELECT UPPER(Code) AS CodeKey FROM @Options) AS o GROUP BY CodeKey HAVING COUNT(*) > 1)
        THROW 50018, N'Două opțiuni au același cod.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.PropertyDefinition (EntityType, Code, Name, DataType, AppliesTo, Section, SortOrder, IsRequired, IsFilterable,
            CompanyId, IsActive, CreatedBy)
        VALUES (@EntityType, @Code, @Name, @DataType, @AppliesTo, @Section, @SortOrder, @IsRequired, @IsFilterable,
            @CompanyId, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @OldCode nvarchar(30), @OldType tinyint, @OldEntity varchar(20);
        SELECT @OldCode = Code, @OldType = DataType, @OldEntity = EntityType FROM core.PropertyDefinition WHERE Id = @Id;
        IF @OldCode IS NULL
            THROW 50027, N'Proprietatea nu există.', 1;

        IF @OldCode <> @Code OR @OldEntity <> @EntityType
            THROW 50021, N'Codul și entitatea unei proprietăți nu se schimbă după creare.', 1;

        IF @OldType <> @DataType AND EXISTS (SELECT 1 FROM core.PropertyValue WHERE PropertyDefinitionId = @Id)
            THROW 50010, N'Proprietatea are valori: tipul nu se mai schimbă. Dezactivați-o și creați alta.', 1;

        UPDATE core.PropertyDefinition
        SET Name = @Name, DataType = @DataType, AppliesTo = @AppliesTo, Section = @Section, SortOrder = @SortOrder,
            IsRequired = @IsRequired, IsFilterable = @IsFilterable, CompanyId = @CompanyId, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 50006, N'Proprietatea a fost modificată de alt utilizator; reîncărcați datele.', 1;
    END;

    IF EXISTS (SELECT 1 FROM @Options AS o WHERE o.Id IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM core.PropertyOption AS p WHERE p.Id = o.Id AND p.PropertyDefinitionId = @Id))
        THROW 50021, N'O opțiune nu aparține acestei proprietăți.', 1;

    IF EXISTS (SELECT 1 FROM core.PropertyOption AS p
               JOIN @Options AS o ON UPPER(o.Code) = p.CodeKey AND (o.Id IS NULL OR o.Id <> p.Id)
               WHERE p.PropertyDefinitionId = @Id AND NOT EXISTS (SELECT 1 FROM @Options AS x WHERE x.Id = p.Id))
        THROW 50018, N'Codul opțiunii există deja la această proprietate.', 1;

    UPDATE p
    SET Code = o.Code, Label = o.Label, SortOrder = o.SortOrder, IsActive = o.IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM core.PropertyOption AS p
    JOIN @Options AS o ON o.Id = p.Id
    WHERE p.PropertyDefinitionId = @Id
      AND (p.Code <> o.Code OR p.Label <> o.Label OR p.SortOrder <> o.SortOrder OR p.IsActive <> o.IsActive);

    INSERT INTO core.PropertyOption (PropertyDefinitionId, Code, Label, SortOrder, IsActive, CreatedBy)
    SELECT @Id, o.Code, o.Label, o.SortOrder, o.IsActive, @UserId
    FROM @Options AS o
    WHERE o.Id IS NULL;

    SELECT Id, RowVersion FROM core.PropertyDefinition WHERE Id = @Id;
END;
