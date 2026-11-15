CREATE OR ALTER PROCEDURE core.usp_Branch_Save
    @Id          int = NULL,
    @CompanyId   int,
    @Code        nvarchar(20),
    @Name        nvarchar(200),
    @Address     nvarchar(300),
    @City        nvarchar(100),
    @CountyCode  nvarchar(3) = NULL,
    @CountryCode char(2),
    @PostalCode  nvarchar(10) = NULL,
    @IsActive    bit,
    @RowVersion  binary(8) = NULL,
    @UserId      int
AS
BEGIN
    -- Punct de lucru (02.01, 3.1): codul unic in firma; nu se muta in alta firma; cu locatii active nu se dezactiveaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId AND IsActive = 1)
        THROW 50021, N'Firma nu există sau este inactivă.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.Country WHERE Code = @CountryCode AND IsActive = 1)
       OR (@CountryCode = 'RO' AND @CountyCode IS NULL)
       OR (@CountyCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.County WHERE Code = @CountyCode AND CountryCode = @CountryCode AND IsActive = 1))
        THROW 50022, N'Țara sau județul nu există în nomenclator.', 1;

    IF EXISTS (SELECT 1 FROM core.Branch WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul punctului de lucru există deja în firmă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Branch (CompanyId, Code, Name, Address, City, CountyCode, CountryCode, PostalCode, IsActive, CreatedBy)
        VALUES (@CompanyId, @Code, @Name, @Address, @City, @CountyCode, @CountryCode, @PostalCode, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.Location WHERE BranchId = @Id AND IsActive = 1)
            THROW 50020, N'Punctul de lucru are locații active; dezactivați-le întâi.', 1;

        UPDATE core.Branch
        SET Code = @Code, Name = @Name, Address = @Address, City = @City, CountyCode = @CountyCode, CountryCode = @CountryCode,
            PostalCode = @PostalCode, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @Id AND CompanyId = @CompanyId)
                THROW 50027, N'Punctul de lucru nu există în această firmă.', 1;
            THROW 50006, N'Punctul de lucru a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.Branch WHERE Id = @Id;
END;
