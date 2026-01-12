-- =============================================
-- ValyanERP - Data Cleanup + Admin Setup
-- Script: 025_CleanupAndSetupAdmin.sql
-- Data: 12 Ianuarie 2026
-- Descriere: 
--   1. Șterge TOATE datele din DB EXCEPTÂND admin@valyanerp.ro și persoana asociată
--   2. Setează admin-ul ca administrator de grup
--   3. Pregătește sistemul pentru inserări fresh cu ownership
-- ⚠️ ATENȚIE: Script DESTRUCTIV! Rulați doar pe environment-uri de test/dev!
-- =============================================

USE [ValyanERP];
GO

PRINT '=== ⚠️ CLEANUP SCRIPT - STARTING ===';
PRINT '=== Acest script va șterge TOATE datele exceptând admin@valyanerp.ro ===';
GO

-- =============================================
-- VARIABILE: Identificare admin și persoana asociată
-- =============================================
DECLARE @AdminEmail NVARCHAR(256) = 'admin@valyanerp.ro';
DECLARE @AdminUserId UNIQUEIDENTIFIER;
DECLARE @AdminPersoanaId UNIQUEIDENTIFIER;

-- Găsește admin user și persoana asociată
SELECT @AdminUserId = Id, @AdminPersoanaId = PersoanaId 
FROM Users WHERE NormalizedEmail = UPPER(@AdminEmail);

IF @AdminUserId IS NULL
BEGIN
    PRINT '❌ EROARE: Admin user nu există! Rulați mai întâi 006_CreateAdminUserWithPersoana.sql';
    RETURN;
END

PRINT '✅ Admin User ID: ' + CAST(@AdminUserId AS NVARCHAR(50));
PRINT '✅ Admin Persoana ID: ' + CAST(@AdminPersoanaId AS NVARCHAR(50));
GO

-- =============================================
-- PASUL 1: Dezactivează toate constraints temporar
-- =============================================
PRINT '';
PRINT '=== PASUL 1: Dezactivare constraints ===';

-- Dezactivează FK checks
EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';
PRINT '✅ FK constraints dezactivate';
GO

-- =============================================
-- PASUL 2: Cleanup tabele în ordine corectă
-- =============================================
PRINT '';
PRINT '=== PASUL 2: Cleanup date ===';

DECLARE @AdminEmail NVARCHAR(256) = 'admin@valyanerp.ro';
DECLARE @AdminUserId UNIQUEIDENTIFIER;
DECLARE @AdminPersoanaId UNIQUEIDENTIFIER;

SELECT @AdminUserId = Id, @AdminPersoanaId = PersoanaId 
FROM Users WHERE NormalizedEmail = UPPER(@AdminEmail);

-- 2.1 UserOrganizationalAccess
IF OBJECT_ID('dbo.UserOrganizationalAccess', 'U') IS NOT NULL
BEGIN
    DELETE FROM UserOrganizationalAccess;
    PRINT '  ✅ UserOrganizationalAccess: șters tot';
END

-- 2.2 UserGridSettings
IF OBJECT_ID('dbo.UserGridSettings', 'U') IS NOT NULL
BEGIN
    DELETE FROM UserGridSettings WHERE UserId <> @AdminUserId;
    PRINT '  ✅ UserGridSettings: păstrat doar admin';
END

-- 2.3 Sessions
IF OBJECT_ID('dbo.Sessions', 'U') IS NOT NULL
BEGIN
    DELETE FROM Sessions;
    PRINT '  ✅ Sessions: șters tot';
END

-- 2.4 AuditLogs
IF OBJECT_ID('dbo.AuditLogs', 'U') IS NOT NULL
BEGIN
    DELETE FROM AuditLogs;
    PRINT '  ✅ AuditLogs: șters tot';
END

-- 2.5 Partners (toate)
IF OBJECT_ID('dbo.PartnerBankAccounts', 'U') IS NOT NULL
BEGIN
    DELETE FROM PartnerBankAccounts;
    PRINT '  ✅ PartnerBankAccounts: șters tot';
END

