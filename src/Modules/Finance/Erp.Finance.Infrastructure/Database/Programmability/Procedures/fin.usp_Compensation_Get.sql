CREATE OR ALTER PROCEDURE fin.usp_Compensation_Get
    @Id        int,
    @CompanyId int
AS
BEGIN
    -- Un PV de compensare cu liniile lui (F3 3.4; macheta f3-05): partida, partenerul, rolul, restul curent si suma compensata.
    SET NOCOUNT ON;

    SELECT c.Id, c.Number, c.DocumentDate, c.Kind, c.ExternalRef, c.CurrencyCode, c.TotalAmount, c.Status, c.SignedAttachmentId, c.Note, c.ValidatedAt,
        c.ReversedAt, c.CreatedAt, u.DisplayName AS CreatedByName, c.RowVersion
    FROM fin.Compensation AS c
    LEFT JOIN core.[User] AS u ON u.Id = c.CreatedBy
    WHERE c.Id = @Id AND c.CompanyId = @CompanyId;

    SELECT l.LineNumber, l.OpenItemId, o.PartnerId, p.Name AS PartnerName, o.Role, o.Kind, o.DocumentNo, o.DocumentDate, o.DueDate, o.Amount AS ItemAmount,
        o.OpenAmount, l.Amount, o.IsVatOnCollection, o.VatAmountRon, o.ExchangeRate
    FROM fin.CompensationLine AS l
    JOIN fin.Compensation AS c ON c.Id = l.CompensationId AND c.CompanyId = @CompanyId
    JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
    JOIN core.Partner AS p ON p.Id = o.PartnerId
    WHERE l.CompensationId = @Id
    ORDER BY l.LineNumber;
END;
