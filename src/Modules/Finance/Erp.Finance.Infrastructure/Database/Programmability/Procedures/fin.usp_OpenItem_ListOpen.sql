CREATE OR ALTER PROCEDURE fin.usp_OpenItem_ListOpen
    @CompanyId int,
    @PartnerId int,
    @Role      tinyint = NULL
AS
BEGIN
    -- Partidele deschise ale unui partener (02.02, F3 6, IPartnerBalanceQueries.GetOpenItemsAsync; ecranele f3-02, f3-03), in ordinea FIFO.
    SET NOCOUNT ON;

    SELECT o.Id AS OpenItemId, o.Role, o.Kind, o.SourceDocumentType, o.SourceDocumentId, o.DocumentNo, o.DocumentDate, o.DueDate, o.CurrencyCode,
        o.Amount, o.OpenAmount, o.IsDisputed, o.DisputeNote, o.IsBlockedForPayment, o.EInvoiceStatus, o.IsVatOnCollection, o.RowVersion
    FROM fin.OpenItem AS o
    WHERE o.CompanyId = @CompanyId AND o.PartnerId = @PartnerId AND (@Role IS NULL OR o.Role = @Role) AND o.OpenAmount <> 0
    ORDER BY o.Role, o.DueDate, o.DocumentDate, o.Id;
END;
