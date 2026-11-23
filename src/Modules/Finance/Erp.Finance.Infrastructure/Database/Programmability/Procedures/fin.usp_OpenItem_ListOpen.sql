CREATE OR ALTER PROCEDURE fin.usp_OpenItem_ListOpen
    @CompanyId int,
    @PartnerId int,
    @Role      tinyint = NULL,
    @OnlyOpen  bit = 1,
    @From      date = NULL,
    @To        date = NULL
AS
BEGIN
    -- Partidele unui partener (02.02, F3 6, IPartnerBalanceQueries.GetOpenItemsAsync; ecranele f3-02, f3-03), in ordinea FIFO: doar cele
    -- deschise (implicit) sau toate documentele dintr-un interval (tab-ul „Toate documentele”). VatNonExigibleRon: TVA-ul la incasare inca
    -- neexigibil al facturii (F3 3.7).
    SET NOCOUNT ON;

    SELECT o.Id AS OpenItemId, o.Role, o.Kind, o.SourceDocumentType, o.SourceDocumentId, o.DocumentNo, o.DocumentDate, o.DueDate, o.CurrencyCode,
        o.Amount, o.OpenAmount, o.IsDisputed, o.DisputeNote, o.IsBlockedForPayment, o.EInvoiceStatus, o.IsVatOnCollection, o.VatAmount,
        CASE WHEN o.IsVatOnCollection = 1 AND o.Kind IN (1, 3, 4)
             THEN ABS(o.VatAmountRon) - ISNULL((SELECT SUM(e.VatAmountRon) FROM fin.VatCashEntry AS e WHERE e.OpenItemId = o.Id), 0) ELSE 0 END
            AS VatNonExigibleRon,
        o.ClosedAt, o.RowVersion
    FROM fin.OpenItem AS o
    WHERE o.CompanyId = @CompanyId AND o.PartnerId = @PartnerId AND (@Role IS NULL OR o.Role = @Role)
      AND (@OnlyOpen = 0 OR o.OpenAmount <> 0)
      AND (@From IS NULL OR o.DocumentDate >= @From) AND (@To IS NULL OR o.DocumentDate <= @To)
    ORDER BY o.Role, CASE WHEN @OnlyOpen = 1 THEN o.DueDate END, o.DocumentDate, o.Id;
END;
