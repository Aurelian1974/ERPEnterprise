CREATE OR ALTER PROCEDURE inv.usp_TransitShipment_Refresh
    @CompanyId  int,
    @ShipmentId int,
    @UserId     int
AS
BEGIN
    -- Starea expeditiei dupa o primire (02.05, S2 3.4, 4.3 pasul 5): Primit partial cat timp ramane ceva in tranzit, apoi Primit sau Primit cu
    -- diferente cand totul e primit sau rezolvat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NpId int = (SELECT NpDocumentId FROM inv.TransitShipment WHERE Id = @ShipmentId AND CompanyId = @CompanyId);
    DECLARE @Open decimal(19,6), @Differences bit;
    SELECT @Open = SUM(np.QtyBase - ISNULL(x.Consumed, 0)), @Differences = MAX(CAST(x.HasDifference AS int))
    FROM inv.StockDocumentLine AS np
    OUTER APPLY (SELECT SUM(nr.ReceivedQtyBase + CASE WHEN nr.DifferenceResolution IN (2, 3, 4, 5) THEN nr.DifferenceQtyBase ELSE 0 END) AS Consumed,
                     MAX(CASE WHEN nr.DifferenceResolution IN (2, 3, 4, 5) THEN 1 ELSE 0 END) AS HasDifference
                 FROM inv.StockDocumentLine AS nr JOIN inv.StockDocument AS d ON d.Id = nr.DocumentId
                 WHERE nr.ShipmentLineId = np.Id AND d.Status = 2 AND d.IsReversal = 0) AS x
    WHERE np.DocumentId = @NpId;

    UPDATE inv.TransitShipment
    SET Status = CASE WHEN @Open > 0 THEN 2 WHEN ISNULL(@Differences, 0) = 1 THEN 4 ELSE 3 END, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @ShipmentId;

    SELECT Status FROM inv.TransitShipment WHERE Id = @ShipmentId;
END;
