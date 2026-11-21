CREATE OR ALTER PROCEDURE core.usp_Partner_Save
    @Id                  int = NULL,
    @Code                nvarchar(30),
    @Name                nvarchar(200),
    @PartnerType         tinyint,
    @FiscalCode          nvarchar(30) = NULL,
    @PersonalIdChange    bit = 0,
    @PersonalIdEncrypted varbinary(256) = NULL,
    @IsCustomer          bit,
    @IsSupplier          bit,
    @PartnerGroupId      int = NULL,
    @VatPayer            bit,
    @VatOnCollection     bit,
    @AnafInactive        bit,
    @AnafCheckedAt       datetime2(3) = NULL,
    @TradeRegisterNo     nvarchar(30) = NULL,
    @Address             nvarchar(300),
    @City                nvarchar(100),
    @CountyCode          nvarchar(3) = NULL,
    @CountryCode         char(2),
    @PostalCode          nvarchar(10) = NULL,
    @Phone               nvarchar(30) = NULL,
    @Email               nvarchar(254) = NULL,
    @IntraGroupCompanyId int = NULL,
    @IsActive            bit,
    @SaveProperties      bit = 0,
    @Properties          core.PropertyValueList READONLY,
    @SaveOwner           bit = 0,
    @OwnerCompanyId      int = NULL,
    @OwnerBranchId       int = NULL,
    @OwnerLocationId     int = NULL,
    @RowVersion          binary(8) = NULL,
    @UserId              int
