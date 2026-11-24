CREATE OR ALTER PROCEDURE fin.usp_PostingQueue_Due
    @CompanyId   int = NULL,
    @Id          bigint = NULL,
    @Reason      tinyint = NULL,
    @From        date = NULL,
    @To          date = NULL,
    @OlderThan   datetime2(3) = NULL,
    @SkipClosedPeriod bit = 0,
    @Take        int = 500
AS
BEGIN
    -- Randurile in asteptare de reincercat (C2 3.7, 4.4): unul (Id), ale unei firme pe motiv sau pe interval de date (redeschiderea lunii), sau
    -- toate cele incercate ultima oara inainte de @OlderThan (jobul de reincercare, fara lunile inchise). In ordinea datei contabile, ca originalul sa treaca
    -- inaintea stornarii lui.
    SET NOCOUNT ON;

    SELECT TOP (@Take) Id, EventId, EventType, CompanyId, Payload
    FROM fin.PostingQueue
    WHERE Status = 1 AND (@CompanyId IS NULL OR CompanyId = @CompanyId) AND (@Id IS NULL OR Id = @Id) AND (@Reason IS NULL OR Reason = @Reason)
      AND (@From IS NULL OR AccountingDate >= @From) AND (@To IS NULL OR AccountingDate <= @To) AND (@OlderThan IS NULL OR LastAttemptAt < @OlderThan)
      AND (@SkipClosedPeriod = 0 OR Reason <> 4)
    ORDER BY AccountingDate, Id;
END;
