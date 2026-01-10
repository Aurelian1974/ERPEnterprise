-- 006_CreateAdminUserWithPersoana.sql
-- Create Persoana for admin and insert Admin user with PersoanaId (if missing)
DECLARE @AdminEmail NVARCHAR(256) = 'admin@valyanerp.ro';
DECLARE @PersoanaId UNIQUEIDENTIFIER;

IF EXISTS (SELECT 1 FROM dbo.Persoane WHERE Email = @AdminEmail)
BEGIN
    SELECT @PersoanaId = Id FROM dbo.Persoane WHERE Email = @AdminEmail;
END
ELSE
BEGIN
    SET @PersoanaId = NEWID();
    INSERT INTO dbo.Persoane (Id, Nume, Prenume, Email, Telefon, IsActive, CreatedAt)
    VALUES (@PersoanaId, 'System', 'Administrator', @AdminEmail, '0700000000', 1, GETDATE());
END

-- Use the same precomputed hash from 004 script (Admin123!)
DECLARE @UserId UNIQUEIDENTIFIER = NEWID();
DECLARE @PasswordHash NVARCHAR(MAX) = 'AQAAAAIAAYagAAAAEKqh8Vl1QKf8LqD3JUELHr4YqV1rZK0PMv8O3ZPkJG4YQJC1v5+JZY3h6rGQJxH5Cw==';

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE NormalizedUserName = UPPER(@AdminEmail))
BEGIN
    INSERT INTO [dbo].[Users] (
        Id, PersoanaId, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
        PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
        TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, FirstName, LastName, IsActive, CreatedAt
    )
    VALUES (
        @UserId, @PersoanaId, @AdminEmail, UPPER(@AdminEmail), @AdminEmail, UPPER(@AdminEmail), 1,
        @PasswordHash, NEWID(), NEWID(), NULL, 0,
        0, NULL, 1, 0, 'Administrator', 'System', 1, GETDATE()
    );
    PRINT 'Admin user inserted via SQL.';
END
ELSE
BEGIN
    PRINT 'Admin user already exists.';
END