CREATE OR ALTER PROCEDURE fin.usp_PenaltyCalculation_Change
    @Id          int,
    @CompanyId   int,
    @Status      tinyint = NULL,
    @RemoveLines nvarchar(max) = NULL,
    @RowVersion  binary(8),
    @UserId      int
AS
BEGIN
    -- Pe un calcul ciorna (F3 4.7 pasul 2): scoate liniile date (JSON cu Id-uri) sau schimba starea — 2 Aprobat, 4 Anulat. Altfel 51423;
    -- inexistent 51422; versiune diferita 51417. Intoarce versiunea noua.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Current tinyint, @Version binary(8);
    SELECT @Current = Status, @Version = RowVersion FROM fin.PenaltyCalculation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Current IS NULL
        THROW 51422, N'Calculul de penalități nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51417, N'Calculul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Current <> 1 OR (@Status IS NOT NULL AND @Status NOT IN (2, 4))
        THROW 51423, N'Doar un calcul ciornă se poate modifica, aproba sau anula.', 1;

    IF @RemoveLines IS NOT NULL
        DELETE FROM fin.PenaltyCalculationLine
        WHERE CalculationId = @Id AND Id IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(@RemoveLines) AS j);

    UPDATE fin.PenaltyCalculation
    SET Status = ISNULL(@Status, Status),
        ApprovedAt = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() ELSE ApprovedAt END, ApprovedBy = CASE WHEN @Status = 2 THEN @UserId ELSE ApprovedBy END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.RowVersion
    WHERE Id = @Id;
END;
