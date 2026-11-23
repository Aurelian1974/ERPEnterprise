CREATE OR ALTER PROCEDURE fin.usp_PartnerBalance_Get
    @CompanyId int,
    @PartnerId int,
    @Role      tinyint = NULL,
    @AsOf      date
AS
BEGIN
    -- Soldul unui partener la o data (02.02, F3 2 regula 1, 6, IPartnerBalanceQueries.GetBalanceAsync): suma resturilor de plata ale partidelor
    -- cu data documentului pana la @AsOf, refacute din alocarile de pana la acea data (cele desfacute dupa @AsOf inca numara). Pe rol si
    -- moneda: soldul, in lei (la cursul partidei), restanta (partidele de sensul rolului cu scadenta trecuta: debit la client, credit la
    -- furnizor), vechimea maxima in zile si platile nealocate (incasari, plati, plati prin angajat).
    SET NOCOUNT ON;

    WITH Items AS (
        SELECT o.Id, o.Role, o.Kind, o.CurrencyCode, o.ExchangeRate, o.DueDate,
            o.Amount - ISNULL(d.Allocated, 0) + ISNULL(c.Allocated, 0) AS OpenAsOf
        FROM fin.OpenItem AS o
        OUTER APPLY (SELECT SUM(a.DebitAmount) AS Allocated FROM fin.Allocation AS a
                     WHERE a.DebitOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS d
        OUTER APPLY (SELECT SUM(a.CreditAmount) AS Allocated FROM fin.Allocation AS a
                     WHERE a.CreditOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS c
        WHERE o.CompanyId = @CompanyId AND o.PartnerId = @PartnerId AND (@Role IS NULL OR o.Role = @Role) AND o.DocumentDate <= @AsOf)
    SELECT @CompanyId AS CompanyId, @PartnerId AS PartnerId, i.Role, i.CurrencyCode,
        SUM(i.OpenAsOf) AS Balance,
        SUM(ROUND(i.OpenAsOf * i.ExchangeRate, 2)) AS BalanceRon,
        SUM(CASE WHEN i.DueDate < @AsOf AND ((i.Role = 1 AND i.OpenAsOf > 0) OR (i.Role = 2 AND i.OpenAsOf < 0)) THEN ROUND(i.OpenAsOf * i.ExchangeRate, 2) ELSE 0 END)
            AS OverdueRon,
        ISNULL(MAX(CASE WHEN i.DueDate < @AsOf AND ((i.Role = 1 AND i.OpenAsOf > 0) OR (i.Role = 2 AND i.OpenAsOf < 0)) THEN DATEDIFF(day, i.DueDate, @AsOf) END), 0)
            AS MaxOverdueDays,
        SUM(CASE WHEN i.Kind IN (5, 10) THEN ROUND(i.OpenAsOf * i.ExchangeRate, 2) ELSE 0 END) AS UnallocatedPaymentsRon
    FROM Items AS i
    GROUP BY i.Role, i.CurrencyCode
    HAVING SUM(CASE WHEN i.OpenAsOf <> 0 THEN 1 ELSE 0 END) > 0
    ORDER BY i.Role, i.CurrencyCode;
END;
