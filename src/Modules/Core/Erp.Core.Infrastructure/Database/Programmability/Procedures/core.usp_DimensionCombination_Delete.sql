CREATE OR ALTER PROCEDURE core.usp_DimensionCombination_Delete
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Deblocarea unei combinatii (configurare, nu date de business; istoricul e in audit).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DELETE FROM core.DimensionCombination WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @@ROWCOUNT = 0
        THROW 50027, N'Combinația nu există.', 1;
END;
