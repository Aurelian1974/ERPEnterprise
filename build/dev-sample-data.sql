-- Datele demonstrative ale bazei de DEZVOLTARE (build/dev-database.ps1 -SampleData), dupa machetele 02.01 (03-firme, 07c):
-- Grupul Exemplu cu 2 grupuri, 4 firme, 5 puncte de lucru, 12 locatii, conturi bancare si seriile firmei Exemplu Distributie.
-- Sterge intai TOATA structura organizatorica existenta (firme, grupuri, serii, conturi, rolurile pe firme si intrarile de
-- audit/outbox ale firmelor). Numai pentru ERP_DEV: nu se livreaza in kit si nu se ruleaza pe o baza de client.
-- Datele noi trec prin procedurile de salvare ale aplicatiei (aceleasi reguli); CUI-urile si IBAN-urile sunt valide.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'ERP[_]DEV%'
    THROW 59999, N'Datele demonstrative se încarcă doar într-o bază de dezvoltare ERP_DEV*.', 1;

BEGIN TRANSACTION;

-- ---------- Curatenie ----------
-- Auditul e imutabil (SP-21); in baza de dezvoltare se sterg doar intrarile structurii, cu triggerul oprit in tranzactie.
DISABLE TRIGGER core.tr_AuditEntry_Immutable ON core.AuditEntry;
DELETE FROM core.AuditEntry
WHERE CompanyId IS NOT NULL
   OR EntityType IN (N'Core.CompanyGroup', N'Core.Company', N'Core.Branch', N'Core.Location', N'Core.CompanyBankAccount', N'Core.NumberSeries');
ENABLE TRIGGER core.tr_AuditEntry_Immutable ON core.AuditEntry;

