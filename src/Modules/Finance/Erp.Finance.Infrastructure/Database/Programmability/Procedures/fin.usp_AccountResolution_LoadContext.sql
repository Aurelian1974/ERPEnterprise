CREATE OR ALTER PROCEDURE fin.usp_AccountResolution_LoadContext
    @CompanyId  int,
    @OnDate     date,
    @Partners   nvarchar(max)
AS
BEGIN
    -- Datele din care se gasesc conturile unei note (C2 3.4), intr-un singur apel: rolurile in vigoare (ale firmei inaintea celor ale
    -- instalarii), contul conturilor de trezorerie ale firmei, contul corespondent al tipurilor de operatiuni si de cheltuieli, tipul
    -- partenerilor notei (JSON [id]), modul de moneda al conturilor planului romanesc si cotele TVA (registrul de TVA, C4 3.1).
    SET NOCOUNT ON;

    SELECT r.Role, r.PartnerKind, r.CurrencyCode, r.CompanyId, a.Code
    FROM fin.GlAccountRoleAssignment AS r
    JOIN fin.GlAccount AS a ON a.Id = r.AccountId
    WHERE (r.CompanyId IS NULL OR r.CompanyId = @CompanyId) AND r.ValidFrom <= @OnDate AND (r.ValidTo IS NULL OR r.ValidTo >= @OnDate)
    ORDER BY CASE WHEN r.CompanyId IS NULL THEN 1 ELSE 0 END, CASE WHEN r.PartnerKind IS NULL THEN 1 ELSE 0 END,
             CASE WHEN r.CurrencyCode IS NULL THEN 1 ELSE 0 END, r.ValidFrom DESC;

    SELECT Id, GlAccountCode FROM fin.TreasuryAccount WHERE CompanyId = @CompanyId AND GlAccountCode IS NOT NULL;

    SELECT Code, CounterGlAccountCode FROM fin.TreasuryOperationType WHERE CounterGlAccountCode IS NOT NULL;

    SELECT Code, CounterGlAccountCode FROM fin.ExpenseType WHERE CounterGlAccountCode IS NOT NULL;

    SELECT p.Id, p.PartnerType FROM core.Partner AS p WHERE p.Id IN (SELECT CAST(value AS int) FROM OPENJSON(@Partners));

    SELECT a.Code, a.CurrencyMode
    FROM fin.GlAccount AS a JOIN fin.ChartOfAccounts AS c ON c.Id = a.ChartId AND c.Kind = 1
    WHERE a.CurrencyMode <> 0;

    SELECT Code, [Percent], Category, ValidFrom, ValidTo FROM core.VatRate;
END;
