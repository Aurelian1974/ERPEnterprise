CREATE OR ALTER PROCEDURE fin.usp_VatEntry_WriteExigible
    @CompanyId           int,
    @EntryId             bigint,
    @SourceDocumentType  varchar(50),
    @SourceDocumentId    bigint,
    @ExigibilityDate     date,
    @BaseRon             decimal(19,2),
    @VatRon              decimal(19,2)
AS
BEGIN
    -- Partea devenita exigibila a unei facturi cu TVA la incasare (C4 3.1, C4-A3): randuri copil ale randurilor neexigibile ale facturii, in
    -- perioada incasarii sau platii, cu baza si TVA-ul impartite proportional cu TVA-ul randurilor (diferenta de rotunjire pe primul rand).
    SET NOCOUNT ON;

    DECLARE @Parents TABLE (Id bigint, Share decimal(19,10), Rn int);
    INSERT INTO @Parents
    SELECT Id, CASE WHEN SUM(VatRon) OVER () = 0 THEN 1.0 / COUNT(*) OVER () ELSE VatRon / SUM(VatRon) OVER () END, ROW_NUMBER() OVER (ORDER BY Id)
    FROM fin.VatEntry
    WHERE CompanyId = @CompanyId AND SourceDocumentType = @SourceDocumentType AND SourceDocumentId = @SourceDocumentId AND IsVatOnCollection = 1
      AND ParentVatEntryId IS NULL AND ExigibilityDate IS NULL;
    IF NOT EXISTS (SELECT 1 FROM @Parents)
        RETURN;

    DECLARE @Rows TABLE (ParentId bigint, Rn int, BaseRon decimal(19,2), VatRon decimal(19,2));
    INSERT INTO @Rows SELECT Id, Rn, ROUND(@BaseRon * Share, 2), ROUND(@VatRon * Share, 2) FROM @Parents;
    UPDATE @Rows SET BaseRon = BaseRon + (@BaseRon - (SELECT SUM(BaseRon) FROM @Rows)), VatRon = VatRon + (@VatRon - (SELECT SUM(VatRon) FROM @Rows)) WHERE Rn = 1;

    INSERT INTO fin.VatEntry (CompanyId, EntryId, SourceDocumentType, SourceDocumentId, DocumentNumber, DocumentDate, Direction, PartnerId, PartnerFiscalCode,
        PartnerCountryCode, PartnerName, OperationTypeCode, VatRateCode, VatRatePercent, BaseRon, VatRon, ExigibilityDate, PeriodYear, PeriodMonth, IsVatOnCollection,
        ParentVatEntryId, DeductionPercent, IsReversal)
    SELECT p.CompanyId, @EntryId, p.SourceDocumentType, p.SourceDocumentId, p.DocumentNumber, p.DocumentDate, p.Direction, p.PartnerId, p.PartnerFiscalCode,
        p.PartnerCountryCode, p.PartnerName, p.OperationTypeCode, p.VatRateCode, p.VatRatePercent, r.BaseRon, r.VatRon, @ExigibilityDate, YEAR(@ExigibilityDate),
        MONTH(@ExigibilityDate), 1, p.Id, p.DeductionPercent, CASE WHEN r.VatRon < 0 THEN 1 ELSE 0 END
    FROM @Rows AS r JOIN fin.VatEntry AS p ON p.Id = r.ParentId;
END;
