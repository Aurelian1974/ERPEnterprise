CREATE OR ALTER PROCEDURE fin.usp_OpeningBalanceImport_Cancel
    @CompanyId  int,
    @Id         int,
    @UserId     int
AS
BEGIN
    -- Anularea unui import validat (C3 3.7 pasul 5): doar cat timp luna de start nu are nicio alta nota (51916). Notele importului se sterg
    -- (numerele lor raman goale in jurnalul IN), lunile inchise ca importate se redeschid.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @Start date, @Status tinyint, @Opening bigint, @Turnover bigint;
    SELECT @Start = StartDate, @Status = Status, @Opening = OpeningEntryId, @Turnover = TurnoverEntryId
    FROM fin.OpeningBalanceImport WITH (UPDLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL OR @Status <> 2
        THROW 51916, N'Importul nu e validat.', 1;
    IF EXISTS (SELECT 1 FROM fin.JournalEntry WHERE CompanyId = @CompanyId AND AccountingDate BETWEEN @Start AND EOMONTH(@Start)
               AND Id NOT IN (ISNULL(@Opening, 0), ISNULL(@Turnover, 0)))
        THROW 51916, N'Luna de start are deja note; importul nu se mai poate anula.', 1;

    UPDATE fin.OpeningBalanceImport SET Status = 3, OpeningEntryId = NULL, TurnoverEntryId = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    DELETE FROM fin.JournalEntry WHERE Id IN (@Opening, @Turnover);
    UPDATE fin.AccountingPeriod SET Status = 1, IsImported = 0, ClosedAt = NULL, ClosedBy = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE CompanyId = @CompanyId AND IsImported = 1;
    COMMIT TRANSACTION;
END;
