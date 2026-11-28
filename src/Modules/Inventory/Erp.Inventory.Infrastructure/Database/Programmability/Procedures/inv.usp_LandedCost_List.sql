CREATE OR ALTER PROCEDURE inv.usp_LandedCost_List
    @CompanyId int,
    @Status    tinyint = NULL,
    @Top       int = 200
AS
BEGIN
    -- Lista documentelor CA (02.05, S3 11; macheta s3-01), cele mai noi intai.
    SET NOCOUNT ON;

    SELECT TOP (@Top) c.Id, c.Number, c.DocumentDate, c.AccountingDate, c.SourceKind, c.SupplierInvoiceId, p.Name AS PartnerName, c.Amount,
        c.AllocationBasis, c.Status, c.StockAmount, c.ExpenseAmount, (SELECT COUNT(*) FROM inv.LandedCostLine AS l WHERE l.LandedCostId = c.Id) AS LineCount
    FROM inv.LandedCost AS c
    LEFT JOIN core.Partner AS p ON p.Id = c.PartnerId
    WHERE c.CompanyId = @CompanyId AND (@Status IS NULL OR c.Status = @Status)
    ORDER BY c.DocumentDate DESC, c.Id DESC;
END;
