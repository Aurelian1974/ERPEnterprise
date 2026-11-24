CREATE OR ALTER PROCEDURE fin.usp_VatEntry_Write
    @CompanyId           int,
    @EntryId             bigint,
    @SourceDocumentType  varchar(50),
    @SourceDocumentId    bigint,
    @DocumentNumber      nvarchar(50),
    @DocumentDate        date,
    @Direction           tinyint,
    @PartnerId           int = NULL,
    @ExigibilityDate     date = NULL,
    @IsVatOnCollection   bit = 0,
    @Rows                nvarchar(max)
AS
BEGIN
    -- Randurile registrului de TVA ale unui document, odata cu nota lui (C4 3.1, decizia 2): JSON [{operationTypeCode, vatRateCode,
    -- vatRatePercent, baseRon, vatRon}], cu datele partenerului copiate acum. Factura cu TVA la incasare nu are perioada (neexigibila).
    SET NOCOUNT ON;

    INSERT INTO fin.VatEntry (CompanyId, EntryId, SourceDocumentType, SourceDocumentId, DocumentNumber, DocumentDate, Direction, PartnerId, PartnerFiscalCode,
        PartnerCountryCode, PartnerName, OperationTypeCode, VatRateCode, VatRatePercent, BaseRon, VatRon, ExigibilityDate, PeriodYear, PeriodMonth, IsVatOnCollection,
        IsReversal)
    SELECT @CompanyId, @EntryId, @SourceDocumentType, @SourceDocumentId, ISNULL(@DocumentNumber, N''), @DocumentDate, @Direction, @PartnerId, p.FiscalCode,
        p.CountryCode, p.Name, j.OperationTypeCode, j.VatRateCode, j.VatRatePercent, j.BaseRon, j.VatRon,
        CASE WHEN @IsVatOnCollection = 1 THEN NULL ELSE @ExigibilityDate END,
        CASE WHEN @IsVatOnCollection = 1 THEN NULL ELSE YEAR(@ExigibilityDate) END, CASE WHEN @IsVatOnCollection = 1 THEN NULL ELSE MONTH(@ExigibilityDate) END,
        @IsVatOnCollection, CASE WHEN j.VatRon < 0 OR j.BaseRon < 0 THEN 1 ELSE 0 END
    FROM OPENJSON(@Rows) WITH (OperationTypeCode nvarchar(20) '$.operationTypeCode', VatRateCode nvarchar(10) '$.vatRateCode',
        VatRatePercent decimal(5,2) '$.vatRatePercent', BaseRon decimal(19,2) '$.baseRon', VatRon decimal(19,2) '$.vatRon') AS j
    LEFT JOIN core.Partner AS p ON p.Id = @PartnerId;
END;
