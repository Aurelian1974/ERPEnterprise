CREATE OR ALTER PROCEDURE fin.usp_OpenItem_AsOf
    @CompanyId int,
    @PartnerId int,
    @Role      tinyint,
    @AsOf      date
AS
BEGIN
    -- Partidele deschise ale unui partener la o data trecuta (02.02, F3 7, F3-A12; PDF-ul confirmarii de sold): partidele cu data documentului
    -- pana la @AsOf si restul lor refacut din alocarile de pana atunci (cele desfacute dupa @AsOf inca numara).
    SET NOCOUNT ON;

    SELECT o.Id AS OpenItemId, o.Kind, o.DocumentNo, o.DocumentDate, o.DueDate, o.CurrencyCode, o.Amount, x.OpenAsOf, ROUND(x.OpenAsOf * o.ExchangeRate, 2) AS OpenRon
    FROM fin.OpenItem AS o
    OUTER APPLY (SELECT SUM(a.DebitAmount) AS Allocated FROM fin.Allocation AS a
                 WHERE a.DebitOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS d
    OUTER APPLY (SELECT SUM(a.CreditAmount) AS Allocated FROM fin.Allocation AS a
                 WHERE a.CreditOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS c
    CROSS APPLY (SELECT o.Amount - ISNULL(d.Allocated, 0) + ISNULL(c.Allocated, 0) AS OpenAsOf) AS x
    WHERE o.CompanyId = @CompanyId AND o.PartnerId = @PartnerId AND o.Role = @Role AND o.DocumentDate <= @AsOf AND x.OpenAsOf <> 0
    ORDER BY o.DueDate, o.DocumentDate, o.Id;
END;
