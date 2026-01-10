-- 003_Persoane_noFK.sql
-- Persoane table without FK references to dbo.Users (added later)
IF OBJECT_ID('dbo.vw_Persoane', 'V') IS NOT NULL DROP VIEW dbo.vw_Persoane;
GO
IF OBJECT_ID('dbo.Persoane', 'U') IS NOT NULL DROP TABLE dbo.Persoane;
GO

CREATE TABLE [dbo].[Persoane] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Nume] NVARCHAR(100) NOT NULL,
    [Prenume] NVARCHAR(100) NOT NULL,
    [CNP] NVARCHAR(13) NULL,
    [DataNasterii] DATE NULL,
    [Email] NVARCHAR(256) NULL,
    [Telefon] NVARCHAR(20) NULL,
    [Adresa] NVARCHAR(500) NULL,
    [Oras] NVARCHAR(100) NULL,
    [Judet] NVARCHAR(100) NULL,
    [CodPostal] NVARCHAR(10) NULL,
    [Tara] NVARCHAR(100) NULL DEFAULT 'Romania',
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2 NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL
    -- FK to Users will be added later via ALTER TABLE
);

CREATE INDEX [IX_Persoane_Nume] ON [dbo].[Persoane] ([Nume]);
CREATE INDEX [IX_Persoane_CNP] ON [dbo].[Persoane] ([CNP]);
CREATE INDEX [IX_Persoane_Email] ON [dbo].[Persoane] ([Email]);
CREATE INDEX [IX_Persoane_IsActive] ON [dbo].[Persoane] ([IsActive]);
GO

-- View (recreated after tables exist)
IF OBJECT_ID('dbo.vw_Persoane', 'V') IS NOT NULL DROP VIEW dbo.vw_Persoane;
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
    uc.UserName AS CreatedByUserName,
    p.UpdatedAt,
    p.UpdatedBy,
    uu.UserName AS UpdatedByUserName
FROM [dbo].[Persoane] p
LEFT JOIN [dbo].[Users] uc ON p.CreatedBy = uc.Id
LEFT JOIN [dbo].[Users] uu ON p.UpdatedBy = uu.Id;
GO

PRINT 'Persoane table (no User FK) created.';