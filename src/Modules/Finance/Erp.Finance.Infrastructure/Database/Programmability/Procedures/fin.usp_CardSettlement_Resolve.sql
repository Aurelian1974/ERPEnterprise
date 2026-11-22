CREATE OR ALTER PROCEDURE fin.usp_CardSettlement_Resolve
    @CompanyId  int,
    @Id         int,
    @Note       nvarchar(500),
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Explicatia unei decontari cu diferenta (02.02, F2 4.6 pasul 3: retur pe card, tranzactie contestata): devine Decontata, cu nota.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @SettlementCompanyId int, @Version binary(8);
    SELECT @Status = Status, @SettlementCompanyId = CompanyId, @Version = RowVersion FROM fin.CardSettlementExpected WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @Status IS NULL OR @SettlementCompanyId <> @CompanyId
        THROW 51021, N'Decontarea de card nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Decontarea de card a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 3
        THROW 51021, N'Doar o decontare cu diferență se explică.', 1;

    UPDATE fin.CardSettlementExpected SET Status = 2, Note = @Note, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    SELECT RowVersion FROM fin.CardSettlementExpected WHERE Id = @Id;
END;
