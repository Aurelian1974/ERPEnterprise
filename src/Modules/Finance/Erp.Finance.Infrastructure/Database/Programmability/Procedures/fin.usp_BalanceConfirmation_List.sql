CREATE OR ALTER PROCEDURE fin.usp_BalanceConfirmation_List
    @CompanyId int,
    @BatchId   int = NULL
AS
BEGIN
    -- Loturile de confirmari (cele mai noi intai, cu numaratorile pe stare) si confirmarile unui lot (implicit ultimul), pentru macheta f3-07.
    SET NOCOUNT ON;

    SELECT TOP (50) b.Id, b.Number, b.AsOfDate, b.Role, b.MinBalance, b.SentAt, COUNT(c.Id) AS Total,
        SUM(CASE WHEN c.ResponseStatus = 2 THEN 1 ELSE 0 END) AS Confirmed, SUM(CASE WHEN c.ResponseStatus = 3 THEN 1 ELSE 0 END) AS Contested,
        SUM(CASE WHEN c.ResponseStatus IN (0, 1, 4) THEN 1 ELSE 0 END) AS Unanswered
    FROM fin.BalanceConfirmationBatch AS b
    LEFT JOIN fin.BalanceConfirmation AS c ON c.BatchId = b.Id
    WHERE b.CompanyId = @CompanyId
    GROUP BY b.Id, b.Number, b.AsOfDate, b.Role, b.MinBalance, b.SentAt
    ORDER BY b.Id DESC;

    DECLARE @Batch int = ISNULL(@BatchId, (SELECT MAX(Id) FROM fin.BalanceConfirmationBatch WHERE CompanyId = @CompanyId));
    EXEC fin.usp_BalanceConfirmation_Get @CompanyId = @CompanyId, @BatchId = @Batch;
END;
