CREATE OR ALTER PROCEDURE core.usp_DimensionValue_Save
    @Id                int = NULL,
    @CompanyId         int,
    @DimensionType     varchar(20),
    @Code              nvarchar(30),
    @Name              nvarchar(200),
    @ParentId          int = NULL,
    @ResponsibleUserId int = NULL,
    @ValidFrom         date = NULL,
    @ValidTo           date = NULL,
    @IsActive          bit,
    @RowVersion        binary(8) = NULL,
    @UserId            int
AS
BEGIN
    -- Valoarea unei dimensiuni (02.01, 3.5; macheta 07k). Firma si tipul nu se schimba dupa creare. Firma trebuie sa fie una pe care
    -- utilizatorul are Core.Dimension.Manage. Parintele: aceeasi firma si acelasi tip, activ, fara cicluri. O valoare cu copii activi
    -- nu se dezactiveaza (50020). Cod duplicat pe firma si tip: 50018.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NOT NULL
        SELECT @CompanyId = CompanyId, @DimensionType = DimensionType FROM core.DimensionValue WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;

    IF NOT EXISTS (SELECT 1 FROM core.DimensionType WHERE Code = @DimensionType AND (IsActive = 1 OR @Id IS NOT NULL))
        THROW 50021, N'Tipul de dimensiune nu există sau este inactiv.', 1;
    IF @UserId <> 1 AND NOT EXISTS (SELECT 1 FROM core.fn_UserCompanies(@UserId, N'Core.Dimension.Manage') WHERE CompanyId = @CompanyId)
        THROW 50021, N'Nu aveți dreptul pe dimensiunile acestei firme.', 1;
    IF @ResponsibleUserId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.[User] WHERE Id = @ResponsibleUserId AND IsActive = 1 AND Id <> 1)
        THROW 50021, N'Responsabilul ales nu există sau este inactiv.', 1;
    IF @ParentId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue
                                             WHERE Id = @ParentId AND CompanyId = @CompanyId AND DimensionType = @DimensionType AND IsActive = 1)
        THROW 50021, N'Părintele trebuie să fie o valoare activă de același tip, din aceeași firmă.', 1;
    -- Ciclu: parintele ales nu poate fi chiar valoarea sau un descendent al ei.
    IF @Id IS NOT NULL AND @ParentId IS NOT NULL
    BEGIN
        WITH Up AS (
            SELECT v.Id, v.ParentId, 0 AS Depth FROM core.DimensionValue AS v WHERE v.Id = @ParentId
            UNION ALL
            SELECT v.Id, v.ParentId, u.Depth + 1 FROM core.DimensionValue AS v JOIN Up AS u ON v.Id = u.ParentId WHERE u.Depth < 50
        )
        SELECT @ParentId = CASE WHEN EXISTS (SELECT 1 FROM Up WHERE Id = @Id) THEN -1 ELSE @ParentId END;
        IF @ParentId = -1
            THROW 50021, N'Părintele ales ar crea un ciclu.', 1;
    END;

    IF EXISTS (SELECT 1 FROM core.DimensionValue WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND DimensionType = @DimensionType AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul există deja pentru acest tip de dimensiune în firmă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, ParentId, ResponsibleUserId, ValidFrom, ValidTo, IsActive, CreatedBy)
        VALUES (@CompanyId, @DimensionType, @Code, @Name, @ParentId, @ResponsibleUserId, @ValidFrom, @ValidTo, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.DimensionValue WHERE ParentId = @Id AND IsActive = 1)
            THROW 50020, N'Valoarea are valori subordonate active; dezactivați-le întâi pe ele.', 1;

        UPDATE core.DimensionValue
        SET Code = @Code, Name = @Name, ParentId = @ParentId, ResponsibleUserId = @ResponsibleUserId, ValidFrom = @ValidFrom, ValidTo = @ValidTo,
            IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.DimensionValue WHERE Id = @Id)
                THROW 50027, N'Valoarea nu există.', 1;
            THROW 50006, N'Valoarea a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.DimensionValue WHERE Id = @Id;
END;
