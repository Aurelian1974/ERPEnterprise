CREATE OR ALTER PROCEDURE core.usp_Location_Save
    @Id           int = NULL,
    @CompanyId    int,
    @BranchId     int = NULL,
    @Code         nvarchar(20),
    @Name         nvarchar(200),
    @LocationType tinyint,
    @Address      nvarchar(300) = NULL,
    @City         nvarchar(100) = NULL,
    @CountyCode   nvarchar(3) = NULL,
    @CountryCode  char(2) = NULL,
    @PostalCode   nvarchar(10) = NULL,
    @Phone        nvarchar(30) = NULL,
    @Email        nvarchar(254) = NULL,
    @IsActive     bit,
    @RowVersion   binary(8) = NULL,
    @UserId       int
AS
BEGIN
    -- Locatie (02.01, 3.1): sub un punct de lucru al aceleiasi firme sau direct sub firma; codul unic in firma;
    -- adresa optionala (implicit cea a parintelui), dar completa daca se da tara. Nu are sub-locatii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId AND IsActive = 1)
        THROW 50021, N'Firma nu există sau este inactivă.', 1;

    IF @BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @BranchId AND CompanyId = @CompanyId AND IsActive = 1)
        THROW 50021, N'Punctul de lucru nu aparține firmei sau este inactiv.', 1;

    IF @CountryCode IS NOT NULL AND (
        NOT EXISTS (SELECT 1 FROM core.Country WHERE Code = @CountryCode AND IsActive = 1)
        OR (@CountryCode = 'RO' AND @CountyCode IS NULL)
        OR (@CountyCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.County WHERE Code = @CountyCode AND CountryCode = @CountryCode AND IsActive = 1)))
        THROW 50022, N'Țara sau județul nu există în nomenclator.', 1;

    IF @CountryCode IS NULL AND @CountyCode IS NOT NULL
        THROW 50022, N'Alegeți țara pentru județul dat.', 1;

    IF EXISTS (SELECT 1 FROM core.Location WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul locației există deja în firmă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Location (CompanyId, BranchId, Code, Name, LocationType, Address, City, CountyCode, CountryCode,         PostalCode, Phone, Email, IsActive, CreatedBy)
                VALUES (@CompanyId, @BranchId, @Code, @Name, @LocationType, @Address, @City, @CountyCode, @CountryCode, @PostalCode, @Phone, @Email, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.Location
        SET BranchId = @BranchId, Code = @Code, Name = @Name, LocationType = @LocationType, Address = @Address, City = @City,
            CountyCode = @CountyCode, CountryCode = @CountryCode, PostalCode = @PostalCode, Phone = @Phone, Email = @Email, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.Location WHERE Id = @Id AND CompanyId = @CompanyId)
                THROW 50027, N'Locația nu există în această firmă.', 1;
            THROW 50006, N'Locația a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.Location WHERE Id = @Id;
END;
