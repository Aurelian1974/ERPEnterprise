CREATE OR ALTER PROCEDURE fin.usp_BankStatement_AcceptGap
    @CompanyId       int,
    @BankStatementId int,
    @Reason          nvarchar(500),
    @RowVersion      binary(8),
    @UserId          int
AS
BEGIN
    -- Acceptarea golului de continuitate al unui extras (02.02, F2 3.3), cu motiv: liniile lui se pot valida apoi.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @StatementCompanyId int, @Continuity tinyint, @Version binary(8);
    SELECT @StatementCompanyId = CompanyId, @Continuity = ContinuityStatus, @Version = RowVersion
    FROM fin.BankStatement WITH (UPDLOCK, ROWLOCK) WHERE Id = @BankStatementId;

    IF @StatementCompanyId IS NULL OR @StatementCompanyId <> @CompanyId
        THROW 51224, N'Extrasul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Extrasul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Continuity <> 2
        THROW 51021, N'Extrasul nu are un gol de continuitate de acceptat.', 1;

    UPDATE fin.BankStatement
    SET ContinuityStatus = 3, GapReason = @Reason, GapAcceptedAt = SYSUTCDATETIME(), GapAcceptedBy = @UserId, ModifiedAt = SYSUTCDATETIME(),
        ModifiedBy = @UserId
    WHERE Id = @BankStatementId;

    SELECT RowVersion FROM fin.BankStatement WHERE Id = @BankStatementId;
END;
