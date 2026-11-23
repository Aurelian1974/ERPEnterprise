CREATE OR ALTER PROCEDURE fin.usp_PartnerBalance_List
    @CompanyId       int,
    @AsOf            date,
    @Role            tinyint = NULL,
    @Search          nvarchar(100) = NULL,
    @OnlyOverdue     bit = 0,
    @OnlyUnallocated bit = 0,
    @BothRoles       bit = 0
AS
BEGIN
    -- Soldurile partenerilor la o data (02.02, F3 10 „Solduri parteneri”, macheta f3-01): partidele cu data documentului pana la @AsOf,
    -- refacute din alocarile de pana atunci. Pe partener si rol, in lei (cursul partidei): soldul, restanta (partidele de sensul rolului cu
    -- scadenta trecuta), vechimea pe intervale (in termen, 1–30, 31–60, peste 60 de zile), intarzierea maxima, platile nealocate. Intoarce
    -- 1) primele 500 de randuri (dupa sold, filtrate); 2) totalurile firmei (nefiltrate): creante, restante, datorii, nealocate, TVA neexigibil.
    SET NOCOUNT ON;

    DECLARE @Items TABLE (Id bigint PRIMARY KEY, PartnerId int, Role tinyint, Kind tinyint, DueDate date, OpenRon decimal(19,2), Natural bit);
    INSERT INTO @Items (Id, PartnerId, Role, Kind, DueDate, OpenRon, Natural)
    SELECT o.Id, o.PartnerId, o.Role, o.Kind, o.DueDate, x.OpenRon,
        CASE WHEN (o.Role = 1 AND x.OpenRon > 0) OR (o.Role = 2 AND x.OpenRon < 0) THEN 1 ELSE 0 END
    FROM fin.OpenItem AS o
    OUTER APPLY (SELECT SUM(a.DebitAmount) AS Allocated FROM fin.Allocation AS a
                 WHERE a.DebitOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS d
    OUTER APPLY (SELECT SUM(a.CreditAmount) AS Allocated FROM fin.Allocation AS a
                 WHERE a.CreditOpenItemId = o.Id AND a.AllocationDate <= @AsOf AND (a.Status = 1 OR CAST(a.UndoneAt AS date) > @AsOf)) AS c
    CROSS APPLY (SELECT ROUND((o.Amount - ISNULL(d.Allocated, 0) + ISNULL(c.Allocated, 0)) * o.ExchangeRate, 2) AS OpenRon) AS x
    WHERE o.CompanyId = @CompanyId AND o.DocumentDate <= @AsOf AND x.OpenRon <> 0;

    DECLARE @Rows TABLE (PartnerId int, Role tinyint, BalanceRon decimal(19,2), OverdueRon decimal(19,2), CurrentRon decimal(19,2), Days30Ron decimal(19,2),
                         Days60Ron decimal(19,2), Over60Ron decimal(19,2), MaxOverdueDays int, UnallocatedRon decimal(19,2), UnallocatedCount int,
                         PRIMARY KEY (PartnerId, Role));
    INSERT INTO @Rows
    SELECT i.PartnerId, i.Role, SUM(i.OpenRon),
        SUM(CASE WHEN i.Natural = 1 AND i.DueDate < @AsOf THEN i.OpenRon ELSE 0 END),
        SUM(CASE WHEN i.Natural = 1 AND i.DueDate >= @AsOf THEN i.OpenRon ELSE 0 END),
        SUM(CASE WHEN i.Natural = 1 AND DATEDIFF(day, i.DueDate, @AsOf) BETWEEN 1 AND 30 THEN i.OpenRon ELSE 0 END),
        SUM(CASE WHEN i.Natural = 1 AND DATEDIFF(day, i.DueDate, @AsOf) BETWEEN 31 AND 60 THEN i.OpenRon ELSE 0 END),
        SUM(CASE WHEN i.Natural = 1 AND DATEDIFF(day, i.DueDate, @AsOf) > 60 THEN i.OpenRon ELSE 0 END),
        ISNULL(MAX(CASE WHEN i.Natural = 1 AND i.DueDate < @AsOf THEN DATEDIFF(day, i.DueDate, @AsOf) END), 0),
        SUM(CASE WHEN i.Kind IN (5, 10) THEN i.OpenRon ELSE 0 END),
        SUM(CASE WHEN i.Kind IN (5, 10) THEN 1 ELSE 0 END)
    FROM @Items AS i
    GROUP BY i.PartnerId, i.Role;

    SELECT TOP (500) r.PartnerId, p.Code AS PartnerCode, p.Name AS PartnerName, p.FiscalCode, r.Role,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM @Rows AS o WHERE o.PartnerId = r.PartnerId AND o.Role <> r.Role) THEN 1 ELSE 0 END AS bit) AS HasOtherRole,
        r.BalanceRon, r.OverdueRon, r.CurrentRon, r.Days30Ron, r.Days60Ron, r.Over60Ron, r.MaxOverdueDays, r.UnallocatedRon, r.UnallocatedCount
    FROM @Rows AS r
    JOIN core.Partner AS p ON p.Id = r.PartnerId
    WHERE (@Role IS NULL OR r.Role = @Role)
      AND (@OnlyOverdue = 0 OR r.OverdueRon <> 0)
      AND (@OnlyUnallocated = 0 OR r.UnallocatedCount > 0)
      AND (@BothRoles = 0 OR EXISTS (SELECT 1 FROM @Rows AS o WHERE o.PartnerId = r.PartnerId AND o.Role <> r.Role))
      AND (@Search IS NULL OR p.NameSearch LIKE N'%' + @Search + N'%' OR p.Code LIKE @Search + N'%' OR p.FiscalCode LIKE N'%' + @Search + N'%')
    ORDER BY ABS(r.BalanceRon) DESC, p.Name, r.Role;

    -- TVA neexigibil la data: TVA-ul facturilor cu TVA la incasare minus ce a devenit exigibil pana atunci (F3 3.7).
    DECLARE @VatCollected decimal(19,2), @VatDeductible decimal(19,2);
    SELECT @VatCollected = ISNULL(SUM(CASE WHEN o.Role = 1 THEN v.Remaining END), 0), @VatDeductible = ISNULL(SUM(CASE WHEN o.Role = 2 THEN v.Remaining END), 0)
    FROM fin.OpenItem AS o
    CROSS APPLY (SELECT ABS(o.VatAmountRon) - ISNULL((SELECT SUM(e.VatAmountRon) FROM fin.VatCashEntry AS e
                                                      WHERE e.OpenItemId = o.Id AND e.ExigibilityDate <= @AsOf), 0) AS Remaining) AS v
    WHERE o.CompanyId = @CompanyId AND o.IsVatOnCollection = 1 AND o.Kind IN (1, 3, 4) AND o.DocumentDate <= @AsOf;

    SELECT ISNULL(SUM(CASE WHEN Role = 1 AND BalanceRon > 0 THEN BalanceRon END), 0) AS ReceivablesRon,
        SUM(CASE WHEN Role = 1 AND BalanceRon > 0 THEN 1 ELSE 0 END) AS CustomersWithBalance,
        ISNULL(SUM(CASE WHEN Role = 1 THEN OverdueRon END), 0) AS OverdueReceivablesRon,
        SUM(CASE WHEN Role = 1 AND OverdueRon > 0 THEN 1 ELSE 0 END) AS CustomersOverdue,
        ISNULL(-SUM(CASE WHEN Role = 2 AND BalanceRon < 0 THEN BalanceRon END), 0) AS PayablesRon,
        SUM(CASE WHEN Role = 2 AND BalanceRon < 0 THEN 1 ELSE 0 END) AS SuppliersWithBalance,
        ISNULL(SUM(ABS(UnallocatedRon)), 0) AS UnallocatedRon, ISNULL(SUM(UnallocatedCount), 0) AS UnallocatedCount,
        @VatCollected AS VatNonExigibleCollectedRon, @VatDeductible AS VatNonExigibleDeductibleRon
    FROM @Rows;
END;
