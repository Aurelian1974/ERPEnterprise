-- =============================================
-- ValyanERP - Persoane (Persons) Management
-- Server: TS1828\ERP
-- Database: ValyanERP
-- =============================================

-- =============================================
-- TABLE: Persoane
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Persoane')
BEGIN
    CREATE TABLE [dbo].[Persoane] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
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
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        CONSTRAINT [FK_Persoane_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_Persoane_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id])
    );

    CREATE INDEX [IX_Persoane_Nume] ON [dbo].[Persoane] ([Nume]);
    CREATE INDEX [IX_Persoane_CNP] ON [dbo].[Persoane] ([CNP]);
    CREATE INDEX [IX_Persoane_Email] ON [dbo].[Persoane] ([Email]);
    CREATE INDEX [IX_Persoane_IsActive] ON [dbo].[Persoane] ([IsActive]);
    
    PRINT 'Table Persoane created successfully.';
END
GO

-- =============================================
-- VIEW: vw_Persoane
-- =============================================
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

-- =============================================
-- STORED PROCEDURES
-- =============================================

-- Get All Persoane
IF OBJECT_ID('dbo.sp_Persoane_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Persoane_GetAll;
GO
CREATE PROCEDURE dbo.sp_Persoane_GetAll
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM [dbo].[vw_Persoane]
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY Nume, Prenume;
END
GO

-- Get Persoana By Id
IF OBJECT_ID('dbo.sp_Persoane_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Persoane_GetById;
GO
CREATE PROCEDURE dbo.sp_Persoane_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM [dbo].[vw_Persoane]
    WHERE Id = @Id;
END
GO

-- Create Persoana
IF OBJECT_ID('dbo.sp_Persoane_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Persoane_Create;
GO
CREATE PROCEDURE dbo.sp_Persoane_Create
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100),
    @CNP NVARCHAR(13) = NULL,
    @DataNasterii DATE = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Adresa NVARCHAR(500) = NULL,
    @Oras NVARCHAR(100) = NULL,
    @Judet NVARCHAR(100) = NULL,
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(100) = 'Romania',
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO [dbo].[Persoane] (
        Nume, Prenume, CNP, DataNasterii, Email, Telefon,
        Adresa, Oras, Judet, CodPostal, Tara, CreatedBy
    )
    VALUES (
        @Nume, @Prenume, @CNP, @DataNasterii, @Email, @Telefon,
        @Adresa, @Oras, @Judet, @CodPostal, @Tara, @CreatedBy
    );
    
    SELECT SCOPE_IDENTITY() AS Id;
END
GO

-- Update Persoana
IF OBJECT_ID('dbo.sp_Persoane_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Persoane_Update;
GO
CREATE PROCEDURE dbo.sp_Persoane_Update
    @Id INT,
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100),
    @CNP NVARCHAR(13) = NULL,
    @DataNasterii DATE = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Adresa NVARCHAR(500) = NULL,
    @Oras NVARCHAR(100) = NULL,
    @Judet NVARCHAR(100) = NULL,
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(100) = 'Romania',
    @IsActive BIT = 1,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE [dbo].[Persoane] SET
        Nume = @Nume,
        Prenume = @Prenume,
        CNP = @CNP,
        DataNasterii = @DataNasterii,
        Email = @Email,
        Telefon = @Telefon,
        Adresa = @Adresa,
        Oras = @Oras,
        Judet = @Judet,
        CodPostal = @CodPostal,
        Tara = @Tara,
        IsActive = @IsActive,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- Delete Persoana (Soft Delete)
IF OBJECT_ID('dbo.sp_Persoane_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Persoane_Delete;
GO
CREATE PROCEDURE dbo.sp_Persoane_Delete
    @Id INT,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE [dbo].[Persoane] SET
        IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- Hard Delete Persoana
IF OBJECT_ID('dbo.sp_Persoane_HardDelete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Persoane_HardDelete;
GO
CREATE PROCEDURE dbo.sp_Persoane_HardDelete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DELETE FROM [dbo].[Persoane] WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- Search Persoane
IF OBJECT_ID('dbo.sp_Persoane_Search', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Persoane_Search;
GO
CREATE PROCEDURE dbo.sp_Persoane_Search
    @SearchTerm NVARCHAR(200) = NULL,
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM [dbo].[vw_Persoane]
    WHERE (@IncludeInactive = 1 OR IsActive = 1)
      AND (@SearchTerm IS NULL 
           OR Nume LIKE '%' + @SearchTerm + '%'
           OR Prenume LIKE '%' + @SearchTerm + '%'
           OR NumeComplet LIKE '%' + @SearchTerm + '%'
           OR CNP LIKE '%' + @SearchTerm + '%'
           OR Email LIKE '%' + @SearchTerm + '%'
           OR Telefon LIKE '%' + @SearchTerm + '%')
    ORDER BY Nume, Prenume;
END
GO

-- =============================================
-- FUNCTION: Check CNP Validity
-- =============================================
IF OBJECT_ID('dbo.fn_ValidateCNP', 'FN') IS NOT NULL DROP FUNCTION dbo.fn_ValidateCNP;
GO
CREATE FUNCTION dbo.fn_ValidateCNP(@CNP NVARCHAR(13))
RETURNS BIT
AS
BEGIN
    DECLARE @IsValid BIT = 0;
    
    -- Check length
    IF LEN(@CNP) <> 13 RETURN 0;
    
    -- Check if all characters are digits
    IF @CNP LIKE '%[^0-9]%' RETURN 0;
    
    -- Validate control digit
    DECLARE @ControlKey VARCHAR(12) = '279146358279';
    DECLARE @Sum INT = 0;
    DECLARE @i INT = 1;
    
    WHILE @i <= 12
    BEGIN
        SET @Sum = @Sum + (CAST(SUBSTRING(@CNP, @i, 1) AS INT) * CAST(SUBSTRING(@ControlKey, @i, 1) AS INT));
        SET @i = @i + 1;
    END
    
    DECLARE @Rest INT = @Sum % 11;
    DECLARE @ControlDigit INT = CASE WHEN @Rest = 10 THEN 1 ELSE @Rest END;
    
    IF @ControlDigit = CAST(SUBSTRING(@CNP, 13, 1) AS INT)
        SET @IsValid = 1;
    
    RETURN @IsValid;
END
GO

PRINT 'Persoane objects created successfully.';
GO
