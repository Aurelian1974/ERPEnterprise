CREATE OR ALTER PROCEDURE fin.usp_GlAccount_Save
    @Id                 int = NULL,
    @ParentId           int = NULL,
    @Code               nvarchar(20),
    @Name               nvarchar(200),
    @CompanyId          int = NULL,
    @CurrencyMode       tinyint,
    @CurrencyCode       char(3) = NULL,
    @PartyRequirement   tinyint,
    @StatutoryReporting bit,
    @ManualPosting      bit,
    @TaxTreatment       tinyint,
    @ValidFrom          date = NULL,
    @ValidTo            date = NULL,
    @RowVersion         binary(8) = NULL,
    @UserId             int
AS
BEGIN
    -- Un cont analitic al planului romanesc (C1 3.2, 3.3): adaugare sub un cont (codul = codul parintelui + „.” + sufix, 51805), cu
    -- atributele mostenite (tipul, contul SAF-T, inchiderea, extrabilantier, gestiune) si restrictia pe firma a parintelui; sau modificare
    -- cu RowVersion (51802). Conturile livrate nu isi schimba codul si denumirea (51807). Codul e unic in plan (51803). Denumirea veche intra
    -- in istoric. Frunzele planului se recalculeaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Chart int = (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1);

    IF @Id IS NULL
    BEGIN
        DECLARE @ParentCode nvarchar(20), @ParentLevel tinyint, @ParentCompany int, @ParentActive bit;
        SELECT @ParentCode = Code, @ParentLevel = Level, @ParentCompany = CompanyId, @ParentActive = IsActive
        FROM fin.GlAccount WHERE Id = @ParentId AND ChartId = @Chart;

        IF @ParentCode IS NULL OR @ParentLevel < 3 OR @ParentActive = 0 OR @Code NOT LIKE @ParentCode + N'.%' OR LEN(@Code) <= LEN(@ParentCode) + 1
           OR (@ParentCompany IS NOT NULL AND ISNULL(@CompanyId, 0) <> @ParentCompany)
            THROW 51805, N'Analiticul se adaugă sub un cont sintetic activ, cu codul contului urmat de punct și sufix, pe firma contului părinte.', 1;

        IF EXISTS (SELECT 1 FROM fin.GlAccount WHERE ChartId = @Chart AND CodeKey = UPPER(@Code))
            THROW 51803, N'Codul există deja în planul de conturi.', 1;

        BEGIN TRANSACTION;
        INSERT INTO fin.GlAccount (ChartId, Code, Name, ParentId, Level, AccountType, CompanyId, IsOffBalance, IsManagement, StatutoryReporting,
            CurrencyMode, CurrencyCode, PartyRequirement, ManualPosting, SaftAccountCode, TaxTreatment, ClosingTarget, ValidFrom, ValidTo, CreatedBy)
        SELECT @Chart, @Code, @Name, p.Id, p.Level + 1, p.AccountType, @CompanyId, p.IsOffBalance, p.IsManagement, @StatutoryReporting,
            @CurrencyMode, @CurrencyCode, @PartyRequirement, CASE WHEN p.ManualPosting = 0 THEN 0 ELSE @ManualPosting END, p.SaftAccountCode,
            @TaxTreatment, p.ClosingTarget, @ValidFrom, @ValidTo, @UserId
        FROM fin.GlAccount AS p WHERE p.Id = @ParentId;
        SET @Id = CAST(SCOPE_IDENTITY() AS int);
        EXEC fin.usp_GlAccount_RefreshPostable @ChartId = @Chart;
        COMMIT TRANSACTION;

        SELECT a.Id, a.RowVersion FROM fin.GlAccount AS a WHERE a.Id = @Id;
        RETURN;
    END;

    DECLARE @OldCode nvarchar(20), @OldName nvarchar(200), @IsSystem bit, @OldParent int, @OldVersion binary(8), @OldCompany int;
    SELECT @OldCode = Code, @OldName = Name, @IsSystem = IsSystem, @OldParent = ParentId, @OldVersion = RowVersion, @OldCompany = CompanyId
    FROM fin.GlAccount WHERE Id = @Id AND ChartId = @Chart;

    IF @OldCode IS NULL
        THROW 51801, N'Contul nu există.', 1;
    IF @OldVersion <> @RowVersion
        THROW 51802, N'Contul a fost modificat de altcineva între timp.', 1;
    IF @IsSystem = 1 AND (@Code <> @OldCode OR @Name <> @OldName)
        THROW 51807, N'Codul și denumirea unui cont livrat se schimbă doar prin actualizarea planului.', 1;
    IF @IsSystem = 0 AND @Code <> @OldCode
    BEGIN
        IF @Code NOT LIKE (SELECT Code FROM fin.GlAccount WHERE Id = @OldParent) + N'.%'
            THROW 51805, N'Codul analiticului trebuie să înceapă cu codul contului părinte, urmat de punct.', 1;
        IF EXISTS (SELECT 1 FROM fin.GlAccount WHERE ChartId = @Chart AND CodeKey = UPPER(@Code) AND Id <> @Id)
            THROW 51803, N'Codul există deja în planul de conturi.', 1;
        IF EXISTS (SELECT 1 FROM fin.GlAccount WHERE ParentId = @Id)
            THROW 51805, N'Codul unui cont cu analitice nu se schimbă.', 1;
    END;

    BEGIN TRANSACTION;
    IF @Name <> @OldName
        INSERT INTO fin.GlAccountNameHistory (AccountId, Name, ValidFrom, ValidTo, ChangedBy)
        SELECT @Id, @OldName, ISNULL((SELECT MAX(ValidTo) FROM fin.GlAccountNameHistory WHERE AccountId = @Id), CreatedAt), SYSUTCDATETIME(), @UserId
        FROM fin.GlAccount WHERE Id = @Id;

    UPDATE fin.GlAccount
    SET Code = @Code, Name = @Name, CompanyId = CASE WHEN IsSystem = 1 THEN CompanyId ELSE @CompanyId END, CurrencyMode = @CurrencyMode,
        CurrencyCode = @CurrencyCode, PartyRequirement = @PartyRequirement, StatutoryReporting = @StatutoryReporting, ManualPosting = @ManualPosting,
        TaxTreatment = @TaxTreatment, ValidFrom = @ValidFrom, ValidTo = @ValidTo, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 51802, N'Contul a fost modificat de altcineva între timp.', 1;
    COMMIT TRANSACTION;

    SELECT a.Id, a.RowVersion FROM fin.GlAccount AS a WHERE a.Id = @Id;
END;
