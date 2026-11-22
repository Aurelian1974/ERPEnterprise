CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_Delete
    @Id         int,
    @CompanyId  int,
    @RowVersion binary(8)
AS
BEGIN
    -- Stergerea unui OP ciorna (02.02, F2 3.6); liniile se sterg in cascada. Validat: 51212.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @OrderCompanyId int, @Version binary(8);
    SELECT @Status = Status, @OrderCompanyId = CompanyId, @Version = RowVersion FROM fin.PaymentOrder WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @Status IS NULL OR @OrderCompanyId <> @CompanyId
        THROW 51225, N'Ordinul de plată nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Ordinul de plată a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 51212, N'Doar o ciornă se poate șterge; un OP validat se stornează.', 1;

    DELETE FROM fin.PaymentOrder WHERE Id = @Id;
END;
