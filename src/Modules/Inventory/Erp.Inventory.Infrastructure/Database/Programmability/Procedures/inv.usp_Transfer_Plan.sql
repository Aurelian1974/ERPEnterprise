CREATE OR ALTER PROCEDURE inv.usp_Transfer_Plan
    @CompanyId  int,
    @DocumentId int
AS
BEGIN
    -- Etapele unei note de predare (02.05, S2 3.4; decizia 2), deduse din locatiile celor doua gestiuni si din setarea TransferMode a firmei
    -- (1 dupa locatie, 2 mereu doua etape, 3 mereu o etapa). Aceeasi locatie: aceeasi locatie a gestiunilor sau a depozitelor; gestiunile la terti
    -- n-au locatie. Perechea invalida: aceeasi gestiune si acelasi depozit, sau custodia cu o gestiune care nu e de custodie (54106).
    -- Intoarce etapele si gestiunea de tranzit a firmei.
    SET NOCOUNT ON;

    DECLARE @FromId int, @ToId int, @FromDepot int, @ToDepot int;
    SELECT @FromId = FromWarehouseId, @ToId = ToWarehouseId, @FromDepot = FromDepotId, @ToDepot = ToDepotId
    FROM inv.StockDocument WHERE Id = @DocumentId AND CompanyId = @CompanyId AND DocumentType = 'NP';
    IF @FromId IS NULL
        THROW 54001, N'Nota de predare nu există în firma curentă.', 1;

    DECLARE @FromKind tinyint, @ToKind tinyint, @FromLocation int, @ToLocation int;
    SELECT @FromKind = w.Kind, @FromLocation = COALESCE(w.LocationId, d.LocationId)
    FROM inv.Warehouse AS w LEFT JOIN inv.Depot AS d ON d.Id = @FromDepot WHERE w.Id = @FromId;
    SELECT @ToKind = w.Kind, @ToLocation = COALESCE(w.LocationId, d.LocationId)
    FROM inv.Warehouse AS w LEFT JOIN inv.Depot AS d ON d.Id = @ToDepot WHERE w.Id = @ToId;

    IF (@FromId = @ToId AND ISNULL(@FromDepot, 0) = ISNULL(@ToDepot, 0)) OR (CASE WHEN @FromKind = 4 THEN 1 ELSE 0 END) <> (CASE WHEN @ToKind = 4 THEN 1 ELSE 0 END)
        THROW 54106, N'Predarea cere două gestiuni (sau două depozite) diferite; custodia se mută doar între gestiuni de custodie.', 1;

    DECLARE @Setting tinyint = ISNULL((SELECT TransferMode FROM inv.StockSettings WHERE CompanyId = @CompanyId), 1);
    DECLARE @SameLocation bit = CASE WHEN @FromLocation IS NOT NULL AND @FromLocation = @ToLocation THEN 1 ELSE 0 END;

    SELECT CAST(CASE @Setting WHEN 2 THEN 2 WHEN 3 THEN 1 ELSE CASE WHEN @SameLocation = 1 THEN 1 ELSE 2 END END AS tinyint) AS TransferMode,
        (SELECT Id FROM inv.Warehouse WHERE CompanyId = @CompanyId AND Kind = 3) AS TransitWarehouseId;
END;
