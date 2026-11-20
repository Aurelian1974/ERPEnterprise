CREATE OR ALTER PROCEDURE core.usp_ImportJob_Get
    @Id        int,
    @ForUpdate bit = 0,
    @WithData  bit = 1
AS
BEGIN
    -- Un job de import; @ForUpdate blocheaza randul pana la sfarsitul tranzactiei (un singur lot o data, la confirmare).
    SET NOCOUNT ON;

    IF @ForUpdate = 1
        SELECT j.Id, j.EntityType, j.CompanyId, j.FileName, j.Status, j.AnafCheck, j.TotalRows, j.ValidRows, j.ErrorRows, j.WarningRows,
               j.ImportedRows, j.LastError, j.CreatedAt, u.DisplayName AS CreatedByName, j.CompletedAt, j.RowVersion,
               CASE WHEN @WithData = 1 THEN j.ErrorReport END AS ErrorReport, CASE WHEN @WithData = 1 THEN j.Payload END AS Payload
        FROM core.ImportJob AS j WITH (UPDLOCK, HOLDLOCK)
        JOIN core.[User] AS u ON u.Id = j.CreatedBy
        WHERE j.Id = @Id;
    ELSE
        SELECT j.Id, j.EntityType, j.CompanyId, j.FileName, j.Status, j.AnafCheck, j.TotalRows, j.ValidRows, j.ErrorRows, j.WarningRows,
               j.ImportedRows, j.LastError, j.CreatedAt, u.DisplayName AS CreatedByName, j.CompletedAt, j.RowVersion,
               CASE WHEN @WithData = 1 THEN j.ErrorReport END AS ErrorReport, CASE WHEN @WithData = 1 THEN j.Payload END AS Payload
        FROM core.ImportJob AS j
        JOIN core.[User] AS u ON u.Id = j.CreatedBy
        WHERE j.Id = @Id;
END;
