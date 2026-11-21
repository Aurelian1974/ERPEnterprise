CREATE OR ALTER PROCEDURE core.usp_DimensionValue_Ensure
    @CompanyId     int,
    @DimensionType varchar(20),
    @Code          nvarchar(30),
    @Name          nvarchar(200),
    @UserId        int
AS
BEGIN
    -- Valoarea dupa cod, creata daca lipseste (IDimensionLookup.EnsureAsync, 02.01, 6): modulele care detin dimensiunea (Stocuri pentru
    -- gestiuni, Constructii pentru santiere) isi adauga valorile. Ruleaza in tranzactia comenzii modulului.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.DimensionType WHERE Code = @DimensionType)
        THROW 50021, N'Tipul de dimensiune nu există.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma nu există.', 1;

    DECLARE @Id int = (SELECT Id FROM core.DimensionValue WITH (UPDLOCK, HOLDLOCK)
                       WHERE CompanyId = @CompanyId AND DimensionType = @DimensionType AND CodeKey = UPPER(@Code));
    IF @Id IS NULL
    BEGIN
        INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy)
        VALUES (@CompanyId, @DimensionType, @Code, @Name, 1, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END;

    SELECT @Id AS Id;
END;
