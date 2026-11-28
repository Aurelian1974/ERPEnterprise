CREATE OR ALTER PROCEDURE fin.usp_AccountingPeriod_History
    @CompanyId  int,
    @Id         int
AS
BEGIN
    -- Istoricul starilor unei luni (C3 3.2): inchideri si redeschideri, cu motiv, cine si cand.
    SET NOCOUNT ON;

    SELECT h.FromStatus, h.ToStatus, h.Reason, h.ChangedAt, u.DisplayName AS ChangedByName
    FROM fin.PeriodStatusHistory AS h
    JOIN fin.AccountingPeriod AS p ON p.Id = h.PeriodId
    LEFT JOIN core.[User] AS u ON u.Id = h.ChangedBy
    WHERE h.PeriodId = @Id AND p.CompanyId = @CompanyId
    ORDER BY h.ChangedAt DESC, h.Id DESC;
END;
