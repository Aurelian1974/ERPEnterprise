CREATE OR ALTER PROCEDURE core.usp_Partner_Search
    @Search               nvarchar(200) = NULL,
    @FiscalSearch         nvarchar(30) = NULL,
    @Role                 tinyint = NULL,
    @PartnerType          tinyint = NULL,
    @IsActive             bit = NULL,
    @AnafInactive         bit = NULL,
    @PropertyDefinitionId int = NULL,
    @PropertyOptionId     int = NULL,
    @PropertyBool         bit = NULL,
    @Offset               int = 0,
    @PageSize             int = 50
AS
BEGIN
    -- Lista partenerilor (macheta 04; publica pentru module): cautare prefix SARGable dupa denumire (fara majuscule si
    -- diacritice, NameSearch), cod (CodeKey) sau CUI; filtre pe rol (1 client, 2 furnizor), tip, stare, inactiv ANAF si o
    -- proprietate filtrabila (optiune de lista sau da/nu). Al doilea rezultat: numarul partenerilor activi, pentru antet.
    SET NOCOUNT ON;

    SELECT p.Id, p.Code, p.Name, p.PartnerType, p.FiscalCode, p.IsCustomer, p.IsSupplier, p.VatPayer, p.VatOnCollection,
           p.AnafInactive, p.CountryCode, p.City, p.IsActive, COUNT(*) OVER () AS TotalCount
    FROM core.Partner AS p
    WHERE (@Search IS NULL
            OR p.NameSearch LIKE @Search + N'%'
            OR p.CodeKey LIKE UPPER(@Search) + N'%'
            OR (@FiscalSearch IS NOT NULL AND p.FiscalCode LIKE @FiscalSearch + N'%'))
      AND (@Role IS NULL OR (@Role = 1 AND p.IsCustomer = 1) OR (@Role = 2 AND p.IsSupplier = 1))
      AND (@PartnerType IS NULL OR p.PartnerType = @PartnerType)
      AND (@IsActive IS NULL OR p.IsActive = @IsActive)
      AND (@AnafInactive IS NULL OR p.AnafInactive = @AnafInactive)
      AND (@PropertyDefinitionId IS NULL OR EXISTS (
            SELECT 1 FROM core.PropertyValue AS v
            WHERE v.PropertyDefinitionId = @PropertyDefinitionId AND v.EntityId = p.Id
              AND (@PropertyOptionId IS NULL OR v.ValueOptionId = @PropertyOptionId)
              AND (@PropertyBool IS NULL OR v.ValueBool = @PropertyBool)))
    ORDER BY p.NameSearch, p.Id
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);

    SELECT COUNT(*) AS ActiveCount FROM core.Partner WHERE IsActive = 1;
END;
