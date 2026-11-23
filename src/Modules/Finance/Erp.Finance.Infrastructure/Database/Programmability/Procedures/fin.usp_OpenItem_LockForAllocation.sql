CREATE OR ALTER PROCEDURE fin.usp_OpenItem_LockForAllocation
    @CompanyId int,
    @Ids       nvarchar(max)
AS
BEGIN
    -- Partidele unui lot de alocari, blocate in ordinea Id-urilor (SP-16) in tranzactia apelantului, cu restul si valoarea lui contabila in
    -- lei, pentru calculul diferentelor de curs (F4 4.1), si regula de curs incrucisat din profilul partenerului (F4 3.3).
    SET NOCOUNT ON;

    SELECT o.Id, o.PartnerId, o.Role, o.Kind, o.CurrencyCode, o.ExchangeRate, o.OpenAmount, o.OpenAmountRon, o.DocumentDate,
        ISNULL(p.CrossCurrencyRule, 1) AS CrossCurrencyRule, ISNULL(p.CrossCurrencyMarkupPercent, 0) AS CrossCurrencyMarkupPercent
    FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    LEFT JOIN fin.PartnerFinanceProfile AS p ON p.CompanyId = o.CompanyId AND p.PartnerId = o.PartnerId AND p.Role = o.Role
    WHERE o.CompanyId = @CompanyId AND o.Id IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(@Ids) AS j)
    ORDER BY o.Id;
END;
