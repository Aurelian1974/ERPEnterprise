-- =============================================
-- ValyanERP Identity Stored Procedures
-- Server: TS1828\ERP
-- Database: ValyanERP
-- =============================================

-- =============================================
-- USER PROCEDURES
-- =============================================

-- Create User
IF OBJECT_ID('dbo.sp_Users_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Users_Create;
GO
CREATE PROCEDURE dbo.sp_Users_Create
    @Id UNIQUEIDENTIFIER,
    @UserName NVARCHAR(256),
    @NormalizedUserName NVARCHAR(256),
    @Email NVARCHAR(256),
    @NormalizedEmail NVARCHAR(256),
    @EmailConfirmed BIT,
    @PasswordHash NVARCHAR(MAX),
    @SecurityStamp NVARCHAR(MAX),
    @ConcurrencyStamp NVARCHAR(MAX),
    @PhoneNumber NVARCHAR(50),
    @PhoneNumberConfirmed BIT,
    @TwoFactorEnabled BIT,
    @LockoutEnd DATETIMEOFFSET,
    @LockoutEnabled BIT,
    @AccessFailedCount INT,
    @FirstName NVARCHAR(100),
    @LastName NVARCHAR(100),
    @IsActive BIT,
    @CreatedAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[Users] (
        Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
        PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
        TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, 
        FirstName, LastName, IsActive, CreatedAt
    ) VALUES (
        @Id, @UserName, @NormalizedUserName, @Email, @NormalizedEmail, @EmailConfirmed,
        @PasswordHash, @SecurityStamp, @ConcurrencyStamp, @PhoneNumber, @PhoneNumberConfirmed,
        @TwoFactorEnabled, @LockoutEnd, @LockoutEnabled, @AccessFailedCount,
        @FirstName, @LastName, @IsActive, @CreatedAt
    );
END
GO

-- Update User
IF OBJECT_ID('dbo.sp_Users_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Users_Update;
GO
CREATE PROCEDURE dbo.sp_Users_Update
    @Id UNIQUEIDENTIFIER,
    @UserName NVARCHAR(256),
    @NormalizedUserName NVARCHAR(256),
    @Email NVARCHAR(256),
    @NormalizedEmail NVARCHAR(256),
    @EmailConfirmed BIT,
    @PasswordHash NVARCHAR(MAX),
    @SecurityStamp NVARCHAR(MAX),
    @ConcurrencyStamp NVARCHAR(MAX),
    @PhoneNumber NVARCHAR(50),
    @PhoneNumberConfirmed BIT,
    @TwoFactorEnabled BIT,
    @LockoutEnd DATETIMEOFFSET,
    @LockoutEnabled BIT,
    @AccessFailedCount INT,
    @FirstName NVARCHAR(100),
    @LastName NVARCHAR(100),
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[Users] SET
        UserName = @UserName,
        NormalizedUserName = @NormalizedUserName,
        Email = @Email,
        NormalizedEmail = @NormalizedEmail,
        EmailConfirmed = @EmailConfirmed,
        PasswordHash = @PasswordHash,
        SecurityStamp = @SecurityStamp,
        ConcurrencyStamp = @ConcurrencyStamp,
        PhoneNumber = @PhoneNumber,
        PhoneNumberConfirmed = @PhoneNumberConfirmed,
        TwoFactorEnabled = @TwoFactorEnabled,
        LockoutEnd = @LockoutEnd,
        LockoutEnabled = @LockoutEnabled,
        AccessFailedCount = @AccessFailedCount,
        FirstName = @FirstName,
        LastName = @LastName,
        IsActive = @IsActive,
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
END
GO

-- Delete User
IF OBJECT_ID('dbo.sp_Users_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Users_Delete;
GO
CREATE PROCEDURE dbo.sp_Users_Delete
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[Users] WHERE Id = @Id;
END
GO

-- Get User By Id
IF OBJECT_ID('dbo.sp_Users_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Users_GetById;
GO
CREATE PROCEDURE dbo.sp_Users_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM [dbo].[Users] WHERE Id = @Id;
END
GO

-- Get User By NormalizedUserName
IF OBJECT_ID('dbo.sp_Users_GetByNormalizedUserName', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Users_GetByNormalizedUserName;
GO
CREATE PROCEDURE dbo.sp_Users_GetByNormalizedUserName
    @NormalizedUserName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM [dbo].[Users] WHERE NormalizedUserName = @NormalizedUserName;
END
GO

-- Get User By NormalizedEmail
IF OBJECT_ID('dbo.sp_Users_GetByNormalizedEmail', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Users_GetByNormalizedEmail;
GO
CREATE PROCEDURE dbo.sp_Users_GetByNormalizedEmail
    @NormalizedEmail NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM [dbo].[Users] WHERE NormalizedEmail = @NormalizedEmail;
END
GO

-- =============================================
-- USER ROLES PROCEDURES
-- =============================================

-- Add User To Role
IF OBJECT_ID('dbo.sp_UserRoles_Add', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_UserRoles_Add;
GO
CREATE PROCEDURE dbo.sp_UserRoles_Add
    @UserId UNIQUEIDENTIFIER,
    @RoleName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[UserRoles] (UserId, RoleId)
    SELECT @UserId, Id FROM [dbo].[Roles] WHERE NormalizedName = @RoleName;
END
GO

-- Remove User From Role
IF OBJECT_ID('dbo.sp_UserRoles_Remove', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_UserRoles_Remove;
GO
CREATE PROCEDURE dbo.sp_UserRoles_Remove
    @UserId UNIQUEIDENTIFIER,
    @RoleName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[UserRoles] 
    WHERE UserId = @UserId 
      AND RoleId IN (SELECT Id FROM [dbo].[Roles] WHERE NormalizedName = @RoleName);
END
GO

-- Get User Roles
IF OBJECT_ID('dbo.sp_UserRoles_GetByUserId', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_UserRoles_GetByUserId;
GO
CREATE PROCEDURE dbo.sp_UserRoles_GetByUserId
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.Name 
    FROM [dbo].[Roles] r
    INNER JOIN [dbo].[UserRoles] ur ON r.Id = ur.RoleId
    WHERE ur.UserId = @UserId;
END
GO

-- Check If User Is In Role
IF OBJECT_ID('dbo.sp_UserRoles_IsInRole', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_UserRoles_IsInRole;
GO
CREATE PROCEDURE dbo.sp_UserRoles_IsInRole
    @UserId UNIQUEIDENTIFIER,
    @RoleName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(1) 
    FROM [dbo].[UserRoles] ur
    INNER JOIN [dbo].[Roles] r ON ur.RoleId = r.Id
    WHERE ur.UserId = @UserId AND r.NormalizedName = @RoleName;
END
GO

-- Get Users In Role
IF OBJECT_ID('dbo.sp_UserRoles_GetUsersInRole', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_UserRoles_GetUsersInRole;
GO
CREATE PROCEDURE dbo.sp_UserRoles_GetUsersInRole
    @RoleName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT u.* 
    FROM [dbo].[Users] u
    INNER JOIN [dbo].[UserRoles] ur ON u.Id = ur.UserId
    INNER JOIN [dbo].[Roles] r ON ur.RoleId = r.Id
    WHERE r.NormalizedName = @RoleName;
END
GO

-- =============================================
-- ROLE PROCEDURES
-- =============================================

-- Create Role
IF OBJECT_ID('dbo.sp_Roles_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Roles_Create;
GO
CREATE PROCEDURE dbo.sp_Roles_Create
    @Id UNIQUEIDENTIFIER,
    @Name NVARCHAR(256),
    @NormalizedName NVARCHAR(256),
    @ConcurrencyStamp NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[Roles] (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (@Id, @Name, @NormalizedName, @ConcurrencyStamp);
END
GO

-- Update Role
IF OBJECT_ID('dbo.sp_Roles_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Roles_Update;
GO
CREATE PROCEDURE dbo.sp_Roles_Update
    @Id UNIQUEIDENTIFIER,
    @Name NVARCHAR(256),
    @NormalizedName NVARCHAR(256),
    @ConcurrencyStamp NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [dbo].[Roles] SET
        Name = @Name,
        NormalizedName = @NormalizedName,
        ConcurrencyStamp = @ConcurrencyStamp
    WHERE Id = @Id;
END
GO

-- Delete Role
IF OBJECT_ID('dbo.sp_Roles_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Roles_Delete;
GO
CREATE PROCEDURE dbo.sp_Roles_Delete
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM [dbo].[Roles] WHERE Id = @Id;
END
GO

-- Get Role By Id
IF OBJECT_ID('dbo.sp_Roles_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Roles_GetById;
GO
CREATE PROCEDURE dbo.sp_Roles_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM [dbo].[Roles] WHERE Id = @Id;
END
GO

-- Get Role By NormalizedName
IF OBJECT_ID('dbo.sp_Roles_GetByNormalizedName', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Roles_GetByNormalizedName;
GO
CREATE PROCEDURE dbo.sp_Roles_GetByNormalizedName
    @NormalizedName NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM [dbo].[Roles] WHERE NormalizedName = @NormalizedName;
END
GO

PRINT 'Stored procedures created successfully.';
