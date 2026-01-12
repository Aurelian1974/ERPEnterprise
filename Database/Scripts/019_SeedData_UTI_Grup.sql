-- =============================================
-- ValyanERP - Seed Data: UTI Grup
-- Script: 019_SeedData_UTI_Grup.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Date inițiale pentru structura organizațională
--            bazate pe grupul UTI (date reprezentative)
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Inserare date UTI Grup ===';
GO

-- =============================================
-- 1. GRUP COMPANII - UTI GRUP
-- =============================================
DECLARE @GroupId UNIQUEIDENTIFIER = NEWID();
DECLARE @UTISystemsId UNIQUEIDENTIFIER = NEWID();
DECLARE @UTIInstalId UNIQUEIDENTIFIER = NEWID();
DECLARE @CobraSecurityId UNIQUEIDENTIFIER = NEWID();
DECLARE @UTIRetailId UNIQUEIDENTIFIER = NEWID();
DECLARE @UTIFacilityId UNIQUEIDENTIFIER = NEWID();
DECLARE @CertSignId UNIQUEIDENTIFIER = NEWID();

-- Insert Group
IF NOT EXISTS (SELECT 1 FROM [dbo].[CompanyGroups] WHERE [Denumire] = 'UTI Grup')
BEGIN
    INSERT INTO [dbo].[CompanyGroups] (
        [Id], [Denumire], [Descriere], [Website], [IsActive], [SortOrder]
    ) VALUES (
        @GroupId,
        N'UTI Grup',
        N'Holding românesc înființat în 2000, cu activități în securitate, construcții, IT și facility management. Peste 4000 de angajați și prezență internațională.',
        N'https://www.uti.eu.com',
        1,
        1
    );
    PRINT '✅ Grup UTI Grup inserat';
END
ELSE
BEGIN
    SELECT @GroupId = [Id] FROM [dbo].[CompanyGroups] WHERE [Denumire] = 'UTI Grup';
    PRINT '⏭️ Grup UTI Grup există deja';
END
GO

-- =============================================
-- 2. COMPANII UTI
-- =============================================

-- Retrieve GroupId
DECLARE @GroupId UNIQUEIDENTIFIER;
SELECT @GroupId = [Id] FROM [dbo].[CompanyGroups] WHERE [Denumire] = 'UTI Grup';

-- Generate IDs for companies
DECLARE @UTISystemsId UNIQUEIDENTIFIER = NEWID();
DECLARE @UTIInstalId UNIQUEIDENTIFIER = NEWID();
DECLARE @CobraSecurityId UNIQUEIDENTIFIER = NEWID();
DECLARE @UTIRetailId UNIQUEIDENTIFIER = NEWID();
DECLARE @UTIFacilityId UNIQUEIDENTIFIER = NEWID();
DECLARE @CertSignId UNIQUEIDENTIFIER = NEWID();

-- 2.1 UTI Systems SA - Compania principală (Holding)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Companies] WHERE [CUI] = 'RO16326010')
BEGIN
    INSERT INTO [dbo].[Companies] (
        [Id], [GroupId], [ParentCompanyId], [CUI], [RegCom], [Denumire], [DenumireScurta],
        [TipCompanie], [ProcentDetinere], [CapitalSocial],
        [Telefon], [Email], [Website],
        [IsPrincipal], [IsActive], [SortOrder]
    ) VALUES (
        @UTISystemsId,
        @GroupId,
        NULL,
        N'RO16326010',
        N'J40/8295/2004',
        N'UTI SYSTEMS SA',
        N'UTI Systems',
        1, -- Holding
        NULL,
        50000000.00,
        N'+40 21 335 88 00',
        N'office@uti.eu.com',
        N'https://www.uti.eu.com',
        1, -- Compania principală
        1,
        1
    );
    PRINT '✅ Companie UTI SYSTEMS SA inserată';
END
ELSE
BEGIN
    SELECT @UTISystemsId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO16326010';
    PRINT '⏭️ Companie UTI SYSTEMS SA există deja';
END

