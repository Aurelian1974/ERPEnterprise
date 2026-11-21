CREATE OR ALTER PROCEDURE core.usp_Company_Save
    @Id                       int = NULL,
    @GroupId                  int = NULL,
    @Code                     nvarchar(20),
    @Name                     nvarchar(200),
    @FiscalCode               nvarchar(20),
    @VatPayer                 bit,
    @VatOnCollection          bit,
    @TradeRegisterNo          nvarchar(30),
    @ShareCapital             decimal(19,2) = NULL,
    @Address                  nvarchar(300),
    @City                     nvarchar(100),
    @CountyCode               nvarchar(3) = NULL,
    @CountryCode              char(2),
    @PostalCode               nvarchar(10) = NULL,
    @Phone                    nvarchar(30) = NULL,
    @Email                    nvarchar(254) = NULL,
    @CitySirutaCode           int = NULL,
    @EffectiveFrom            date = NULL,
    @Today                    date = NULL,
    @DefaultPrecisionPolicyId int,
    @IncludeInConsolidation   bit,
    @IsActive                 bit,
    @RowVersion               binary(8) = NULL,
    @UserId                   int
AS
BEGIN
    -- Firma (02.01, 3.1): cod unic in instalare (fara diferenta de majuscule), CUI unic, adresa din nomenclatoare;
    -- o firma cu puncte de lucru sau locatii active nu se dezactiveaza. Ruleaza in tranzactia comenzii.
    -- Datele cu istoric (3.6 punctul 2: denumire, sediu, regim TVA, registru, capital) se salveaza ca versiune de la @EffectiveFrom
    -- (implicit azi; o firma noua incepe la 01.01.1900); core.Company ramane la versiunea valabila azi (usp_CompanyVersion_Apply).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Country WHERE Code = @CountryCode AND IsActive = 1)
       OR (@CountryCode = 'RO' AND @CountyCode IS NULL)
       OR (@CountyCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.County WHERE Code = @CountyCode AND CountryCode = @CountryCode AND IsActive = 1))
        THROW 50022, N'Țara sau județul nu există în nomenclator.', 1;

    IF @GroupId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.CompanyGroup WHERE Id = @GroupId AND IsActive = 1)
        THROW 50021, N'Grupul ales nu există sau este inactiv.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.PrecisionPolicy WHERE Id = @DefaultPrecisionPolicyId AND IsActive = 1)
        THROW 50021, N'Politica de zecimale aleasă nu există sau este inactivă.', 1;

    IF EXISTS (SELECT 1 FROM core.Company WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul firmei există deja.', 1;

    IF EXISTS (SELECT 1 FROM core.Company WITH (UPDLOCK, HOLDLOCK) WHERE FiscalCode = @FiscalCode AND (@Id IS NULL OR Id <> @Id))
        THROW 50019, N'Există deja o firmă cu acest CUI.', 1;

    -- Localitatea SIRUTA (02.01, 3.5): codul ales se verifica, fara cod se cauta dupa nume (usp_City_Resolve).
    EXEC core.usp_City_Resolve @CountryCode, @CountyCode, @City OUTPUT, @CitySirutaCode OUTPUT;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Company (GroupId, Code, Name, FiscalCode, VatPayer, VatOnCollection, TradeRegisterNo, ShareCapital,
            Address, City, CitySirutaCode, CountyCode, CountryCode, PostalCode, Phone, Email, DefaultPrecisionPolicyId, IncludeInConsolidation, IsActive, CreatedBy)
        VALUES (@GroupId, @Code, @Name, @FiscalCode, @VatPayer, @VatOnCollection, @TradeRegisterNo, @ShareCapital,
            @Address, @City, @CitySirutaCode, @CountyCode, @CountryCode, @PostalCode, @Phone, @Email, @DefaultPrecisionPolicyId, @IncludeInConsolidation, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND (
            EXISTS (SELECT 1 FROM core.Branch WHERE CompanyId = @Id AND IsActive = 1)
            OR EXISTS (SELECT 1 FROM core.Location WHERE CompanyId = @Id AND IsActive = 1))
            THROW 50020, N'Firma are puncte de lucru sau locații active; dezactivați-le întâi.', 1;

        -- Firma-mama a unui grup ramane in acel grup (sau intr-un subgrup al lui).
        IF EXISTS (SELECT 1 FROM core.CompanyGroup WHERE ParentCompanyId = @Id)
           AND NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @Id AND GroupId IS NOT DISTINCT FROM @GroupId)
            THROW 50021, N'Firma este firma-mamă a unui grup; alegeți întâi altă firmă-mamă pentru grup.', 1;

        UPDATE core.Company
        SET GroupId = @GroupId, Code = @Code, Name = @Name, FiscalCode = @FiscalCode, VatPayer = @VatPayer,
            VatOnCollection = @VatOnCollection, TradeRegisterNo = @TradeRegisterNo, ShareCapital = @ShareCapital,
            Address = @Address, City = @City, CitySirutaCode = @CitySirutaCode, CountyCode = @CountyCode, CountryCode = @CountryCode, PostalCode = @PostalCode,
            Phone = @Phone, Email = @Email,
            DefaultPrecisionPolicyId = @DefaultPrecisionPolicyId, IncludeInConsolidation = @IncludeInConsolidation,
            IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @Id)
                THROW 50027, N'Firma nu există.', 1;
            THROW 50006, N'Firma a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SET @Today = ISNULL(@Today, CAST(SYSUTCDATETIME() AS date));
    IF NOT EXISTS (SELECT 1 FROM core.CompanyVersion WHERE CompanyId = @Id)
        SET @EffectiveFrom = '19000101';
    SET @EffectiveFrom = ISNULL(@EffectiveFrom, @Today);

    MERGE core.CompanyVersion AS target
    USING (SELECT @Id AS CompanyId, @EffectiveFrom AS ValidFrom) AS source ON target.CompanyId = source.CompanyId AND target.ValidFrom = source.ValidFrom
    WHEN MATCHED THEN UPDATE SET Name = @Name, VatPayer = @VatPayer, VatOnCollection = @VatOnCollection, TradeRegisterNo = @TradeRegisterNo,
        ShareCapital = @ShareCapital, Address = @Address, City = @City, CitySirutaCode = @CitySirutaCode, CountyCode = @CountyCode,
        CountryCode = @CountryCode, PostalCode = @PostalCode, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHEN NOT MATCHED THEN INSERT (CompanyId, ValidFrom, Name, VatPayer, VatOnCollection, TradeRegisterNo, ShareCapital, Address, City, CitySirutaCode,
        CountyCode, CountryCode, PostalCode, ModifiedAt, ModifiedBy)
        VALUES (@Id, @EffectiveFrom, @Name, @VatPayer, @VatOnCollection, @TradeRegisterNo, @ShareCapital, @Address, @City, @CitySirutaCode,
        @CountyCode, @CountryCode, @PostalCode, SYSUTCDATETIME(), @UserId);

    -- O schimbare programata pentru viitor nu se vede inca pe firma: firma ramane la versiunea de azi.
    DECLARE @Applied TABLE (CompanyId int);
    INSERT INTO @Applied (CompanyId) EXEC core.usp_CompanyVersion_Apply @Today, @Id;

    SELECT Id, RowVersion FROM core.Company WHERE Id = @Id;
END;