IF OBJECT_ID('dbo.PartnerAddresses', 'U') IS NOT NULL
BEGIN
    DELETE FROM PartnerAddresses;
    PRINT '  ✅ PartnerAddresses: șters tot';
END

IF OBJECT_ID('dbo.PartnerContacts', 'U') IS NOT NULL
BEGIN
    DELETE FROM PartnerContacts;
    PRINT '  ✅ PartnerContacts: șters tot';
END

IF OBJECT_ID('dbo.Partners', 'U') IS NOT NULL
BEGIN
    DELETE FROM Partners;
    PRINT '  ✅ Partners: șters tot';
END

-- 2.6 UserRoles (păstrează doar admin)
DELETE FROM UserRoles WHERE UserId <> @AdminUserId;
PRINT '  ✅ UserRoles: păstrat doar admin';

-- 2.7 UserClaims, UserLogins, UserTokens
DELETE FROM UserClaims WHERE UserId <> @AdminUserId;
DELETE FROM UserLogins WHERE UserId <> @AdminUserId;
DELETE FROM UserTokens WHERE UserId <> @AdminUserId;
PRINT '  ✅ UserClaims/Logins/Tokens: păstrat doar admin';

-- 2.8 Users (păstrează doar admin)
DELETE FROM Users WHERE Id <> @AdminUserId;
PRINT '  ✅ Users: păstrat doar admin';

-- 2.9 Persoane (păstrează doar persoana admin)
DELETE FROM Persoane WHERE Id <> @AdminPersoanaId;
PRINT '  ✅ Persoane: păstrat doar persoana admin';

-- 2.10 Locations
IF OBJECT_ID('dbo.Locations', 'U') IS NOT NULL
BEGIN
    DELETE FROM Locations;
    PRINT '  ✅ Locations: șters tot';
END

-- 2.11 WorkPlaces
IF OBJECT_ID('dbo.WorkPlaces', 'U') IS NOT NULL
BEGIN
    DELETE FROM WorkPlaces;
    PRINT '  ✅ WorkPlaces: șters tot';
END

-- 2.12 Companies
IF OBJECT_ID('dbo.Companies', 'U') IS NOT NULL
BEGIN
    DELETE FROM Companies;
    PRINT '  ✅ Companies: șters tot';
END

-- 2.13 CompanyGroups
IF OBJECT_ID('dbo.CompanyGroups', 'U') IS NOT NULL
BEGIN
    DELETE FROM CompanyGroups;
    PRINT '  ✅ CompanyGroups: șters tot';
END

PRINT '';
PRINT '=== Cleanup date completat ===';
GO

-- =============================================
-- PASUL 3: Reactivează constraints
-- =============================================
PRINT '';
PRINT '=== PASUL 3: Reactivare constraints ===';

EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';
PRINT '✅ FK constraints reactivate';
GO

-- =============================================
-- PASUL 4: Creează structura organizațională de bază
-- =============================================
PRINT '';
PRINT '=== PASUL 4: Creare structură organizațională de bază ===';

DECLARE @AdminEmail NVARCHAR(256) = 'admin@valyanerp.ro';
DECLARE @AdminUserId UNIQUEIDENTIFIER;
DECLARE @GroupId UNIQUEIDENTIFIER = NEWID();
DECLARE @CompanyId UNIQUEIDENTIFIER = NEWID();
DECLARE @WorkPlaceId UNIQUEIDENTIFIER = NEWID();

SELECT @AdminUserId = Id FROM Users WHERE NormalizedEmail = UPPER(@AdminEmail);

-- 4.1 Crează un grup implicit
INSERT INTO CompanyGroups (Id, Denumire, Descriere, IsActive, SortOrder, CreatedAt, CreatedBy)
VALUES (@GroupId, 'VALYAN GRUP', 'Grup principal ValyanERP', 1, 1, GETDATE(), @AdminUserId);
PRINT '  ✅ CompanyGroups: creat VALYAN GRUP';

-- 4.2 Crează o companie implicită
INSERT INTO Companies (Id, GroupId, CUI, Denumire, DenumireScurta, TipCompanie, IsPrincipal, IsActive, SortOrder, CreatedAt, CreatedBy)
VALUES (@CompanyId, @GroupId, 'RO00000000', 'VALYAN ERP SRL', 'VALYAN', 0, 1, 1, 1, GETDATE(), @AdminUserId);
PRINT '  ✅ Companies: creat VALYAN ERP SRL';

