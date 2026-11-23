CREATE OR ALTER PROCEDURE fin.usp_Compensation_Reverse
    @Id         int,
    @CompanyId  int,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Storneaza un PV validat (02.02, F3 4.4 pasul 4): starea 3; intoarce alocarile active ale PV-ului (le desface apelantul) si partidele
    -- tehnice (apelantul le stinge intre ele, Source = 3, ca soldurile sa revina). Nevalidat: 51411; alta versiune: 51417.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @HeaderCompanyId int, @Status tinyint, @Version binary(8);
    SELECT @HeaderCompanyId = CompanyId, @Status = Status, @Version = RowVersion FROM fin.Compensation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @HeaderCompanyId IS NULL OR @HeaderCompanyId <> @CompanyId
        THROW 51021, N'Procesul-verbal nu există.', 1;
    IF @Status <> 2
        THROW 51411, N'Doar un proces-verbal validat se poate storna.', 1;
    IF @Version <> @RowVersion
        THROW 51417, N'Procesul-verbal a fost modificat de alt utilizator; reîncărcați datele.', 1;

    UPDATE fin.Compensation SET Status = 3, ReversedAt = SYSUTCDATETIME(), ReversedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    SELECT a.Id FROM fin.Allocation AS a WHERE a.CompensationId = @Id AND a.Status = 1 ORDER BY a.Id;
    SELECT o.Id, o.Amount FROM fin.OpenItem AS o WHERE o.SourceDocumentType = 'Finance.Compensation' AND o.SourceDocumentId = @Id ORDER BY o.Id;
END;
