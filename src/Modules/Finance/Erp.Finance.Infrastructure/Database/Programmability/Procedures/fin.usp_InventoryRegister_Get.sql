CREATE OR ALTER PROCEDURE fin.usp_InventoryRegister_Get
    @CompanyId   int,
    @Year        smallint,
    @WithPdf     bit = 0
AS
BEGIN
    -- Registrul inventar al anului: antetul (si PDF-ul, la cerere), apoi liniile.
    SET NOCOUNT ON;
    DECLARE @Id int = (SELECT Id FROM fin.InventoryRegister WHERE CompanyId = @CompanyId AND Year = @Year);
    SELECT Id, Year, IsFinal, GeneratedAt, CASE WHEN @WithPdf = 1 THEN Pdf END AS Pdf FROM fin.InventoryRegister WHERE Id = @Id;
    SELECT AccountCode, AccountName, PartnerId, PartnerName, Side, BookValue, InventoryValue, Difference
    FROM fin.InventoryRegisterLine WHERE RegisterId = @Id ORDER BY AccountCode, PartnerName;
END;
