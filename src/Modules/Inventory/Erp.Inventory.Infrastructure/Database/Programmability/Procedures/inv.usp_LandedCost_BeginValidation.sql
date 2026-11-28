CREATE OR ALTER PROCEDURE inv.usp_LandedCost_BeginValidation
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8) = NULL
AS
BEGIN
    -- Inceputul validarii documentului CA (02.05, S3 4.2): blocat, ciorna, cu versiunea vazuta (54117, 54118). Fara versiune (portul din Achizitii,
    -- in aceeasi tranzactie cu salvarea) nu se verifica versiunea.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8);
    SELECT @Status = Status, @Version = RowVersion FROM inv.LandedCost WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @RowVersion IS NOT NULL AND @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 54118, N'Documentul nu mai este ciornă.', 1;
END;
