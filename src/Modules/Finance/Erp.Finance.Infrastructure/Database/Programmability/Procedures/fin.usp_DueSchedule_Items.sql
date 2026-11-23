CREATE OR ALTER PROCEDURE fin.usp_DueSchedule_Items
    @CompanyId int,
    @Role      tinyint,
    @Day       date = NULL,
    @Before    date = NULL
AS
BEGIN
    -- Partidele scadente intr-o zi (@Day) sau restante (scadenta inainte de @Before), pe rol (F3 10, macheta f3-04, detaliul zilei): factura,
    -- partenerul, restul (al ratei, daca are rate) si observatiile: litigiu, blocare la plata (factura sau furnizor), e-Factura, partener
    -- inactiv la ANAF, contul bancar implicit al partenerului (pentru OP).
    SET NOCOUNT ON;

    SELECT TOP (500) o.Id AS OpenItemId, o.PartnerId, p.Name AS PartnerName, p.AnafInactive, o.DocumentNo, o.DocumentDate, ISNULL(s.DueDate, o.DueDate) AS DueDate,
        o.CurrencyCode, ABS(ISNULL(s.OpenAmount, o.OpenAmount)) AS OpenAmount, ABS(ROUND(ISNULL(s.OpenAmount, o.OpenAmount) * o.ExchangeRate, 2)) AS OpenRon,
        o.IsDisputed, o.IsBlockedForPayment, CAST(ISNULL(pf.IsPaymentBlocked, 0) AS bit) AS PartnerBlocked, o.EInvoiceStatus,
        CAST(CASE WHEN p.CountryCode <> 'RO' OR p.PartnerType = 4 THEN 1 ELSE 0 END AS bit) AS SpvExempt,
        (SELECT TOP (1) b.Id FROM core.PartnerBankAccount AS b WHERE b.PartnerId = o.PartnerId AND b.IsActive = 1 AND b.Currency = o.CurrencyCode
         ORDER BY b.IsDefault DESC, b.Id) AS PartnerBankAccountId
    FROM fin.OpenItem AS o
    JOIN core.Partner AS p ON p.Id = o.PartnerId
    LEFT JOIN fin.OpenItemSchedule AS s ON s.OpenItemId = o.Id AND s.OpenAmount <> 0
    LEFT JOIN fin.PartnerFinanceProfile AS pf ON pf.CompanyId = o.CompanyId AND pf.PartnerId = o.PartnerId AND pf.Role = o.Role
    WHERE o.CompanyId = @CompanyId AND o.Role = @Role AND o.OpenAmount <> 0 AND o.Kind IN (1, 3, 4, 6)
      AND ((o.Role = 1 AND o.Amount > 0) OR (o.Role = 2 AND o.Amount < 0))
      AND ((@Day IS NOT NULL AND ISNULL(s.DueDate, o.DueDate) = @Day) OR (@Before IS NOT NULL AND ISNULL(s.DueDate, o.DueDate) < @Before))
    ORDER BY ISNULL(s.DueDate, o.DueDate), p.Name, o.Id;
END;
