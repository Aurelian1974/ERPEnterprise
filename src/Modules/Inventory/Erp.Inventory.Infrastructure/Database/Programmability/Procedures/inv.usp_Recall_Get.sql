CREATE OR ALTER PROCEDURE inv.usp_Recall_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Retragerea (02.05, S4 3.6; macheta s4-05): antetul, loturile cu stocul ramas si clientii din instantaneu, cu rezultatul notat.
    SET NOCOUNT ON;

    SELECT r.Id, r.Number, r.DocumentDate, r.SourceKind, r.Reason, r.SupplierPartnerId, p.Name AS SupplierName, r.SupplierLotCode, r.Status, r.ValidatedAt,
        r.ClosedAt, r.RowVersion
    FROM inv.Recall AS r LEFT JOIN core.Partner AS p ON p.Id = r.SupplierPartnerId
    WHERE r.Id = @Id AND r.CompanyId = @CompanyId;

    SELECT x.Id AS LotId, x.CkuCode, x.LotCode, x.SupplierLotCode, x.Status,
        ISNULL((SELECT SUM(s.QtyOnHand) FROM inv.Stock AS s WHERE s.LotId = x.Id), 0) AS QtyOnHand
    FROM inv.RecallLot AS l JOIN inv.Lot AS x ON x.Id = l.LotId JOIN inv.Recall AS r ON r.Id = l.RecallId AND r.CompanyId = @CompanyId
    WHERE l.RecallId = @Id
    ORDER BY x.CkuCode;

    SELECT c.Id, c.LotId, x.CkuCode, c.PartnerId, p.Name AS PartnerName, c.SourceDocumentType, c.SourceDocumentId, c.SourceDocumentNumber, c.MovementDate,
        c.QtyBase, c.Outcome, c.OutcomeNote
    FROM inv.RecallCustomer AS c
    JOIN inv.Recall AS r ON r.Id = c.RecallId AND r.CompanyId = @CompanyId
    JOIN inv.Lot AS x ON x.Id = c.LotId
    LEFT JOIN core.Partner AS p ON p.Id = c.PartnerId
    WHERE c.RecallId = @Id
    ORDER BY p.Name, c.MovementDate;
END;
