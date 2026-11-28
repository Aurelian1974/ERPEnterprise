CREATE OR ALTER PROCEDURE inv.usp_Lot_ExpiryScan
    @CompanyId int,
    @Today     date,
    @UserId    int
AS
BEGIN
    -- Jobul zilnic de expirare (02.05, S4 3.2), pe o firma: loturile cu data limita de consum depasita trec in Expirat (cu istoric); loturile cu
    -- durabilitate minimala raman disponibile. Intoarce loturile expirate (pentru LotStareSchimbata) si, pe gestiune, loturile cu stoc care expira
    -- in urmatoarele ExpiryWarningDays zile (pentru LoturiAproapeDeExpirare).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Expired TABLE (LotId int PRIMARY KEY, FromStatus tinyint);
    UPDATE x SET Status = 4, StatusReason = N'Data limită de consum a trecut', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.Id, deleted.Status INTO @Expired
    FROM inv.Lot AS x
    WHERE x.CompanyId = @CompanyId AND x.IsTechnical = 0 AND x.ExpiryKind = 1 AND x.ExpiryDate < @Today AND x.Status IN (1, 2, 3);

    INSERT INTO inv.LotStatusHistory (LotId, FromStatus, ToStatus, Reason, SourceDocumentType, ChangedBy)
    SELECT LotId, FromStatus, 4, N'Data limită de consum a trecut', 'LotExpiryScan', @UserId FROM @Expired;

    SELECT x.Id AS LotId, x.ItemVariantId, x.LotCode, e.FromStatus, x.SupplierPartnerId
    FROM @Expired AS e JOIN inv.Lot AS x ON x.Id = e.LotId;

    DECLARE @Days int = ISNULL((SELECT ExpiryWarningDays FROM inv.StockSettings WHERE CompanyId = @CompanyId), 30);
    SELECT s.WarehouseId, x.Id AS LotId, x.ItemVariantId, x.ExpiryDate, SUM(s.QtyOnHand) AS QtyOnHand
    FROM inv.Lot AS x
    JOIN inv.Stock AS s ON s.LotId = x.Id AND s.QtyOnHand > 0
    WHERE x.CompanyId = @CompanyId AND x.IsTechnical = 0 AND x.Status IN (1, 2, 3) AND x.ExpiryDate BETWEEN @Today AND DATEADD(DAY, @Days, @Today)
    GROUP BY s.WarehouseId, x.Id, x.ItemVariantId, x.ExpiryDate
    ORDER BY s.WarehouseId, x.ExpiryDate;
END;
