CREATE OR ALTER PROCEDURE fin.usp_GlAccountRole_Save
    @CompanyId  int,
    @Roles      nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Atribuirile de roluri ale firmei (C1 3.2), inlocuite in bloc: JSON [{role, accountId, partnerKind, currencyCode, validFrom, validTo}].
    -- Contul e o frunza activa a planului romanesc, permisa firmei (51806); cele implicite ale instalarii nu se ating.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @New TABLE (Role varchar(40), AccountId int, PartnerKind tinyint, CurrencyCode char(3), ValidFrom date, ValidTo date);
    INSERT INTO @New (Role, AccountId, PartnerKind, CurrencyCode, ValidFrom, ValidTo)
    SELECT Role, AccountId, PartnerKind, CurrencyCode, ISNULL(ValidFrom, '20000101'), ValidTo
    FROM OPENJSON(@Roles) WITH (Role varchar(40) '$.role', AccountId int '$.accountId', PartnerKind tinyint '$.partnerKind',
        CurrencyCode char(3) '$.currencyCode', ValidFrom date '$.validFrom', ValidTo date '$.validTo');

    IF EXISTS (SELECT 1 FROM @New AS n
               WHERE NOT EXISTS (SELECT 1 FROM fin.GlAccount AS a JOIN fin.ChartOfAccounts AS c ON c.Id = a.ChartId AND c.Kind = 1
                                 WHERE a.Id = n.AccountId AND a.IsPostable = 1 AND a.IsActive = 1 AND (a.CompanyId IS NULL OR a.CompanyId = @CompanyId)))
        THROW 51806, N'Rolul se atribuie unui cont analitic sau sintetic fără analitice, activ și permis firmei.', 1;

    BEGIN TRANSACTION;
    DELETE FROM fin.GlAccountRoleAssignment WHERE CompanyId = @CompanyId;
    INSERT INTO fin.GlAccountRoleAssignment (CompanyId, Role, AccountId, PartnerKind, CurrencyCode, ValidFrom, ValidTo, CreatedBy)
    SELECT @CompanyId, Role, AccountId, PartnerKind, CurrencyCode, ValidFrom, ValidTo, @UserId FROM @New;
    COMMIT TRANSACTION;
END;