-- 4.3 Crează un punct de lucru (sediu social)
INSERT INTO WorkPlaces (Id, CompanyId, Denumire, TipPunctLucru, RegimJuridic, Adresa, Localitate, Judet, Tara, EsteSediuSocial, IsActive, SortOrder, CreatedAt, CreatedBy)
VALUES (@WorkPlaceId, @CompanyId, 'Sediu Social', 0, 0, 'Strada Exemplu nr. 1', 'București', 'București', 'România', 1, 1, 1, GETDATE(), @AdminUserId);
PRINT '  ✅ WorkPlaces: creat Sediu Social';

PRINT '';
PRINT '=== Structură organizațională creată ===';
GO

-- =============================================
-- PASUL 5: Setează admin ca administrator de grup
-- =============================================
PRINT '';
PRINT '=== PASUL 5: Setare admin ca administrator de grup ===';

DECLARE @AdminEmail NVARCHAR(256) = 'admin@valyanerp.ro';
DECLARE @AdminUserId UNIQUEIDENTIFIER;
DECLARE @GroupId UNIQUEIDENTIFIER;

SELECT @AdminUserId = Id FROM Users WHERE NormalizedEmail = UPPER(@AdminEmail);
SELECT TOP 1 @GroupId = Id FROM CompanyGroups WHERE IsActive = 1 ORDER BY CreatedAt;

IF @AdminUserId IS NOT NULL AND @GroupId IS NOT NULL
BEGIN
    -- Șterge orice acces existent (curățare)
    DELETE FROM UserOrganizationalAccess WHERE UserId = @AdminUserId;
    
    -- Inserează acces Admin la nivelul grupului
    INSERT INTO UserOrganizationalAccess (
        UserId, EntityType, EntityId, AccessLevel, InheritToChildren, 
        Notes, IsActive, CreatedAt, CreatedBy
    )
    VALUES (
        @AdminUserId, 
        'GROUP', 
        @GroupId, 
        4, -- Admin access
        1, -- Inherit to all children
        'Admin principal - acces complet la tot sistemul',
        1,
        GETDATE(),
        @AdminUserId
    );
    
    PRINT '✅ Admin setat cu AccessLevel=4 (Admin) pe grup';
    PRINT '   → Are acces la TOATE companiile din grup';
    PRINT '   → Are acces la TOATE punctele de lucru';
    PRINT '   → Are acces la TOATE locațiile';
END
ELSE
BEGIN
    PRINT '❌ EROARE: Nu s-a putut seta admin pe grup';
END
GO

-- =============================================
-- PASUL 6: Verificare finală
-- =============================================
PRINT '';
PRINT '=== PASUL 6: Verificare finală ===';

SELECT 'Users' AS Tabel, COUNT(*) AS [Count] FROM Users
UNION ALL
SELECT 'Persoane', COUNT(*) FROM Persoane
UNION ALL
SELECT 'CompanyGroups', COUNT(*) FROM CompanyGroups
UNION ALL
SELECT 'Companies', COUNT(*) FROM Companies
UNION ALL
SELECT 'WorkPlaces', COUNT(*) FROM WorkPlaces
UNION ALL
SELECT 'Locations', COUNT(*) FROM Locations
UNION ALL
SELECT 'UserOrganizationalAccess', COUNT(*) FROM UserOrganizationalAccess
UNION ALL
SELECT 'Partners', COUNT(*) FROM Partners;

PRINT '';
PRINT '=== ✅ CLEANUP SCRIPT COMPLETED ===';
PRINT '';
PRINT 'Următorii pași:';
PRINT '1. Logați-vă ca admin@valyanerp.ro / Admin@123';
PRINT '2. Modificați datele companiei (CUI, denumire, adresă)';
PRINT '3. Creați persoane și utilizatori noi';
PRINT '4. Setați permisiunile organizaționale pentru utilizatorii noi';
GO
