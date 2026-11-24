CREATE OR ALTER PROCEDURE fin.usp_PartnerPaymentBehavior_Recalculate
    @CompanyId     int,
    @AsOf          date,
    @HistoryMonths int,
    @UserId        int
AS
BEGIN
    -- Istoricul de plata pe intervale de intarziere (F5 3.4), pe client si pe firma (PartnerId NULL), dintr-o singura trecere pe seturi:
    -- alocarile active ale facturilor de client pe plati din ultimele @HistoryMonths luni; zilele de intarziere = data alocarii - scadenta,
    -- suma = valoarea in lei a facturii stinse. Randurile vechi se inlocuiesc.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC fin.usp_PaymentDelayBucket_Ensure @CompanyId = @CompanyId, @UserId = @UserId;

    DECLARE @From date = DATEADD(MONTH, -@HistoryMonths, @AsOf);
    CREATE TABLE #Paid (PartnerId int NOT NULL, OpenItemId bigint NOT NULL, DaysLate int NOT NULL, AmountRon decimal(19,2) NOT NULL, BucketId int NOT NULL);

    INSERT INTO #Paid (PartnerId, OpenItemId, DaysLate, AmountRon, BucketId)
    SELECT i.PartnerId, i.Id, d.DaysLate, a.AmountRonDebit, b.Id
    FROM fin.Allocation AS a
    JOIN fin.OpenItem AS i ON i.Id = a.DebitOpenItemId
    JOIN fin.OpenItem AS p ON p.Id = a.CreditOpenItemId
    CROSS APPLY (SELECT DATEDIFF(DAY, i.DueDate, a.AllocationDate) AS DaysLate) AS d
    CROSS APPLY (SELECT TOP (1) x.Id FROM fin.PaymentDelayBucket AS x
                 WHERE x.CompanyId = @CompanyId AND (x.DaysFrom IS NULL OR d.DaysLate >= x.DaysFrom) AND (x.DaysTo IS NULL OR d.DaysLate <= x.DaysTo)
                 ORDER BY x.SortOrder) AS b
    WHERE a.CompanyId = @CompanyId AND a.Status = 1 AND a.AllocationDate > @From AND a.AllocationDate <= @AsOf
      AND i.Role = 1 AND i.Kind IN (1, 3, 4, 6) AND p.Kind = 5;

    BEGIN TRANSACTION;

    DELETE FROM fin.PartnerPaymentBehavior WHERE CompanyId = @CompanyId;

    INSERT INTO fin.PartnerPaymentBehavior (CompanyId, PartnerId, SampleInvoices, SampleAmountRon, AvgDaysLate, CalculatedAt)
    SELECT @CompanyId, PartnerId, COUNT(DISTINCT OpenItemId), SUM(AmountRon),
           CAST(ISNULL(SUM(AmountRon * DaysLate) / NULLIF(SUM(AmountRon), 0), 0) AS decimal(9,2)), SYSUTCDATETIME()
    FROM #Paid
    GROUP BY GROUPING SETS ((PartnerId), ());

    INSERT INTO fin.PartnerPaymentBehaviorBucket (BehaviorId, BucketId, AmountShare, AvgDaysLate)
    SELECT h.Id, b.Id, CAST(ISNULL(SUM(p.AmountRon), 0) / h.SampleAmountRon AS decimal(9,6)),
           CAST(ISNULL(SUM(p.AmountRon * p.DaysLate) / NULLIF(SUM(p.AmountRon), 0), 0) AS decimal(9,2))
    FROM fin.PartnerPaymentBehavior AS h
    JOIN fin.PaymentDelayBucket AS b ON b.CompanyId = h.CompanyId
    LEFT JOIN #Paid AS p ON p.BucketId = b.Id AND (h.PartnerId IS NULL OR p.PartnerId = h.PartnerId)
    WHERE h.CompanyId = @CompanyId AND h.SampleAmountRon > 0
    GROUP BY h.Id, b.Id, h.SampleAmountRon;

    COMMIT TRANSACTION;

    SELECT COUNT(*) AS Partners FROM fin.PartnerPaymentBehavior WHERE CompanyId = @CompanyId AND PartnerId IS NOT NULL;
END;
