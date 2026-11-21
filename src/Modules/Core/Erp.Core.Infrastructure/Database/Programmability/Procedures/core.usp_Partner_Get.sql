CREATE OR ALTER PROCEDURE core.usp_Partner_Get
    @Id int
AS
BEGIN
    -- Fisa partenerului (macheta 04): datele generale, adresele, conturile bancare, contactele si valorile proprietatilor.
    -- CNP-ul nu se intoarce aici; doar faptul ca exista (citirea lui e separata, cu permisiune si jurnalizata).
    SET NOCOUNT ON;

    SELECT p.Id, p.Code, p.Name, p.PartnerType, p.FiscalCode, CAST(CASE WHEN p.PersonalIdEncrypted IS NULL THEN 0 ELSE 1 END AS bit) AS HasPersonalId,
           p.IsCustomer, p.IsSupplier, p.PartnerGroupId, p.VatPayer, p.VatOnCollection, p.AnafInactive, p.AnafCheckedAt, p.TradeRegisterNo,
           p.Address, p.City, p.CitySirutaCode, p.CountyCode, p.CountryCode, p.PostalCode, p.Phone, p.Email, p.IntraGroupCompanyId, p.IsActive,
           p.CreatedAt, COALESCE(m.DisplayName, cr.DisplayName) AS ModifiedByName, COALESCE(p.ModifiedAt, p.CreatedAt) AS LastChangedAt, p.RowVersion,
           p.OwnerCompanyId, p.OwnerBranchId, p.OwnerLocationId,
           CASE WHEN p.OwnerLocationId IS NOT NULL THEN (SELECT x.Name FROM core.Location AS x WHERE x.Id = p.OwnerLocationId)
                WHEN p.OwnerBranchId IS NOT NULL THEN (SELECT x.Name FROM core.Branch AS x WHERE x.Id = p.OwnerBranchId)
                WHEN p.OwnerCompanyId IS NOT NULL THEN (SELECT x.Name FROM core.Company AS x WHERE x.Id = p.OwnerCompanyId) END AS OwnerName
    FROM core.Partner AS p
    JOIN core.[User] AS cr ON cr.Id = p.CreatedBy
    LEFT JOIN core.[User] AS m ON m.Id = p.ModifiedBy
    WHERE p.Id = @Id;

    SELECT a.Id, a.PartnerId, a.AddressType, a.Name, a.Address, a.City, a.CitySirutaCode, a.CountyCode, a.CountryCode, a.PostalCode, a.Phone, a.Email,
           a.IsDefault, a.IsActive, a.RowVersion
    FROM core.PartnerAddress AS a
    WHERE a.PartnerId = @Id
    ORDER BY a.IsActive DESC, a.AddressType, a.IsDefault DESC, a.Id;

    SELECT b.Id, b.PartnerId, b.Iban, b.BankName, b.Currency, b.IsDefault, b.IsActive, b.RowVersion
    FROM core.PartnerBankAccount AS b
    WHERE b.PartnerId = @Id
    ORDER BY b.IsActive DESC, b.IsDefault DESC, b.Currency, b.Id;

    SELECT k.Id, k.PartnerId, k.Name, k.Role, k.Phone, k.Email, k.Notes, k.IsActive, k.RowVersion
    FROM core.PartnerContact AS k
    WHERE k.PartnerId = @Id
    ORDER BY k.IsActive DESC, k.Name, k.Id;

    SELECT v.PropertyDefinitionId, v.ValueText, v.ValueNumber, v.ValueDate, v.ValueBool, v.ValueOptionId, v.ValueUserId
    FROM core.PropertyValue AS v
    JOIN core.PropertyDefinition AS d ON d.Id = v.PropertyDefinitionId AND d.EntityType = 'Partner'
    WHERE v.EntityId = @Id
    ORDER BY v.PropertyDefinitionId, v.Id;
END;