DELETE FROM core.Outbox WHERE CompanyId IS NOT NULL;
UPDATE core.[User] SET LastCompanyId = NULL WHERE LastCompanyId IS NOT NULL;
DELETE FROM core.UserRoleAssignment WHERE CompanyId IS NOT NULL;
-- Datele legate de firme (3.6 si celelalte etape ale Nucleului), ca reincarcarea sa mearga si pe o baza folosita.
DELETE FROM core.DefaultDimension;
DELETE FROM core.DimensionCombination;
UPDATE core.DimensionValue SET ParentId = NULL WHERE ParentId IS NOT NULL;
DELETE FROM core.DimensionValue;
DELETE FROM core.PartnerCredit;
DELETE FROM core.NumberSeriesRule;
DELETE FROM core.CompanyVersion;
DELETE FROM core.CompanyContact;
DELETE FROM core.CompanyAddress;
UPDATE core.Partner SET OwnerCompanyId = NULL, OwnerBranchId = NULL, OwnerLocationId = NULL WHERE OwnerCompanyId IS NOT NULL;
UPDATE core.Item SET OwnerCompanyId = NULL, OwnerBranchId = NULL, OwnerLocationId = NULL WHERE OwnerCompanyId IS NOT NULL;
DELETE FROM core.NumberSeries;
DELETE FROM core.CompanyBankAccount;
DELETE FROM core.Location;
DELETE FROM core.Branch;
UPDATE core.CompanyGroup SET ParentCompanyId = NULL, ParentGroupId = NULL;
DELETE FROM core.Company;
DELETE FROM core.CompanyGroup;
DBCC CHECKIDENT ('core.CompanyGroup', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.Company', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.Branch', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.Location', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.CompanyBankAccount', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('core.NumberSeries', RESEED, 0) WITH NO_INFOMSGS;

-- ---------- Structura (macheta 03) ----------
DECLARE @U int = COALESCE((SELECT Id FROM core.[User] WHERE UserName = N'administrator'), 1);
DECLARE @Policy int = (SELECT Id FROM core.PrecisionPolicy WHERE Code = N'STANDARD');
DECLARE @R TABLE (Id int, RowVersion binary(8));
DECLARE @Grup int, @Subgrup int, @Holding int, @Distributie int, @Productie int, @Construct int;
DECLARE @PlCluj int, @PlTimisoara int, @PlBrasov int, @PlSibiu int, @PlArad int;

INSERT INTO @R EXEC core.usp_CompanyGroup_Save @Code = N'GRP', @Name = N'Grupul Exemplu', @IsActive = 1, @UserId = @U;
SELECT @Grup = Id FROM @R; DELETE FROM @R;
INSERT INTO @R EXEC core.usp_CompanyGroup_Save @Code = N'SUBCONS', @Name = N'Subgrup Construcții', @ParentGroupId = @Grup, @IsActive = 1, @UserId = @U;
SELECT @Subgrup = Id FROM @R; DELETE FROM @R;

INSERT INTO @R EXEC core.usp_Company_Save @GroupId = @Grup, @Code = N'EXH', @Name = N'Exemplu Holding SA', @FiscalCode = N'10000008',
    @VatPayer = 1, @VatOnCollection = 0, @TradeRegisterNo = N'J40/1000/2010', @ShareCapital = 1000000.00, @Address = N'Bd. Unirii nr. 1',
    @City = N'Sector 3', @CountyCode = N'B', @CountryCode = 'RO', @PostalCode = N'030167', @DefaultPrecisionPolicyId = @Policy,
    @IncludeInConsolidation = 1, @IsActive = 1, @UserId = @U;
SELECT @Holding = Id FROM @R; DELETE FROM @R;

INSERT INTO @R EXEC core.usp_Company_Save @GroupId = @Grup, @Code = N'EXD', @Name = N'Exemplu Distribuție SRL', @FiscalCode = N'12345674',
    @VatPayer = 1, @VatOnCollection = 0, @TradeRegisterNo = N'J40/1234/2015', @ShareCapital = 200.00, @Address = N'Str. Exemplului nr. 10, et. 2',
    @City = N'Sector 1', @CountyCode = N'B', @CountryCode = 'RO', @PostalCode = N'010101', @DefaultPrecisionPolicyId = @Policy,
    @IncludeInConsolidation = 1, @IsActive = 1, @UserId = @U;
SELECT @Distributie = Id FROM @R; DELETE FROM @R;

INSERT INTO @R EXEC core.usp_Company_Save @GroupId = @Grup, @Code = N'EXP', @Name = N'Exemplu Producție SRL', @FiscalCode = N'23456783',
    @VatPayer = 1, @VatOnCollection = 0, @TradeRegisterNo = N'J08/567/2016', @ShareCapital = 50000.00, @Address = N'Str. Fabricii nr. 5',
    @City = N'Brașov', @CountyCode = N'BV', @CountryCode = 'RO', @PostalCode = N'500001', @DefaultPrecisionPolicyId = @Policy,
    @IncludeInConsolidation = 1, @IsActive = 1, @UserId = @U;
SELECT @Productie = Id FROM @R; DELETE FROM @R;

INSERT INTO @R EXEC core.usp_Company_Save @GroupId = @Subgrup, @Code = N'EXC', @Name = N'Exemplu Construct SRL', @FiscalCode = N'34567892',
    @VatPayer = 1, @VatOnCollection = 1, @TradeRegisterNo = N'J22/890/2018', @ShareCapital = 10000.00, @Address = N'Str. Palat nr. 3',
    @City = N'Iași', @CountyCode = N'IS', @CountryCode = 'RO', @PostalCode = N'700032', @DefaultPrecisionPolicyId = @Policy,
    @IncludeInConsolidation = 1, @IsActive = 1, @UserId = @U;
SELECT @Construct = Id FROM @R; DELETE FROM @R;

-- Firma-mama a grupului (consolidare): Exemplu Holding SA.
DECLARE @GrupVersion binary(8) = (SELECT RowVersion FROM core.CompanyGroup WHERE Id = @Grup);
INSERT INTO @R EXEC core.usp_CompanyGroup_Save @Id = @Grup, @Code = N'GRP', @Name = N'Grupul Exemplu', @ParentCompanyId = @Holding,
    @IsActive = 1, @RowVersion = @GrupVersion, @UserId = @U;
DELETE FROM @R;

-- Puncte de lucru (5)
INSERT INTO @R EXEC core.usp_Branch_Save @CompanyId = @Distributie, @Code = N'PL-CJ', @Name = N'Punct de lucru Cluj', @Address = N'Str. Memorandumului nr. 20',
    @City = N'Cluj-Napoca', @CountyCode = N'CJ', @CountryCode = 'RO', @PostalCode = N'400114', @IsActive = 1, @UserId = @U;
SELECT @PlCluj = Id FROM @R; DELETE FROM @R;
INSERT INTO @R EXEC core.usp_Branch_Save @CompanyId = @Distributie, @Code = N'PL-TM', @Name = N'Punct de lucru Timișoara', @Address = N'Bd. Republicii nr. 8',
    @City = N'Timișoara', @CountyCode = N'TM', @CountryCode = 'RO', @PostalCode = N'300159', @IsActive = 1, @UserId = @U;
SELECT @PlTimisoara = Id FROM @R; DELETE FROM @R;
INSERT INTO @R EXEC core.usp_Branch_Save @CompanyId = @Productie, @Code = N'PL-BV', @Name = N'Fabrica Brașov', @Address = N'Str. Industriilor nr. 12',
    @City = N'Brașov', @CountyCode = N'BV', @CountryCode = 'RO', @PostalCode = N'500269', @IsActive = 1, @UserId = @U;
SELECT @PlBrasov = Id FROM @R; DELETE FROM @R;
INSERT INTO @R EXEC core.usp_Branch_Save @CompanyId = @Productie, @Code = N'PL-SB', @Name = N'Punct de lucru Sibiu', @Address = N'Str. Ocnei nr. 4',
    @City = N'Sibiu', @CountyCode = N'SB', @CountryCode = 'RO', @PostalCode = N'550189', @IsActive = 1, @UserId = @U;
SELECT @PlSibiu = Id FROM @R; DELETE FROM @R;
INSERT INTO @R EXEC core.usp_Branch_Save @CompanyId = @Productie, @Code = N'PL-AR', @Name = N'Punct de lucru Arad', @Address = N'Bd. Revoluției nr. 50',
    @City = N'Arad', @CountyCode = N'AR', @CountryCode = 'RO', @PostalCode = N'310025', @IsActive = 1, @UserId = @U;
SELECT @PlArad = Id FROM @R; DELETE FROM @R;

-- Locatii (12): 1 birou, 2 depozit, 3 magazin, 4 fabrica, 5 santier, 6 punct de livrare
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Holding, @Code = N'SED-B', @Name = N'Sediu social București', @LocationType = 1, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Distributie, @BranchId = @PlCluj, @Code = N'DEP-CJ', @Name = N'Depozit Cluj', @LocationType = 2, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Distributie, @BranchId = @PlCluj, @Code = N'MAG-CJ', @Name = N'Magazin Cluj Centru', @LocationType = 3, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Distributie, @BranchId = @PlTimisoara, @Code = N'DEP-TM', @Name = N'Depozit Timișoara', @LocationType = 2, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Distributie, @BranchId = @PlTimisoara, @Code = N'MAG-TM', @Name = N'Magazin Timișoara Iulius', @LocationType = 3, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Distributie, @BranchId = @PlTimisoara, @Code = N'LIV-TM', @Name = N'Punct de livrare Timișoara Vest', @LocationType = 6, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Distributie, @Code = N'DEP-PH', @Name = N'Depozit central Ploiești', @LocationType = 2,
    @Address = N'DN1 km 60', @City = N'Ploiești', @CountyCode = N'PH', @CountryCode = 'RO', @PostalCode = N'100001', @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Productie, @BranchId = @PlBrasov, @Code = N'FAB-BV', @Name = N'Hala de producție Brașov', @LocationType = 4, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Productie, @BranchId = @PlBrasov, @Code = N'DEP-MP', @Name = N'Depozit materii prime Brașov', @LocationType = 2, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Productie, @Code = N'DEP-PF', @Name = N'Depozit produse finite Brașov', @LocationType = 2, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Construct, @Code = N'SAN-IS', @Name = N'Șantier Bloc A Iași', @LocationType = 5,
    @Address = N'Str. Moara de Vânt nr. 30', @City = N'Iași', @CountyCode = N'IS', @CountryCode = 'RO', @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_Location_Save @CompanyId = @Construct, @Code = N'SAN-BC', @Name = N'Șantier Hala Bacău', @LocationType = 5,
    @Address = N'Str. Narciselor nr. 2', @City = N'Bacău', @CountyCode = N'BC', @CountryCode = 'RO', @IsActive = 0, @UserId = @U;
DELETE FROM @R;

-- Conturi bancare (IBAN-uri valide mod 97, banci reale, conturi fictive)
INSERT INTO @R EXEC core.usp_CompanyBankAccount_Save @CompanyId = @Distributie, @Iban = 'RO16BTRLRONCRT0123456701', @BankName = N'Banca Transilvania', @Currency = 'RON', @IsDefault = 1, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_CompanyBankAccount_Save @CompanyId = @Distributie, @Iban = 'RO09BTRLEURCRT0123456703', @BankName = N'Banca Transilvania', @Currency = 'EUR', @IsDefault = 1, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_CompanyBankAccount_Save @CompanyId = @Holding, @Iban = 'RO12INGBRONCRT0123456704', @BankName = N'ING Bank', @Currency = 'RON', @IsDefault = 1, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_CompanyBankAccount_Save @CompanyId = @Productie, @Iban = 'RO28BRDERONCRT0123456705', @BankName = N'BRD - Groupe Société Générale', @Currency = 'RON', @IsDefault = 1, @IsActive = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_CompanyBankAccount_Save @CompanyId = @Construct, @Iban = 'RO17CECERONCRT0123456706', @BankName = N'CEC Bank', @Currency = 'RON', @IsDefault = 1, @IsActive = 1, @UserId = @U;
DELETE FROM @R;

-- Seriile 2026 (macheta 07c): firma Exemplu Distributie, plus seria de facturi a celorlalte firme. Nefolosite (numarul 1).
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Distributie, @DocumentTypeCode = N'Sales.Invoice', @Prefix = N'EXD', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 1, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Distributie, @BranchId = @PlCluj, @DocumentTypeCode = N'Sales.Invoice', @Prefix = N'EXC', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 1, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Distributie, @DocumentTypeCode = N'Sales.CreditNote', @Prefix = N'EXS', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 1, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Distributie, @DocumentTypeCode = N'Inventory.DeliveryNote', @Prefix = N'AV', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 0, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Distributie, @DocumentTypeCode = N'Finance.Receipt', @Prefix = N'CH', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 1, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Distributie, @DocumentTypeCode = N'Sales.Order', @Prefix = N'CC', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 0, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Distributie, @BranchId = @PlCluj, @DocumentTypeCode = N'Inventory.GoodsReceipt', @Prefix = N'NIR', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 0, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Holding, @DocumentTypeCode = N'Sales.Invoice', @Prefix = N'EXH', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 1, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Productie, @DocumentTypeCode = N'Sales.Invoice', @Prefix = N'EXP', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 1, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
INSERT INTO @R EXEC core.usp_NumberSeries_Save @CompanyId = @Construct, @DocumentTypeCode = N'Sales.Invoice', @Prefix = N'ECS', @Year = 2026, @NextNumber = 1, @Padding = 6, @IsFiscal = 1, @IsDefault = 1, @AutoRollover = 1, @UserId = @U;
DELETE FROM @R;

-- Administratorul lucreaza implicit pe Exemplu Distributie (ca in macheta).
UPDATE core.[User] SET LastCompanyId = @Distributie WHERE UserName = N'administrator';

COMMIT;

SELECT (SELECT COUNT(*) FROM core.CompanyGroup) AS Grupuri, (SELECT COUNT(*) FROM core.Company) AS Firme,
       (SELECT COUNT(*) FROM core.Branch) AS PuncteDeLucru, (SELECT COUNT(*) FROM core.Location) AS Locatii,
       (SELECT COUNT(*) FROM core.CompanyBankAccount) AS ConturiBancare, (SELECT COUNT(*) FROM core.NumberSeries) AS Serii;
