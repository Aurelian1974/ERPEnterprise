CREATE OR ALTER PROCEDURE core.usp_PartnerAddress_Save
    @Id          int = NULL,
    @PartnerId   int,
    @AddressType int,
    @Name        nvarchar(100) = NULL,
    @Address     nvarchar(300),
    @City        nvarchar(100),
    @CountyCode  nvarchar(3) = NULL,
    @CountryCode char(2),
    @PostalCode  nvarchar(10) = NULL,
    @Phone       nvarchar(30) = NULL,
    @Email       nvarchar(254) = NULL,
    @CitySirutaCode int = NULL,
    @IsDefault   bit,
    @IsActive    bit,
    @RowVersion  binary(8) = NULL,
    @UserId      int
AS
BEGIN
    -- Adresa partenerului pe tip (livrare, facturare, ...; 02.01, 3.2). Sediul social e pe fisa partenerului.
    -- O singura adresa implicita pe partener si tip: alegerea alteia o inlocuieste.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
        THROW 50027, N'Partenerul nu există.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.AddressType WHERE Id = @AddressType AND (IsActive = 1 OR @Id IS NOT NULL))
        THROW 50021, N'Tipul de adresă nu există sau este inactiv.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.Country WHERE Code = @CountryCode AND IsActive = 1)
       OR (@CountryCode = 'RO' AND @CountyCode IS NULL)
       OR (@CountyCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.County WHERE Code = @CountyCode AND CountryCode = @CountryCode AND IsActive = 1))
        THROW 50022, N'Țara sau județul nu există în nomenclator.', 1;

    IF @IsDefault = 1
        UPDATE core.PartnerAddress
        SET IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE PartnerId = @PartnerId AND AddressType = @AddressType AND IsDefault = 1 AND (@Id IS NULL OR Id <> @Id);

    -- Localitatea SIRUTA (02.01, 3.5): codul ales se verifica, fara cod se cauta dupa nume (usp_City_Resolve).
    EXEC core.usp_City_Resolve @CountryCode, @CountyCode, @City OUTPUT, @CitySirutaCode OUTPUT;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.PartnerAddress (PartnerId, AddressType, Name, Address, City, CitySirutaCode, CountyCode, CountryCode, PostalCode, Phone, Email,
            IsDefault, IsActive, CreatedBy)
        VALUES (@PartnerId, @AddressType, @Name, @Address, @City, @CitySirutaCode, @CountyCode, @CountryCode, @PostalCode, @Phone, @Email,
            @IsDefault, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.PartnerAddress
        SET AddressType = @AddressType, Name = @Name, Address = @Address, City = @City, CitySirutaCode = @CitySirutaCode, CountyCode = @CountyCode, CountryCode = @CountryCode,
            PostalCode = @PostalCode, Phone = @Phone, Email = @Email, IsDefault = @IsDefault, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND PartnerId = @PartnerId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.PartnerAddress WHERE Id = @Id AND PartnerId = @PartnerId)
                THROW 50027, N'Adresa nu există la acest partener.', 1;
            THROW 50006, N'Adresa a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.PartnerAddress WHERE Id = @Id;
END;
