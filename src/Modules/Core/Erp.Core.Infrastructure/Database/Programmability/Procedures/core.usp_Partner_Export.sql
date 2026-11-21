CREATE OR ALTER PROCEDURE core.usp_Partner_Export
    @Search               nvarchar(200) = NULL,
    @FiscalSearch         nvarchar(30) = NULL,
    @Role                 tinyint = NULL,
    @PartnerType          tinyint = NULL,
    @IsActive             bit = NULL,
    @AnafInactive         bit = NULL,
    @PropertyDefinitionId int = NULL,
    @PropertyOptionId     int = NULL,
    @PropertyBool         bit = NULL,
    @UserId               int = NULL,
    @CompanyId            int = NULL,
    @OwnerFilter          tinyint = NULL,
    @OwnerBranchId        int = NULL,
    @OwnerLocationId      int = NULL,
    @ContextBranchId      int = NULL,
    @ContextLocationId    int = NULL
AS
BEGIN
    -- Exportul Excel al listei partenerilor (02.01, 10), cu aceleasi filtre ca usp_Partner_Search si coloanele sablonului de import.
    SET NOCOUNT ON;

    -- Vizibilitatea pe proprietar (21.11.2026): cu @UserId, comunele si cele proprii nodurilor accesibile in firma @CompanyId
    -- (core.fn_UserOwnerNodes); cu @ContextBranchId / @ContextLocationId (nodul din antet), ca si cum acela ar fi nodul lui. @OwnerFilter: 1 doar comune, 2 doar proprii firmei (oricarui nod al ei); @OwnerBranchId /
    -- @OwnerLocationId: doar cele ale punctului de lucru (cu locatiile lui) sau ale locatiei. Fara @UserId nu se filtreaza (module).
    DECLARE @Nodes TABLE (WholeCompany bit NOT NULL, BranchId int NULL, LocationId int NULL);
    IF @UserId IS NOT NULL
        INSERT INTO @Nodes (WholeCompany, BranchId, LocationId)
        SELECT WholeCompany, BranchId, LocationId
        FROM core.fn_UserOwnerNodesInContext(@UserId, @CompanyId, N'Core.Partner.View', @ContextBranchId, @ContextLocationId);
    DECLARE @AnyNode bit = CASE WHEN EXISTS (SELECT 1 FROM @Nodes) THEN 1 ELSE 0 END;

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
      AND (@UserId IS NULL
            OR (p.OwnerCompanyId IS NULL AND @AnyNode = 1)
            OR (p.OwnerCompanyId = @CompanyId AND EXISTS (
                    SELECT 1 FROM @Nodes AS n
                    WHERE n.WholeCompany = 1 OR n.BranchId = p.OwnerBranchId OR n.LocationId = p.OwnerLocationId
                       OR (n.BranchId IS NULL AND n.LocationId IS NULL AND p.OwnerBranchId IS NULL AND p.OwnerLocationId IS NULL))))
      AND (@OwnerFilter IS NULL OR (@OwnerFilter = 1 AND p.OwnerCompanyId IS NULL) OR (@OwnerFilter = 2 AND p.OwnerCompanyId = @CompanyId))
      AND (@OwnerBranchId IS NULL OR p.OwnerBranchId = @OwnerBranchId
            OR p.OwnerLocationId IN (SELECT l.Id FROM core.Location AS l WHERE l.BranchId = @OwnerBranchId))
      AND (@OwnerLocationId IS NULL OR p.OwnerLocationId = @OwnerLocationId)
    ORDER BY p.NameSearch, p.Id
    OPTION (RECOMPILE);
END;
