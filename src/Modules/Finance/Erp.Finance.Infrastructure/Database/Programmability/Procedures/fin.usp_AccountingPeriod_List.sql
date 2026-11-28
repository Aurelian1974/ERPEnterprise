CREATE OR ALTER PROCEDURE fin.usp_AccountingPeriod_List
    @CompanyId int
AS
BEGIN
    -- Anii financiari ai firmei si lunile lor (C3 11, macheta c3-01), cel mai nou an primul.
    SET NOCOUNT ON;

    SELECT y.Id, y.[Year], y.StartDate, y.EndDate, y.Status, y.VatPeriodicity, y.ClosedAt, y.LockedAt, y.LockReason, y.RowVersion
    FROM fin.FiscalYear AS y
    WHERE y.CompanyId = @CompanyId
    ORDER BY y.[Year] DESC;

    SELECT p.Id, p.FiscalYearId, p.PeriodNo, p.StartDate, p.EndDate, p.Status, p.IsImported, p.SoftClosedAt, sc.DisplayName AS SoftClosedByName,
           p.ClosedAt, c.DisplayName AS ClosedByName, p.RowVersion
    FROM fin.AccountingPeriod AS p
    LEFT JOIN core.[User] AS sc ON sc.Id = p.SoftClosedBy
    LEFT JOIN core.[User] AS c ON c.Id = p.ClosedBy
    WHERE p.CompanyId = @CompanyId
    ORDER BY p.StartDate;
END;
