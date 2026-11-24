CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_SavePartnerLines
    @CompanyId  int,
    @Id         int,
    @Lines      nvarchar(max)
AS
BEGIN
    -- Liniile pe partener ale unei declaratii informative (C4 4.2), rescrise la fiecare calcul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM fin.TaxReturn WHERE Id = @Id AND CompanyId = @CompanyId AND Status < 4)
        THROW 51947, N'Declarația e depusă; o corecție se face prin rectificativă.', 1;
    BEGIN TRANSACTION;
    DELETE FROM fin.TaxReturnPartnerLine WHERE ReturnId = @Id;
    INSERT INTO fin.TaxReturnPartnerLine (ReturnId, Section, PartnerId, PartnerFiscalCode, PartnerCountryCode, PartnerName, VatRatePercent, Documents, BaseRon,
        VatRon, Issue)
    SELECT @Id, Section, PartnerId, PartnerFiscalCode, PartnerCountryCode, PartnerName, VatRatePercent, Documents, BaseRon, VatRon, Issue
    FROM OPENJSON(@Lines) WITH (Section varchar(5) '$.section', PartnerId int '$.partnerId', PartnerFiscalCode nvarchar(30) '$.partnerFiscalCode',
        PartnerCountryCode char(2) '$.partnerCountryCode', PartnerName nvarchar(200) '$.partnerName', VatRatePercent decimal(5,2) '$.vatRatePercent',
        Documents int '$.documents', BaseRon decimal(19,2) '$.baseRon', VatRon decimal(19,2) '$.vatRon', Issue nvarchar(200) '$.issue');
    COMMIT;
END;
