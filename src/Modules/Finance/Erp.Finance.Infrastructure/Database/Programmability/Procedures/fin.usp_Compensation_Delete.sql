CREATE OR ALTER PROCEDURE fin.usp_Compensation_Delete
    @Id         int,
    @CompanyId  int,
    @RowVersion binary(8)
AS
BEGIN
    -- Sterge un PV de compensare ciorna (F3 3.4); validat: 51411; alta versiune: 51417.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @HeaderCompanyId int, @Status tinyint, @Version binary(8);
    SELECT @HeaderCompanyId = CompanyId, @Status = Status, @Version = RowVersion FROM fin.Compensation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @HeaderCompanyId IS NULL OR @HeaderCompanyId <> @CompanyId
        THROW 51021, N'Procesul-verbal nu există.', 1;
    IF @Status <> 1
        THROW 51411, N'Doar o ciornă se poate șterge.', 1;
    IF @Version <> @RowVersion
        THROW 51417, N'Procesul-verbal a fost modificat de alt utilizator; reîncărcați datele.', 1;

    DELETE FROM fin.CompensationLine WHERE CompensationId = @Id;
    DELETE FROM fin.Compensation WHERE Id = @Id;
END;
