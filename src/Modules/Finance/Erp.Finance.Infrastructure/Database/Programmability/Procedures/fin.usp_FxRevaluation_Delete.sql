CREATE OR ALTER PROCEDURE fin.usp_FxRevaluation_Delete
    @CompanyId   int,
    @RunId       int,
    @RowVersion  binary(8)
AS
BEGIN
    -- Stergerea unei rulari ciorna (F4 3.6): liniile, cursurile si rularea. Inexistenta 51601, versiune diferita 51602, nu e ciorna 51603.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8);
    SELECT @Status = Status, @Version = RowVersion FROM fin.FxRevaluationRun WITH (UPDLOCK, ROWLOCK) WHERE Id = @RunId AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 51601, N'Rularea de reevaluare nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51602, N'Rularea a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 51603, N'Doar o rulare ciornă poate fi ștearsă.', 1;

    DELETE FROM fin.FxRevaluationLine WHERE RunId = @RunId;
    DELETE FROM fin.FxRevaluationRate WHERE RunId = @RunId;
    DELETE FROM fin.FxRevaluationRun WHERE Id = @RunId;
END;
