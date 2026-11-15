CREATE OR ALTER PROCEDURE core.usp_Company_Get
    @Id int
AS
BEGIN
    -- Fisa firmei (macheta 03, tab-urile Date generale si Conturi bancare). Dreptul se verifica in aplicatie.
    SET NOCOUNT ON;

    SELECT c.Id, c.GroupId, c.Code, c.Name, c.FiscalCode, c.VatPayer, c.VatOnCollection, c.TradeRegisterNo, c.ShareCapital,
           c.Address, c.City, c.CountyCode, c.CountryCode, c.PostalCode, c.FunctionalCurrency, c.DefaultPrecisionPolicyId,
           c.IncludeInConsolidation, c.IsActive, c.ModifiedAt, COALESCE(m.DisplayName, cr.DisplayName) AS ModifiedByName,
           COALESCE(c.ModifiedAt, c.CreatedAt) AS LastChangedAt, c.RowVersion
    FROM core.Company AS c
    JOIN core.[User] AS cr ON cr.Id = c.CreatedBy
    LEFT JOIN core.[User] AS m ON m.Id = c.ModifiedBy
    WHERE c.Id = @Id;

    SELECT a.Id, a.CompanyId, a.Iban, a.BankName, a.Currency, a.IsDefault, a.IsActive, a.RowVersion
    FROM core.CompanyBankAccount AS a
    WHERE a.CompanyId = @Id
    ORDER BY a.IsActive DESC, a.IsDefault DESC, a.Currency, a.Id;
END;
