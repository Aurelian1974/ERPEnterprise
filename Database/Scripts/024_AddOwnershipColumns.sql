-- =============================================
-- ValyanERP - Data Ownership Columns
-- Script: 024_AddOwnershipColumns.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Adaugă coloane de ownership organizațional în tabelele de date
--            pentru a putea filtra datele după perimetrul userului
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 024_AddOwnershipColumns ===';
GO

-- =============================================
-- 1. PERSOANE: Adaugă coloane de ownership
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Persoane') AND name = 'OwnerCompanyId')
BEGIN
    ALTER TABLE [dbo].[Persoane] ADD
        [OwnerCompanyId] UNIQUEIDENTIFIER NULL,
        [OwnerWorkPlaceId] UNIQUEIDENTIFIER NULL,
        [OwnerLocationId] UNIQUEIDENTIFIER NULL;
    
    PRINT '✅ Coloane ownership adăugate în Persoane';
END
ELSE
BEGIN
    PRINT '⏭️ Coloanele ownership există deja în Persoane';
END
GO

-- Adaugă FK constraints pentru Persoane
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Persoane_OwnerCompany')
BEGIN
    ALTER TABLE [dbo].[Persoane] ADD
        CONSTRAINT [FK_Persoane_OwnerCompany] FOREIGN KEY ([OwnerCompanyId]) REFERENCES [dbo].[Companies]([Id]),
        CONSTRAINT [FK_Persoane_OwnerWorkPlace] FOREIGN KEY ([OwnerWorkPlaceId]) REFERENCES [dbo].[WorkPlaces]([Id]),
        CONSTRAINT [FK_Persoane_OwnerLocation] FOREIGN KEY ([OwnerLocationId]) REFERENCES [dbo].[Locations]([Id]);
    
    PRINT '✅ FK constraints adăugate pentru Persoane';
END
GO

-- Index pentru filtrare rapidă pe Persoane
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Persoane_OwnerCompanyId')
BEGIN
    CREATE INDEX [IX_Persoane_OwnerCompanyId] ON [dbo].[Persoane] ([OwnerCompanyId]) WHERE [OwnerCompanyId] IS NOT NULL;
    PRINT '✅ Index IX_Persoane_OwnerCompanyId creat';
END
GO

-- =============================================
-- 2. USERS: Adaugă coloane de ownership
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'OwnerCompanyId')
BEGIN
    ALTER TABLE [dbo].[Users] ADD
        [OwnerCompanyId] UNIQUEIDENTIFIER NULL,
        [OwnerWorkPlaceId] UNIQUEIDENTIFIER NULL,
        [OwnerLocationId] UNIQUEIDENTIFIER NULL;
    
    PRINT '✅ Coloane ownership adăugate în Users';
END
ELSE
BEGIN
    PRINT '⏭️ Coloanele ownership există deja în Users';
END
GO

-- Adaugă FK constraints pentru Users
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Users_OwnerCompany')
BEGIN
    ALTER TABLE [dbo].[Users] ADD
        CONSTRAINT [FK_Users_OwnerCompany] FOREIGN KEY ([OwnerCompanyId]) REFERENCES [dbo].[Companies]([Id]),
        CONSTRAINT [FK_Users_OwnerWorkPlace] FOREIGN KEY ([OwnerWorkPlaceId]) REFERENCES [dbo].[WorkPlaces]([Id]),
        CONSTRAINT [FK_Users_OwnerLocation] FOREIGN KEY ([OwnerLocationId]) REFERENCES [dbo].[Locations]([Id]);
    
    PRINT '✅ FK constraints adăugate pentru Users';
END
GO

-- Index pentru filtrare rapidă pe Users
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_OwnerCompanyId')
BEGIN
    CREATE INDEX [IX_Users_OwnerCompanyId] ON [dbo].[Users] ([OwnerCompanyId]) WHERE [OwnerCompanyId] IS NOT NULL;
    PRINT '✅ Index IX_Users_OwnerCompanyId creat';
END
GO

-- =============================================
-- 3. PARTNERS: Adaugă coloane de ownership
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Partners') AND name = 'OwnerCompanyId')
BEGIN
    ALTER TABLE [dbo].[Partners] ADD
        [OwnerCompanyId] UNIQUEIDENTIFIER NULL,
        [OwnerWorkPlaceId] UNIQUEIDENTIFIER NULL,
        [OwnerLocationId] UNIQUEIDENTIFIER NULL;
    
    PRINT '✅ Coloane ownership adăugate în Partners';
END
ELSE
BEGIN
    PRINT '⏭️ Coloanele ownership există deja în Partners';
END
GO

-- Adaugă FK constraints pentru Partners
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Partners_OwnerCompany')
BEGIN
    ALTER TABLE [dbo].[Partners] ADD
        CONSTRAINT [FK_Partners_OwnerCompany] FOREIGN KEY ([OwnerCompanyId]) REFERENCES [dbo].[Companies]([Id]),
        CONSTRAINT [FK_Partners_OwnerWorkPlace] FOREIGN KEY ([OwnerWorkPlaceId]) REFERENCES [dbo].[WorkPlaces]([Id]),
        CONSTRAINT [FK_Partners_OwnerLocation] FOREIGN KEY ([OwnerLocationId]) REFERENCES [dbo].[Locations]([Id]);
    
    PRINT '✅ FK constraints adăugate pentru Partners';
END
GO