-- 2.2 UTI Instal Construct SRL
IF NOT EXISTS (SELECT 1 FROM [dbo].[Companies] WHERE [CUI] = 'RO18765432')
BEGIN
    INSERT INTO [dbo].[Companies] (
        [Id], [GroupId], [ParentCompanyId], [CUI], [RegCom], [Denumire], [DenumireScurta],
        [TipCompanie], [ProcentDetinere], [CapitalSocial],
        [Telefon], [Email], [Website],
        [IsPrincipal], [IsActive], [SortOrder]
    ) VALUES (
        @UTIInstalId,
        @GroupId,
        @UTISystemsId,
        N'RO18765432',
        N'J40/12345/2006',
        N'UTI INSTAL CONSTRUCT SRL',
        N'UTI Instal',
        2, -- Subsidiary
        100.00,
        5000000.00,
        N'+40 21 335 88 10',
        N'instal@uti.eu.com',
        NULL,
        0,
        1,
        2
    );
    PRINT '✅ Companie UTI INSTAL CONSTRUCT SRL inserată';
END
ELSE
BEGIN
    SELECT @UTIInstalId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO18765432';
END

-- 2.3 Cobra Security SRL
IF NOT EXISTS (SELECT 1 FROM [dbo].[Companies] WHERE [CUI] = 'RO9433622')
BEGIN
    INSERT INTO [dbo].[Companies] (
        [Id], [GroupId], [ParentCompanyId], [CUI], [RegCom], [Denumire], [DenumireScurta],
        [TipCompanie], [ProcentDetinere], [CapitalSocial],
        [Telefon], [Email], [Website],
        [IsPrincipal], [IsActive], [SortOrder]
    ) VALUES (
        @CobraSecurityId,
        @GroupId,
        @UTISystemsId,
        N'RO9433622',
        N'J40/5678/1997',
        N'COBRA SECURITY SRL',
        N'Cobra Security',
        2, -- Subsidiary
        100.00,
        10000000.00,
        N'+40 21 335 88 20',
        N'cobra@uti.eu.com',
        N'https://www.cobra.ro',
        0,
        1,
        3
    );
    PRINT '✅ Companie COBRA SECURITY SRL inserată';
END
ELSE
BEGIN
    SELECT @CobraSecurityId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO9433622';
END

-- 2.4 UTI Retail Solutions SRL
IF NOT EXISTS (SELECT 1 FROM [dbo].[Companies] WHERE [CUI] = 'RO22334455')
BEGIN
    INSERT INTO [dbo].[Companies] (
        [Id], [GroupId], [ParentCompanyId], [CUI], [RegCom], [Denumire], [DenumireScurta],
        [TipCompanie], [ProcentDetinere], [CapitalSocial],
        [Telefon], [Email], [Website],
        [IsPrincipal], [IsActive], [SortOrder]
    ) VALUES (
        @UTIRetailId,
        @GroupId,
        @UTISystemsId,
        N'RO22334455',
        N'J40/9876/2008',
        N'UTI RETAIL SOLUTIONS SRL',
        N'UTI Retail',
        2, -- Subsidiary
        100.00,
        2000000.00,
        N'+40 21 335 88 30',
        N'retail@uti.eu.com',
        NULL,
        0,
        1,
        4
    );
    PRINT '✅ Companie UTI RETAIL SOLUTIONS SRL inserată';
END
ELSE
BEGIN
    SELECT @UTIRetailId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO22334455';
END

-- 2.5 UTI Facility Management SRL
IF NOT EXISTS (SELECT 1 FROM [dbo].[Companies] WHERE [CUI] = 'RO33445566')
BEGIN
    INSERT INTO [dbo].[Companies] (
        [Id], [GroupId], [ParentCompanyId], [CUI], [RegCom], [Denumire], [DenumireScurta],
        [TipCompanie], [ProcentDetinere], [CapitalSocial],
        [Telefon], [Email], [Website],
        [IsPrincipal], [IsActive], [SortOrder]
    ) VALUES (
        @UTIFacilityId,
        @GroupId,
        @UTISystemsId,
        N'RO33445566',
        N'J40/4567/2010',
        N'UTI FACILITY MANAGEMENT SRL',
        N'UTI Facility',
        2, -- Subsidiary
        100.00,
        3000000.00,
        N'+40 21 335 88 40',
        N'facility@uti.eu.com',
        NULL,
        0,
        1,
        5
    );
    PRINT '✅ Companie UTI FACILITY MANAGEMENT SRL inserată';
END
ELSE
BEGIN
    SELECT @UTIFacilityId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO33445566';
END

