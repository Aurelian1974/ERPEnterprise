CREATE OR ALTER PROCEDURE core.usp_ItemReference_Save
    @Id            int = NULL,
    @ItemId        int,
    @PartnerId     int,
    @ReferenceType tinyint,
    @Code          nvarchar(50),
    @Description   nvarchar(250) = NULL,
    @UomId         int = NULL,
    @IsActive      bit,
    @RowVersion    binary(8) = NULL,
    @UserId        int
AS
BEGIN
    -- Codul articolului la un partener (02.01, 3.6 punctul 11): partenerul are rolul cerut (client / furnizor), UM e una a articolului,
    -- codul e unic pe partener si tip (50018). Codul nu se sterge, se dezactiveaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @ItemId)
        THROW 50027, N'Articolul nu există.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId
                   AND ((@ReferenceType = 1 AND IsCustomer = 1) OR (@ReferenceType = 2 AND IsSupplier = 1)))
        THROW 50021, N'Partenerul nu există sau nu e client / furnizor, după tipul codului.', 1;
    IF @UomId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM core.Item AS i JOIN core.UomSetLine AS l ON l.UomSetId = i.UomSetId WHERE i.Id = @ItemId AND l.UomId = @UomId
        UNION ALL
        SELECT 1 FROM core.ItemUom AS x WHERE x.ItemId = @ItemId AND x.UomId = @UomId)
        THROW 50021, N'UM aleasă nu e una a articolului.', 1;
    IF EXISTS (SELECT 1 FROM core.ItemPartnerReference WITH (UPDLOCK, HOLDLOCK)
               WHERE PartnerId = @PartnerId AND ReferenceType = @ReferenceType AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul există deja la acest partener (poate la alt articol).', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.ItemPartnerReference (ItemId, PartnerId, ReferenceType, Code, Description, UomId, IsActive, CreatedBy)
        VALUES (@ItemId, @PartnerId, @ReferenceType, @Code, @Description, @UomId, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.ItemPartnerReference
        SET PartnerId = @PartnerId, ReferenceType = @ReferenceType, Code = @Code, Description = @Description, UomId = @UomId, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND ItemId = @ItemId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.ItemPartnerReference WHERE Id = @Id AND ItemId = @ItemId)
                THROW 50027, N'Codul nu există la acest articol.', 1;
            THROW 50006, N'Codul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.ItemPartnerReference WHERE Id = @Id;
END;
