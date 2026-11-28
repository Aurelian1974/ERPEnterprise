CREATE OR ALTER PROCEDURE fin.usp_Saft_ReadInvoices
    @CompanyId  int,
    @Direction  tinyint,
    @From       date,
    @To         date,
    @AfterId    bigint = 0,
    @Take       int = 5000
AS
BEGIN
    -- O pagina din facturile D406 (C4 3.5): randurile registrului de TVA ale facturilor emise (1) sau primite (2) cu data documentului in perioada,
    -- in ordinea documentului; aplicatia le grupeaza pe factura. Randurile copil ale TVA la incasare nu se repeta (factura e o singura data).
    SET NOCOUNT ON;
    SELECT TOP (@Take) v.Id, v.SourceDocumentType, v.SourceDocumentId, v.DocumentNumber, v.DocumentDate, v.PartnerId, v.OperationTypeCode, v.VatRateCode,
        v.VatRatePercent, v.BaseRon, v.VatRon, v.IsReversal
    FROM fin.VatEntry AS v
    WHERE v.CompanyId = @CompanyId AND v.Direction = @Direction AND v.DocumentDate BETWEEN @From AND @To AND v.ParentVatEntryId IS NULL AND v.Id > @AfterId
    ORDER BY v.Id;
END;
