CREATE OR ALTER PROCEDURE fin.usp_ReferenceRate_Save
    @ValidFrom date,
    @Rate      decimal(9,4),
    @Delete    bit = 0,
    @UserId    int
AS
BEGIN
    -- Rata de referinta BNR valabila de la o data (F3 3.10 regula 2, OG 13/2011): adauga, modifica sau sterge; comuna tuturor firmelor.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Delete = 1
        DELETE FROM fin.ReferenceRate WHERE ValidFrom = @ValidFrom;
    ELSE IF EXISTS (SELECT 1 FROM fin.ReferenceRate WITH (UPDLOCK, HOLDLOCK) WHERE ValidFrom = @ValidFrom)
        UPDATE fin.ReferenceRate SET Rate = @Rate, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE ValidFrom = @ValidFrom;
    ELSE
        INSERT INTO fin.ReferenceRate (ValidFrom, Rate, CreatedBy) VALUES (@ValidFrom, @Rate, @UserId);
END;
