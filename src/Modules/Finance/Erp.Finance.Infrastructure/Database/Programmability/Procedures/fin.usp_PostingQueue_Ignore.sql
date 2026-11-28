CREATE OR ALTER PROCEDURE fin.usp_PostingQueue_Ignore
    @CompanyId   int,
    @Id          bigint,
    @Reason      nvarchar(500),
    @RowVersion  binary(8),
    @UserId      int
AS
BEGIN
    -- Ignora un rand din coada, cu motiv (C2 3.7, dreptul Fin.Posting.Ignore): documentul ramane necontat, iar inchiderea lunii nu il mai asteapta.
    SET NOCOUNT ON;

    UPDATE fin.PostingQueue SET Status = 3, IgnoreReason = @Reason, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND CompanyId = @CompanyId AND Status = 1 AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 51842, N'Rândul nu mai e în așteptare sau a fost modificat între timp.', 1;
END;
