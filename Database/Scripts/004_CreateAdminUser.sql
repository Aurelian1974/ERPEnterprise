-- =============================================
-- Create Admin User for ValyanERP
-- Password: Admin123!
-- =============================================

DECLARE @UserId UNIQUEIDENTIFIER = NEWID();
DECLARE @PasswordHash NVARCHAR(MAX) = 'AQAAAAIAAYagAAAAEKqh8Vl1QKf8LqD3JUELHr4YqV1rZK0PMv8O3ZPkJG4YQJC1v5+JZY3h6rGQJxH5Cw==';
-- This is a hashed version of 'Admin123!' using ASP.NET Core Identity

INSERT INTO [dbo].[Users] (
    Id, UserName, NormalizedUserName, Email, NormalizedEmail, 
    EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
    PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
    LockoutEnd, LockoutEnabled, AccessFailedCount,
    FirstName, LastName, IsActive, CreatedAt
)
VALUES (
    @UserId,
    'admin@valyanerp.ro',
    'ADMIN@VALYANERP.RO',
    'admin@valyanerp.ro',
    'ADMIN@VALYANERP.RO',
    1,
    @PasswordHash,
    NEWID(),
    NEWID(),
    NULL,
    0,
    0,
    NULL,
    1,
    0,
    'Administrator',
    'System',
    1,
    GETDATE()
);

PRINT 'Admin user created: admin@valyanerp.ro';