-- Index pentru filtrare rapidă pe Partners
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_OwnerCompanyId')
BEGIN
    CREATE INDEX [IX_Partners_OwnerCompanyId] ON [dbo].[Partners] ([OwnerCompanyId]) WHERE [OwnerCompanyId] IS NOT NULL;
    PRINT '✅ Index IX_Partners_OwnerCompanyId creat';
END
GO

-- =============================================
-- 4. VIEW: Actualizează vw_Persoane pentru a include ownership
-- =============================================
IF OBJECT_ID('dbo.vw_Persoane', 'V') IS NOT NULL
    DROP VIEW dbo.vw_Persoane;
GO

CREATE VIEW dbo.vw_Persoane
AS
SELECT 
    p.Id,
    p.Nume,
    p.Prenume,
    p.Nume + ' ' + p.Prenume AS NumeComplet,
    p.CNP,
    p.DataNasterii,
    p.Email,
    p.Telefon,
    p.Adresa,
    p.Oras,
    p.Judet,
    p.CodPostal,
    p.Tara,
    p.IsActive,
    p.CreatedAt,
    p.CreatedBy,
    p.UpdatedAt,
    p.UpdatedBy,
    -- Ownership
    p.OwnerCompanyId,
    p.OwnerWorkPlaceId,
    p.OwnerLocationId,
    oc.Denumire AS OwnerCompanyName,
    owp.Denumire AS OwnerWorkPlaceName,
    ol.Denumire AS OwnerLocationName,
    -- Creator info
    cu.UserName AS CreatedByUserName,
    uu.UserName AS UpdatedByUserName
FROM [dbo].[Persoane] p
LEFT JOIN [dbo].[Companies] oc ON oc.Id = p.OwnerCompanyId
LEFT JOIN [dbo].[WorkPlaces] owp ON owp.Id = p.OwnerWorkPlaceId
LEFT JOIN [dbo].[Locations] ol ON ol.Id = p.OwnerLocationId
LEFT JOIN [dbo].[Users] cu ON cu.Id = p.CreatedBy
LEFT JOIN [dbo].[Users] uu ON uu.Id = p.UpdatedBy;
GO

PRINT '✅ View vw_Persoane actualizat cu ownership';
GO

-- =============================================
-- 5. FUNCȚIE: Returnează datele vizibile pentru un user (Persoane)
-- =============================================
IF OBJECT_ID('dbo.fn_GetUserVisiblePersoane', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_GetUserVisiblePersoane;
GO

CREATE FUNCTION [dbo].[fn_GetUserVisiblePersoane](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    SELECT p.Id AS PersoanaId
    FROM Persoane p
    WHERE p.IsActive = 1
    AND (
        -- 1. Datele din companiile la care are acces
        p.OwnerCompanyId IN (SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@UserId))
        OR
        -- 2. Datele pe care le-a creat userul
        p.CreatedBy = @UserId
        OR
        -- 3. Datele fără owner (legacy sau global)
        p.OwnerCompanyId IS NULL
        OR
        -- 4. User-ul este Admin (bypass)
        EXISTS (
            SELECT 1 FROM UserRoles ur
            INNER JOIN Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId AND r.NormalizedName = 'ADMIN'
        )
    )
);
GO

PRINT '✅ Funcție fn_GetUserVisiblePersoane creată';
GO

-- =============================================
-- 6. FUNCȚIE: Returnează datele vizibile pentru un user (Partners)
-- =============================================
IF OBJECT_ID('dbo.fn_GetUserVisiblePartners', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_GetUserVisiblePartners;
GO

CREATE FUNCTION [dbo].[fn_GetUserVisiblePartners](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    SELECT p.Id AS PartnerId
    FROM Partners p
    WHERE p.IsActive = 1
    AND (
        -- 1. Datele din companiile la care are acces
        p.OwnerCompanyId IN (SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@UserId))
        OR
        -- 2. Datele pe care le-a creat userul
        p.CreatedBy = @UserId
        OR
        -- 3. Datele fără owner (legacy sau global)
        p.OwnerCompanyId IS NULL
        OR
        -- 4. User-ul este Admin (bypass)
        EXISTS (
            SELECT 1 FROM UserRoles ur
            INNER JOIN Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId AND r.NormalizedName = 'ADMIN'
        )
    )
);
GO

PRINT '✅ Funcție fn_GetUserVisiblePartners creată';
GO

-- =============================================
-- 7. FUNCȚIE: Returnează datele vizibile pentru un user (Users)
-- =============================================
IF OBJECT_ID('dbo.fn_GetUserVisibleUsers', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_GetUserVisibleUsers;
GO

CREATE FUNCTION [dbo].[fn_GetUserVisibleUsers](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    SELECT u.Id AS VisibleUserId
    FROM Users u
    WHERE u.IsActive = 1
    AND (
        -- 1. Datele din companiile la care are acces
        u.OwnerCompanyId IN (SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@UserId))
        OR
        -- 2. Userul se poate vedea pe sine
        u.Id = @UserId
        OR
        -- 3. Datele fără owner (legacy sau global - inclusiv admin)
        u.OwnerCompanyId IS NULL
        OR
        -- 4. User-ul este Admin (bypass)
        EXISTS (
            SELECT 1 FROM UserRoles ur
            INNER JOIN Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId AND r.NormalizedName = 'ADMIN'
        )
    )
);
GO

PRINT '✅ Funcție fn_GetUserVisibleUsers creată';
GO

PRINT '=== Migrare 024_AddOwnershipColumns completă ===';
GO
