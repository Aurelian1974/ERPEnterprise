CREATE OR ALTER PROCEDURE inv.usp_Recall_List
    @CompanyId int,
    @Top       int = 200
AS
BEGIN
    -- Retragerile firmei (02.05, S4 10), cele mai noi intai, cu numarul de loturi si de clienti.
    SET NOCOUNT ON;
    SELECT TOP (@Top) r.Id, r.Number, r.DocumentDate, r.SourceKind, r.SupplierLotCode, r.Status,
        (SELECT COUNT(*) FROM inv.RecallLot AS l WHERE l.RecallId = r.Id) AS LotCount,
        (SELECT COUNT(DISTINCT c.PartnerId) FROM inv.RecallCustomer AS c WHERE c.RecallId = r.Id) AS CustomerCount
    FROM inv.Recall AS r
    WHERE r.CompanyId = @CompanyId
    ORDER BY r.DocumentDate DESC, r.Id DESC;
END;
