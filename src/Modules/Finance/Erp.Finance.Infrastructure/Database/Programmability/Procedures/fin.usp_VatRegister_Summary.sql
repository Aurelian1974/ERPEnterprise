CREATE OR ALTER PROCEDURE fin.usp_VatRegister_Summary
    @CompanyId  int,
    @From       date,
    @To         date
AS
BEGIN
    -- Datele D300 ale unei perioade (C4 3.4): registrul de TVA exigibil in perioada, pe sens si tip de operatiune, cu TVA-ul deductibil (dupa
    -- dreptul de deducere al tipului si procentul de deducere al randului); apoi amprenta datelor (C4 3.3: generarea verifica ca nu s-au schimbat).
    SET NOCOUNT ON;

    SELECT v.Direction, v.OperationTypeCode, ISNULL(t.Name, v.OperationTypeCode) AS Name, SUM(v.BaseRon) AS BaseRon, SUM(v.VatRon) AS VatRon,
        SUM(CASE WHEN ISNULL(t.IsDeductible, 1) = 1 THEN ROUND(v.VatRon * v.DeductionPercent / 100, 2) ELSE 0 END) AS DeductibleRon
    FROM fin.VatEntry AS v
    OUTER APPLY (SELECT TOP (1) o.Name, o.IsDeductible FROM fin.VatOperationType AS o WHERE o.Code = v.OperationTypeCode ORDER BY o.ValidFrom DESC) AS t
    WHERE v.CompanyId = @CompanyId AND v.ExigibilityDate BETWEEN @From AND @To
    GROUP BY v.Direction, v.OperationTypeCode, t.Name, t.IsDeductible
    ORDER BY v.Direction, v.OperationTypeCode;

    SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(Id, BaseRon, VatRon, OperationTypeCode)), 0) AS Hash, COUNT(*) AS Rows
    FROM fin.VatEntry WHERE CompanyId = @CompanyId AND ExigibilityDate BETWEEN @From AND @To;
END;
