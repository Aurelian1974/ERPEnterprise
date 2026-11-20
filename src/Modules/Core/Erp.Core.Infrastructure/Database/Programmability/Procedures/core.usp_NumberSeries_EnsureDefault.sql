CREATE OR ALTER PROCEDURE core.usp_NumberSeries_EnsureDefault
    @CompanyId        int,
    @DocumentTypeCode nvarchar(50),
    @Year             smallint,
    @Prefix           nvarchar(20),
    @Padding          tinyint,
    @UserId           int
AS
BEGIN
    -- Seria implicita nefiscala a unui tip de document fara numerotare legala (de exemplu codul partenerului, 02.01, 3.2),
    -- creata la prima folosire, ca utilizatorul sa nu fie oprit de o serie neconfigurata. Nu face nimic daca firma are deja o
    -- serie implicita deschisa pentru tip si an, ori una din anul anterior cu trecere automata (usp_NumberSeries_Next o continua),
    -- ori o serie cu acelasi prefix si an. Intoarce 1 daca a creat seria. Ruleaza in tranzactia documentului.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @DocumentTypeId int, @IsFiscal bit;
    SELECT @DocumentTypeId = Id, @IsFiscal = IsFiscalDefault FROM core.DocumentType WHERE Code = @DocumentTypeCode;

    IF @DocumentTypeId IS NULL
        THROW 50021, N'Tipul de document nu există.', 1;

    IF @IsFiscal = 1
        THROW 50021, N'Seriile fiscale nu se creează automat; definiți-le în Configurare › Serii.', 1;

    IF EXISTS (
        SELECT 1 FROM core.NumberSeries WITH (UPDLOCK, HOLDLOCK)
        WHERE CompanyId = @CompanyId AND DocumentTypeId = @DocumentTypeId
          AND ((([Year] = @Year AND IsDefault = 1 AND ClosedAt IS NULL) OR ([Year] = @Year - 1 AND AutoRollover = 1))
               OR ([Year] = @Year AND BranchId IS NULL AND PrefixKey = UPPER(@Prefix))))
    BEGIN
        SELECT CAST(0 AS bit) AS Created;
        RETURN;
    END;

    INSERT INTO core.NumberSeries (CompanyId, BranchId, DocumentTypeId, Prefix, [Year], NextNumber, FirstNumber, Padding,
        IsFiscal, IsDefault, AutoRollover, CreatedBy)
    VALUES (@CompanyId, NULL, @DocumentTypeId, @Prefix, @Year, 1, 1, @Padding, 0, 1, 1, @UserId);

    SELECT CAST(1 AS bit) AS Created;
END;