-- 2.6 CertSign SA (IT/Securitate digitală)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Companies] WHERE [CUI] = 'RO44556677')
BEGIN
    INSERT INTO [dbo].[Companies] (
        [Id], [GroupId], [ParentCompanyId], [CUI], [RegCom], [Denumire], [DenumireScurta],
        [TipCompanie], [ProcentDetinere], [CapitalSocial],
        [Telefon], [Email], [Website],
        [IsPrincipal], [IsActive], [SortOrder]
    ) VALUES (
        @CertSignId,
        @GroupId,
        @UTISystemsId,
        N'RO44556677',
        N'J40/7890/2005',
        N'CERTSIGN SA',
        N'CertSign',
        2, -- Subsidiary
        75.00, -- 75% deținere
        8000000.00,
        N'+40 21 335 88 50',
        N'office@certsign.ro',
        N'https://www.certsign.ro',
        0,
        1,
        6
    );
    PRINT '✅ Companie CERTSIGN SA inserată';
END
ELSE
BEGIN
    SELECT @CertSignId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO44556677';
END

PRINT '✅ Toate companiile UTI Grup au fost inserate';
GO

-- =============================================
-- 3. PUNCTE DE LUCRU (WorkPlaces)
-- =============================================

-- Retrieve Company IDs
DECLARE @UTISystemsId UNIQUEIDENTIFIER;
DECLARE @CobraSecurityId UNIQUEIDENTIFIER;
DECLARE @UTIInstalId UNIQUEIDENTIFIER;
SELECT @UTISystemsId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO16326010';
SELECT @CobraSecurityId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO9433622';
SELECT @UTIInstalId = [Id] FROM [dbo].[Companies] WHERE [CUI] = 'RO18765432';

-- 3.1 Sediu Social UTI Systems - București
DECLARE @SediuUTIId UNIQUEIDENTIFIER = NEWID();
IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @UTISystemsId AND [EsteSediuSocial] = 1)
BEGIN
    INSERT INTO [dbo].[WorkPlaces] (
        [Id], [CompanyId], [Denumire], [TipPunctLucru], [EsteSediuSocial],
        [Adresa], [Localitate], [Judet], [CodPostal], [Tara],
        [Telefon], [Email], [IsActive], [SortOrder]
    ) VALUES (
        @SediuUTIId,
        @UTISystemsId,
        N'Sediu Central UTI',
        1, -- Sediu Social
        1,
        N'Șoseaua Olteniței nr. 103-105',
        N'București',
        N'Sector 4',
        N'041303',
        N'România',
        N'+40 21 335 88 00',
        N'office@uti.eu.com',
        1,
        1
    );
    PRINT '✅ Sediu Social UTI Systems inserat';
END
ELSE
BEGIN
    SELECT @SediuUTIId = [Id] FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @UTISystemsId AND [EsteSediuSocial] = 1;
END

-- 3.2 Punct de lucru UTI Systems - Cluj
IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @UTISystemsId AND [Denumire] = N'Filiala Cluj')
BEGIN
    INSERT INTO [dbo].[WorkPlaces] (
        [Id], [CompanyId], [Denumire], [TipPunctLucru], [EsteSediuSocial],
        [Adresa], [Localitate], [Judet], [CodPostal], [Tara],
        [Telefon], [Email], [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @UTISystemsId,
        N'Filiala Cluj',
        3, -- Punct de Lucru
        0,
        N'Strada Fabricii nr. 45',
        N'Cluj-Napoca',
        N'Cluj',
        N'400520',
        N'România',
        N'+40 264 123 456',
        N'cluj@uti.eu.com',
        1,
        2
    );
    PRINT '✅ Filiala Cluj UTI Systems inserată';
END

-- 3.3 Punct de lucru UTI Systems - Iași
IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @UTISystemsId AND [Denumire] = N'Filiala Iași')
BEGIN
    INSERT INTO [dbo].[WorkPlaces] (
        [Id], [CompanyId], [Denumire], [TipPunctLucru], [EsteSediuSocial],
        [Adresa], [Localitate], [Judet], [CodPostal], [Tara],
        [Telefon], [Email], [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @UTISystemsId,
        N'Filiala Iași',
        3, -- Punct de Lucru
        0,
        N'Bulevardul Independenței nr. 12',
        N'Iași',
        N'Iași',
        N'700012',
        N'România',
        N'+40 232 123 456',
        N'iasi@uti.eu.com',
        1,
        3
    );
    PRINT '✅ Filiala Iași UTI Systems inserată';
END

-- 3.4 Sediu Social Cobra Security
DECLARE @SediuCobraId UNIQUEIDENTIFIER = NEWID();
IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @CobraSecurityId AND [EsteSediuSocial] = 1)
BEGIN
    INSERT INTO [dbo].[WorkPlaces] (
        [Id], [CompanyId], [Denumire], [TipPunctLucru], [EsteSediuSocial],
        [Adresa], [Localitate], [Judet], [CodPostal], [Tara],
        [Telefon], [Email], [IsActive], [SortOrder]
    ) VALUES (
        @SediuCobraId,
        @CobraSecurityId,
        N'Sediu Central Cobra',
        1, -- Sediu Social
        1,
        N'Șoseaua Olteniței nr. 103-105, Corp B',
        N'București',
        N'Sector 4',
        N'041303',
        N'România',
        N'+40 21 335 88 20',
        N'office@cobra.ro',
        1,
        1
    );
    PRINT '✅ Sediu Social Cobra Security inserat';
