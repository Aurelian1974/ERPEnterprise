CREATE OR ALTER PROCEDURE fin.usp_VatRegister_ByPartner
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Datele D394 si D390 ale unei perioade (C4 4.2): registrul exigibil in perioada, pe partener (cod fiscal, tara), tip de operatiune si cota,
    -- cu numarul de documente distincte.
    SET NOCOUNT ON;
    SELECT v.PartnerId, v.PartnerFiscalCode, v.PartnerCountryCode, MAX(v.PartnerName) AS PartnerName, v.OperationTypeCode, v.VatRatePercent,
        COUNT(DISTINCT CONCAT(v.SourceDocumentType, '|', v.SourceDocumentId)) AS Documents, SUM(v.BaseRon) AS BaseRon, SUM(v.VatRon) AS VatRon
    FROM fin.VatEntry AS v
    WHERE v.CompanyId = @CompanyId AND v.ExigibilityDate BETWEEN @From AND @To
    GROUP BY v.PartnerId, v.PartnerFiscalCode, v.PartnerCountryCode, v.OperationTypeCode, v.VatRatePercent
    ORDER BY v.OperationTypeCode, v.PartnerFiscalCode;
END;