AS
BEGIN
    -- Partenerul (02.01, 3.2): cod unic fara diferenta de majuscule, un CUI o singura data in nomenclator, adresa din
    -- nomenclatoare. Cu @SaveProperties = 1, valorile proprietatilor partenerului se inlocuiesc cu @Properties (validate dupa
    -- tip in aplicatie; aici: definitia apartine partenerilor, e activa, iar optiunea ii apartine). Ruleaza in tranzactia comenzii.
    -- @PersonalIdChange = 0 pastreaza CNP-ul criptat existent (fisa nu il primeste inapoi in clar). @AnafCheckedAt NULL pastreaza
    -- data ultimei verificari ANAF; altfel e momentul interogarii din care s-au preluat datele.
    -- Proprietarul (21.11.2026): NULL = comun; altfel firma, cu punctul de lucru sau locatia ei. Un articol / partener nou il
    -- primeste mereu; la modificare se schimba doar cu @SaveOwner = 1 (importul il pastreaza). Accesul se verifica inainte, in
    -- core.usp_OwnerAccess_Check.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Country WHERE Code = @CountryCode AND IsActive = 1)
       OR (@CountryCode = 'RO' AND @CountyCode IS NULL)
       OR (@CountyCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.County WHERE Code = @CountyCode AND CountryCode = @CountryCode AND IsActive = 1))
        THROW 50022, N'Țara sau județul nu există în nomenclator.', 1;

    IF @PartnerGroupId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.PartnerGroup WHERE Id = @PartnerGroupId AND IsActive = 1)
        THROW 50021, N'Grupa de parteneri aleasă nu există sau este inactivă.', 1;

    IF @IntraGroupCompanyId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @IntraGroupCompanyId)
        THROW 50021, N'Firma din grup aleasă nu există.', 1;

    IF EXISTS (SELECT 1 FROM core.Partner WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul partenerului există deja.', 1;

    -- Un CUI o singura data in toata baza (21.11.2026): mesajul spune unde exista partenerul, care se poate folosi pe documente.
    IF @FiscalCode IS NOT NULL
       AND EXISTS (SELECT 1 FROM core.Partner WITH (UPDLOCK, HOLDLOCK) WHERE FiscalCode = @FiscalCode AND (@Id IS NULL OR Id <> @Id))
    BEGIN
        DECLARE @Existing nvarchar(2000) = (
            SELECT TOP (1) FORMATMESSAGE(N'Partenerul „%s” (cod %s) cu acest CUI există deja %s și poate fi folosit pe facturi, NIR și celelalte documente.',
                   p.Name, p.Code,
                   CASE WHEN p.OwnerCompanyId IS NULL THEN N'ca partener comun al grupului'
                        ELSE N'în ' + COALESCE(l.Name, b.Name, c.Name) + CASE WHEN l.Id IS NOT NULL OR b.Id IS NOT NULL THEN N' (' + c.Name + N')' ELSE N'' END END)
            FROM core.Partner AS p
            LEFT JOIN core.Company AS c ON c.Id = p.OwnerCompanyId
            LEFT JOIN core.Branch AS b ON b.Id = p.OwnerBranchId
            LEFT JOIN core.Location AS l ON l.Id = p.OwnerLocationId
            WHERE p.FiscalCode = @FiscalCode AND (@Id IS NULL OR p.Id <> @Id)
            ORDER BY p.Id);
        THROW 50002, @Existing, 1;
    END;

    IF @SaveProperties = 1 AND EXISTS (
        SELECT 1
        FROM @Properties AS p
        LEFT JOIN core.PropertyDefinition AS d ON d.Id = p.PropertyDefinitionId AND d.EntityType = 'Partner' AND d.IsActive = 1
        LEFT JOIN core.PropertyOption AS o ON o.Id = p.ValueOptionId AND o.PropertyDefinitionId = p.PropertyDefinitionId
        LEFT JOIN core.[User] AS u ON u.Id = p.ValueUserId
        WHERE d.Id IS NULL
           OR (p.ValueOptionId IS NOT NULL AND o.Id IS NULL)
           OR (p.ValueUserId IS NOT NULL AND u.Id IS NULL))
        THROW 50021, N'O proprietate sau o valoare aleasă nu există.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Partner (Code, Name, PartnerType, FiscalCode, PersonalIdEncrypted, IsCustomer, IsSupplier, PartnerGroupId,
            VatPayer, VatOnCollection, AnafInactive, AnafCheckedAt, TradeRegisterNo, Address, City, CountyCode, CountryCode, PostalCode, Phone, Email,
            IntraGroupCompanyId, IsActive, OwnerCompanyId, OwnerBranchId, OwnerLocationId, CreatedBy)
        VALUES (@Code, @Name, @PartnerType, @FiscalCode, @PersonalIdEncrypted, @IsCustomer, @IsSupplier, @PartnerGroupId,
            @VatPayer, @VatOnCollection, @AnafInactive, @AnafCheckedAt, @TradeRegisterNo, @Address, @City, @CountyCode, @CountryCode, @PostalCode, @Phone, @Email,
            @IntraGroupCompanyId, @IsActive, @OwnerCompanyId, @OwnerBranchId, @OwnerLocationId, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.Partner
        SET Code = @Code, Name = @Name, PartnerType = @PartnerType, FiscalCode = @FiscalCode,
            PersonalIdEncrypted = CASE WHEN @PartnerType <> 4 THEN NULL WHEN @PersonalIdChange = 1 THEN @PersonalIdEncrypted ELSE PersonalIdEncrypted END,
            IsCustomer = @IsCustomer, IsSupplier = @IsSupplier, PartnerGroupId = @PartnerGroupId, VatPayer = @VatPayer,
            VatOnCollection = @VatOnCollection, AnafInactive = @AnafInactive, AnafCheckedAt = COALESCE(@AnafCheckedAt, AnafCheckedAt), TradeRegisterNo = @TradeRegisterNo, Address = @Address,
            City = @City, CountyCode = @CountyCode, CountryCode = @CountryCode, PostalCode = @PostalCode, Phone = @Phone, Email = @Email,
            IntraGroupCompanyId = @IntraGroupCompanyId, IsActive = @IsActive,
            OwnerCompanyId = CASE WHEN @SaveOwner = 1 THEN @OwnerCompanyId ELSE OwnerCompanyId END,
            OwnerBranchId = CASE WHEN @SaveOwner = 1 THEN @OwnerBranchId ELSE OwnerBranchId END,
            OwnerLocationId = CASE WHEN @SaveOwner = 1 THEN @OwnerLocationId ELSE OwnerLocationId END,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @Id)
                THROW 50027, N'Partenerul nu există.', 1;
            THROW 50006, N'Partenerul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    IF @SaveProperties = 1
    BEGIN
        DELETE v
        FROM core.PropertyValue AS v
        JOIN core.PropertyDefinition AS d ON d.Id = v.PropertyDefinitionId AND d.EntityType = 'Partner'
        WHERE v.EntityId = @Id;

        INSERT INTO core.PropertyValue (PropertyDefinitionId, EntityId, ValueText, ValueNumber, ValueDate, ValueBool, ValueOptionId, ValueUserId, CreatedBy)
        SELECT p.PropertyDefinitionId, @Id, p.ValueText, p.ValueNumber, p.ValueDate, p.ValueBool, p.ValueOptionId, p.ValueUserId, @UserId
        FROM @Properties AS p;
    END;

    SELECT Id, RowVersion FROM core.Partner WHERE Id = @Id;
END;
