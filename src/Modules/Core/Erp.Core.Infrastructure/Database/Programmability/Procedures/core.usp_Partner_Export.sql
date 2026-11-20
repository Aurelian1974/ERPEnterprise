CREATE OR ALTER PROCEDURE core.usp_Partner_Export
    @Search               nvarchar(200) = NULL,
    @FiscalSearch         nvarchar(30) = NULL,
    @Role                 tinyint = NULL,
    @PartnerType          tinyint = NULL,
    @IsActive             bit = NULL,
    @AnafInactive         bit = NULL,
    @PropertyDefinitionId int = NULL,
    @PropertyOptionId     int = NULL,
    @PropertyBool         bit = NULL
AS
BEGIN
    -- Exportul Excel al listei partenerilor (02.01, 10), cu aceleasi filtre ca usp_Partner_Search si coloanele sablonului de import.
    SET NOCOUNT ON;

    SELECT p.Code, p.Name, p.PartnerType, p.IsCustomer, p.IsSupplier, p.FiscalCode, p.TradeRegisterNo, p.VatPayer, p.VatOnCollection,
           p.AnafInactive, g.Code AS GroupCode, p.Address, p.City, p.CountyCode, p.CountryCode, p.PostalCode, p.Phone, p.Email, p.IsActive
    FROM core.Partner AS p
    LEFT JOIN core.PartnerGroup AS g ON g.Id = p.PartnerGroupId
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
    OPTION (RECOMPILE);
END;
