CREATE OR ALTER PROCEDURE core.usp_CompanyAddress_Save
    @Id          int = NULL,
    @CompanyId   int,
    @AddressType int,
    @Name        nvarchar(100) = NULL,
    @Address     nvarchar(300),
    @City        nvarchar(100),
    @CountyCode  nvarchar(3) = NULL,
    @CountryCode char(2),
    @PostalCode  nvarchar(10) = NULL,
    @Phone       nvarchar(30) = NULL,
    @Email       nvarchar(254) = NULL,
    @IsDefault   bit,
    @IsActive    bit,
    @RowVersion  binary(8) = NULL,
    @UserId      int
AS
BEGIN
    -- Adresa firmei pe tip (02.01, 3.1): o singura adresa implicita pe firma si tip; alegerea uneia o inlocuieste pe cea veche.
    -- Sediul social nu se salveaza aici, ci pe core.Company.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma nu există.', 1;

    -- Un tip inactiv nu se mai alege pentru adrese noi; adresele existente pe el se pot edita doar fara a schimba tipul.
    IF NOT EXISTS (SELECT 1 FROM core.AddressType t WHERE t.Id = @AddressType AND (t.IsActive = 1
            OR EXISTS (SELECT 1 FROM core.CompanyAddress a WHERE a.Id = @Id AND a.AddressType = t.Id)))
        THROW 50021, N'Tipul de adresă nu există sau este inactiv.', 1;

    IF @IsDefault = 1
        UPDATE core.CompanyAddress WITH (UPDLOCK, HOLDLOCK)
        SET IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE CompanyId = @CompanyId AND AddressType = @AddressType AND IsDefault = 1 AND (@Id IS NULL OR Id <> @Id);

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.CompanyAddress (CompanyId, AddressType, Name, Address, City, CountyCode,         CountryCode, PostalCode, Phone, Email, IsDefault, IsActive, CreatedBy)
                VALUES (@CompanyId, @AddressType, @Name, @Address, @City, @CountyCode, @CountryCode, @PostalCode, @Phone, @Email, @IsDefault, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.CompanyAddress
        SET AddressType = @AddressType, Name = @Name, Address = @Address, City = @City, CountyCode = @CountyCode, CountryCode = @CountryCode,
            PostalCode = @PostalCode, Phone = @Phone, Email = @Email, IsDefault = @IsDefault, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.CompanyAddress WHERE Id = @Id AND CompanyId = @CompanyId)
                THROW 50027, N'Adresa nu există în această firmă.', 1;
            THROW 50006, N'Adresa a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.CompanyAddress WHERE Id = @Id;
END;
