CREATE OR ALTER PROCEDURE core.usp_ImportJob_List
    @EntityType varchar(20) = NULL,
    @Top        int = 20
AS
BEGIN
    -- Istoricul importurilor (02.01, 10 „Importuri”), cele mai noi intai, fara datele randurilor.
    SET NOCOUNT ON;

    SELECT TOP (@Top) j.Id, j.EntityType, j.CompanyId, j.FileName, j.Status, j.AnafCheck, j.TotalRows, j.ValidRows, j.ErrorRows, j.WarningRows,
           j.ImportedRows, j.LastError, j.CreatedAt, u.DisplayName AS CreatedByName, j.CompletedAt, j.RowVersion,
           CAST(NULL AS nvarchar(max)) AS ErrorReport, CAST(NULL AS nvarchar(max)) AS Payload
    FROM core.ImportJob AS j
    JOIN core.[User] AS u ON u.Id = j.CreatedBy
    WHERE @EntityType IS NULL OR j.EntityType = @EntityType
    ORDER BY j.Id DESC;
END;
