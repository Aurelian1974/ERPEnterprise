-- =============================================
-- ValyanERP Identity Tables
-- Server: TS1828\ERP
-- Database: ValyanERP
-- =============================================

-- Users table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [PersoanaId] UNIQUEIDENTIFIER NOT NULL,
        [UserName] NVARCHAR(256) NOT NULL,
        [NormalizedUserName] NVARCHAR(256) NOT NULL,
        [Email] NVARCHAR(256) NULL,
        [NormalizedEmail] NVARCHAR(256) NULL,
        [EmailConfirmed] BIT NOT NULL DEFAULT 0,
        [PasswordHash] NVARCHAR(MAX) NULL,
        [SecurityStamp] NVARCHAR(MAX) NULL,
        [ConcurrencyStamp] NVARCHAR(MAX) NULL,
        [FirstName] NVARCHAR(100) NULL,
        [LastName] NVARCHAR(100) NULL,
        [PhoneNumber] NVARCHAR(50) NULL,
        [PhoneNumberConfirmed] BIT NOT NULL DEFAULT 0,
        [TwoFactorEnabled] BIT NOT NULL DEFAULT 0,
        [LockoutEnd] DATETIMEOFFSET NULL,
        [LockoutEnabled] BIT NOT NULL DEFAULT 0,
        [AccessFailedCount] INT NOT NULL DEFAULT 0,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_Users_Persoane] FOREIGN KEY ([PersoanaId]) REFERENCES [dbo].[Persoane]([Id])
    );

    CREATE UNIQUE INDEX [IX_Users_NormalizedUserName] ON [dbo].[Users] ([NormalizedUserName]);
    CREATE INDEX [IX_Users_NormalizedEmail] ON [dbo].[Users] ([NormalizedEmail]);
    CREATE INDEX [IX_Users_PersoanaId] ON [dbo].[Users] ([PersoanaId]);
END
GO

-- Roles table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Roles')
BEGIN
    CREATE TABLE [dbo].[Roles] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [Name] NVARCHAR(256) NOT NULL,
        [NormalizedName] NVARCHAR(256) NOT NULL,
        [ConcurrencyStamp] NVARCHAR(MAX) NULL
    );

    CREATE UNIQUE INDEX [IX_Roles_NormalizedName] ON [dbo].[Roles] ([NormalizedName]);
END
GO

-- UserRoles table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserRoles')
BEGIN
    CREATE TABLE [dbo].[UserRoles] (
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        [RoleId] UNIQUEIDENTIFIER NOT NULL,
        PRIMARY KEY ([UserId], [RoleId]),
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
        FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]) ON DELETE CASCADE
    );
END
GO

-- UserClaims table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserClaims')
BEGIN
    CREATE TABLE [dbo].[UserClaims] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        [ClaimType] NVARCHAR(MAX) NULL,
        [ClaimValue] NVARCHAR(MAX) NULL,
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
    );
END
GO

-- RoleClaims table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RoleClaims')
BEGIN
    CREATE TABLE [dbo].[RoleClaims] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [RoleId] UNIQUEIDENTIFIER NOT NULL,
        [ClaimType] NVARCHAR(MAX) NULL,
        [ClaimValue] NVARCHAR(MAX) NULL,
        FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]) ON DELETE CASCADE
    );
END
GO

-- UserLogins table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserLogins')
BEGIN
    CREATE TABLE [dbo].[UserLogins] (
        [LoginProvider] NVARCHAR(128) NOT NULL,
        [ProviderKey] NVARCHAR(128) NOT NULL,
        [ProviderDisplayName] NVARCHAR(MAX) NULL,
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        PRIMARY KEY ([LoginProvider], [ProviderKey]),
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
    );
END
GO

-- UserTokens table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserTokens')
BEGIN
    CREATE TABLE [dbo].[UserTokens] (
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        [LoginProvider] NVARCHAR(128) NOT NULL,
        [Name] NVARCHAR(128) NOT NULL,
        [Value] NVARCHAR(MAX) NULL,
        PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
    );
END
GO

-- Insert default roles
IF NOT EXISTS (SELECT * FROM [dbo].[Roles] WHERE [NormalizedName] = 'ADMIN')
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES 
        (NEWID(), 'Admin', 'ADMIN', NEWID()),
        (NEWID(), 'Manager', 'MANAGER', NEWID()),
        (NEWID(), 'User', 'USER', NEWID());
END
GO

PRINT 'Identity tables created successfully.';
