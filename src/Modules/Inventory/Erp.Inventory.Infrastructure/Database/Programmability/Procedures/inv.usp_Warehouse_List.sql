CREATE OR ALTER PROCEDURE inv.usp_Warehouse_List
    @CompanyId       int,
    @Search          nvarchar(100) = NULL,
    @Kind            tinyint = NULL,
    @LocationId      int = NULL,
    @IncludeInactive bit = 1
AS
BEGIN
    -- Lista ecranului Gestiuni (02.05, S1 11; macheta s1-01), cu filtrele pe tip, locatie si stare. @Search vine deja escapat pentru LIKE.
    -- Restrangerea pe gestiunile permise (DimensionScope) o face apelantul, dupa DimensionValueId.
    SET NOCOUNT ON;

    SELECT w.Id, w.Code, w.Name, w.Kind, w.ValuationBasis, w.LocationId, l.Name AS LocationName, w.ThirdPartyId, p.Name AS ThirdPartyName,
        w.NegativeStockPolicy, w.Purpose, w.DimensionValueId, w.IsBlocked, w.IsActive
    FROM inv.Warehouse AS w
    LEFT JOIN core.Location AS l ON l.Id = w.LocationId
    LEFT JOIN core.Partner AS p ON p.Id = w.ThirdPartyId
    WHERE w.CompanyId = @CompanyId
      AND (@IncludeInactive = 1 OR w.IsActive = 1)
      AND (@Kind IS NULL OR w.Kind = @Kind)
      AND (@LocationId IS NULL OR w.LocationId = @LocationId)
      AND (@Search IS NULL OR w.CodeKey LIKE UPPER(@Search) + N'%' OR w.NameSearch LIKE N'%' + @Search + N'%')
    ORDER BY w.Kind, w.CodeKey;
END;