END
ELSE
BEGIN
    SELECT @SediuCobraId = [Id] FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @CobraSecurityId AND [EsteSediuSocial] = 1;
END

-- 3.5 Dispecerat Cobra Security
IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @CobraSecurityId AND [Denumire] = N'Dispecerat Central')
BEGIN
    INSERT INTO [dbo].[WorkPlaces] (
        [Id], [CompanyId], [Denumire], [TipPunctLucru], [EsteSediuSocial],
        [Adresa], [Localitate], [Judet], [CodPostal], [Tara],
        [Telefon], [Email], [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @CobraSecurityId,
        N'Dispecerat Central',
        3, -- Punct de Lucru
        0,
        N'Strada Drumul Taberei nr. 100',
        N'București',
        N'Sector 6',
        N'061352',
        N'România',
        N'+40 21 9422',
        N'dispecerat@cobra.ro',
        1,
        2
    );
    PRINT '✅ Dispecerat Central Cobra inserat';
END

-- 3.6 Sediu Social UTI Instal Construct
IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkPlaces] WHERE [CompanyId] = @UTIInstalId AND [EsteSediuSocial] = 1)
BEGIN
    INSERT INTO [dbo].[WorkPlaces] (
        [Id], [CompanyId], [Denumire], [TipPunctLucru], [EsteSediuSocial],
        [Adresa], [Localitate], [Judet], [CodPostal], [Tara],
        [Telefon], [Email], [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @UTIInstalId,
        N'Sediu UTI Instal',
        1, -- Sediu Social
        1,
        N'Șoseaua Olteniței nr. 103-105, Corp C',
        N'București',
        N'Sector 4',
        N'041303',
        N'România',
        N'+40 21 335 88 10',
        N'instal@uti.eu.com',
        1,
        1
    );
    PRINT '✅ Sediu Social UTI Instal Construct inserat';
END

PRINT '✅ Toate punctele de lucru au fost inserate';
GO

-- =============================================
-- 4. LOCAȚII (Locations) - în cadrul sediului principal
-- =============================================

-- Retrieve WorkPlace ID for UTI Systems HQ
DECLARE @SediuUTIId UNIQUEIDENTIFIER;
DECLARE @SediuCobraId UNIQUEIDENTIFIER;
SELECT @SediuUTIId = [Id] FROM [dbo].[WorkPlaces] wp
    INNER JOIN [dbo].[Companies] c ON wp.[CompanyId] = c.[Id]
    WHERE c.[CUI] = 'RO16326010' AND wp.[EsteSediuSocial] = 1;
SELECT @SediuCobraId = [Id] FROM [dbo].[WorkPlaces] wp
    INNER JOIN [dbo].[Companies] c ON wp.[CompanyId] = c.[Id]
    WHERE c.[CUI] = 'RO9433622' AND wp.[EsteSediuSocial] = 1;

-- 4.1 Depozit Central UTI
IF @SediuUTIId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[Locations] WHERE [WorkPlaceId] = @SediuUTIId AND [Denumire] = N'Depozit Central')
BEGIN
    INSERT INTO [dbo].[Locations] (
        [Id], [WorkPlaceId], [Denumire], [TipLocatie],
        [Adresa], [Localitate], [Judet], [CodPostal],
        [PoateStoca], [PoateVinde], [PoateCumpara], [PoateProduce],
        [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @SediuUTIId,
        N'Depozit Central',
        1, -- Depozit
        N'Șoseaua Olteniței nr. 103, Hala A',
        N'București',
        N'Sector 4',
        N'041303',
        1, -- PoateStoca
        0, -- PoateVinde
        1, -- PoateCumpara
        0, -- PoateProduce
        1,
        1
    );
    PRINT '✅ Depozit Central UTI inserat';
END

-- 4.2 Showroom UTI
IF @SediuUTIId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[Locations] WHERE [WorkPlaceId] = @SediuUTIId AND [Denumire] = N'Showroom')
BEGIN
    INSERT INTO [dbo].[Locations] (
        [Id], [WorkPlaceId], [Denumire], [TipLocatie],
        [Adresa], [Localitate], [Judet], [CodPostal],
        [PoateStoca], [PoateVinde], [PoateCumpara], [PoateProduce],
        [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @SediuUTIId,
        N'Showroom',
        3, -- Magazin
        N'Șoseaua Olteniței nr. 105, Parter',
        N'București',
        N'Sector 4',
        N'041303',
        1, -- PoateStoca
        1, -- PoateVinde
        0, -- PoateCumpara
        0, -- PoateProduce
        1,
        2
    );
    PRINT '✅ Showroom UTI inserat';
END

-- 4.3 Service Echipamente UTI
IF @SediuUTIId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[Locations] WHERE [WorkPlaceId] = @SediuUTIId AND [Denumire] = N'Service Echipamente')
BEGIN
    INSERT INTO [dbo].[Locations] (
        [Id], [WorkPlaceId], [Denumire], [TipLocatie],
        [Adresa], [Localitate], [Judet], [CodPostal],
        [PoateStoca], [PoateVinde], [PoateCumpara], [PoateProduce],
        [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @SediuUTIId,
        N'Service Echipamente',
        5, -- Atelier
        N'Șoseaua Olteniței nr. 103, Hala B',
        N'București',
        N'Sector 4',
        N'041303',
        1, -- PoateStoca
        0, -- PoateVinde
        1, -- PoateCumpara
        1, -- PoateProduce (reparații)
        1,
        3
    );
    PRINT '✅ Service Echipamente UTI inserat';
END

-- 4.4 Dispecerat Monitorizare Cobra
IF @SediuCobraId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[Locations] WHERE [WorkPlaceId] = @SediuCobraId AND [Denumire] = N'Sala Dispecerat')
BEGIN
    INSERT INTO [dbo].[Locations] (
        [Id], [WorkPlaceId], [Denumire], [TipLocatie],
        [Adresa], [Localitate], [Judet], [CodPostal],
        [PoateStoca], [PoateVinde], [PoateCumpara], [PoateProduce],
        [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @SediuCobraId,
        N'Sala Dispecerat',
        4, -- Birou
        N'Șoseaua Olteniței nr. 103-105, Corp B, Et. 1',
        N'București',
        N'Sector 4',
        N'041303',
        0, -- PoateStoca
        0, -- PoateVinde
        0, -- PoateCumpara
        0, -- PoateProduce
        1,
        1
    );
    PRINT '✅ Sala Dispecerat Cobra inserată';
END

-- 4.5 Depozit Echipamente Cobra
IF @SediuCobraId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[Locations] WHERE [WorkPlaceId] = @SediuCobraId AND [Denumire] = N'Depozit Echipamente')
BEGIN
    INSERT INTO [dbo].[Locations] (
        [Id], [WorkPlaceId], [Denumire], [TipLocatie],
        [Adresa], [Localitate], [Judet], [CodPostal],
        [PoateStoca], [PoateVinde], [PoateCumpara], [PoateProduce],
        [IsActive], [SortOrder]
    ) VALUES (
        NEWID(),
        @SediuCobraId,
        N'Depozit Echipamente',
        1, -- Depozit
        N'Șoseaua Olteniței nr. 103-105, Corp B, Subsol',
        N'București',
        N'Sector 4',
        N'041303',
        1, -- PoateStoca
        0, -- PoateVinde
        1, -- PoateCumpara
        0, -- PoateProduce
        1,
        2
    );
    PRINT '✅ Depozit Echipamente Cobra inserat';
END

PRINT '✅ Toate locațiile au fost inserate';
GO

PRINT '=== Seed Data UTI Grup - COMPLET ===';
GO

-- =============================================
-- VERIFICARE DATE INSERATE
-- =============================================
PRINT '';
PRINT '=== SUMAR DATE INSERATE ===';
SELECT 'Grupuri' AS Entitate, COUNT(*) AS Total FROM [dbo].[CompanyGroups];
SELECT 'Companii' AS Entitate, COUNT(*) AS Total FROM [dbo].[Companies];
SELECT 'Puncte de Lucru' AS Entitate, COUNT(*) AS Total FROM [dbo].[WorkPlaces];
SELECT 'Locații' AS Entitate, COUNT(*) AS Total FROM [dbo].[Locations];
GO
