CREATE OR ALTER PROCEDURE fin.usp_VatJournal_Get
    @CompanyId  int,
    @Year       smallint,
    @Month      tinyint,
    @Direction  tinyint
AS
BEGIN
    -- Jurnalul de vanzari (1) sau de cumparari (2) al unei luni (C4 3.1, 10): randurile exigibile in luna, apoi totalurile pe tip de operatiune.
    SET NOCOUNT ON;

    SELECT v.Id, v.DocumentNumber, v.DocumentDate, v.ExigibilityDate, v.PartnerName, v.PartnerFiscalCode, v.PartnerCountryCode, v.OperationTypeCode,
        v.VatRateCode, v.VatRatePercent, v.BaseRon, v.VatRon, v.IsVatOnCollection, v.IsReversal, v.EntryId
    FROM fin.VatEntry AS v
    WHERE v.CompanyId = @CompanyId AND v.PeriodYear = @Year AND v.PeriodMonth = @Month AND v.Direction = @Direction
    ORDER BY v.DocumentDate, v.DocumentNumber, v.Id;

    SELECT v.OperationTypeCode, ISNULL(t.Name, v.OperationTypeCode) AS Name, SUM(v.BaseRon) AS BaseRon, SUM(v.VatRon) AS VatRon, COUNT(*) AS Rows
    FROM fin.VatEntry AS v
    OUTER APPLY (SELECT TOP (1) o.Name FROM fin.VatOperationType AS o WHERE o.Code = v.OperationTypeCode ORDER BY o.ValidFrom DESC) AS t
    WHERE v.CompanyId = @CompanyId AND v.PeriodYear = @Year AND v.PeriodMonth = @Month AND v.Direction = @Direction
    GROUP BY v.OperationTypeCode, t.Name
    ORDER BY v.OperationTypeCode;
END;
