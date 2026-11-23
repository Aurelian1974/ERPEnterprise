CREATE OR ALTER PROCEDURE fin.usp_OpenItem_ForPaymentOrders
    @CompanyId int,
    @Ids       nvarchar(max)
AS
BEGIN
    -- Facturile de furnizor alese pentru ordine de plata (F2 „Din facturi scadente”, F3 macheta f3-04 „Creeaza OP-uri”): partida, partenerul,
    -- restul de plata (pozitiv), blocarile si litigiul, contul bancar activ al partenerului in moneda facturii (implicit intai).
    SET NOCOUNT ON;

    SELECT o.Id AS OpenItemId, o.PartnerId, p.Name AS PartnerName, o.Role, o.Kind, o.DocumentNo, o.CurrencyCode, -o.OpenAmount AS OpenAmount, o.IsDisputed,
        o.IsBlockedForPayment, CAST(ISNULL(pf.IsPaymentBlocked, 0) AS bit) AS PartnerBlocked,
        (SELECT TOP (1) b.Id FROM core.PartnerBankAccount AS b WHERE b.PartnerId = o.PartnerId AND b.IsActive = 1 AND b.Currency = o.CurrencyCode
         ORDER BY b.IsDefault DESC, b.Id) AS PartnerBankAccountId
    FROM fin.OpenItem AS o
    JOIN core.Partner AS p ON p.Id = o.PartnerId
    LEFT JOIN fin.PartnerFinanceProfile AS pf ON pf.CompanyId = o.CompanyId AND pf.PartnerId = o.PartnerId AND pf.Role = o.Role
    WHERE o.CompanyId = @CompanyId AND o.Id IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(@Ids) AS j)
    ORDER BY p.Name, o.DueDate, o.Id;
END;
