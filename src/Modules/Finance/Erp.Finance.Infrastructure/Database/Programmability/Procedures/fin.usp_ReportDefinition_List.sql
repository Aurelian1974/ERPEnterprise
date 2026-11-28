CREATE OR ALTER PROCEDURE fin.usp_ReportDefinition_List
    @CompanyId  int
AS
BEGIN
    -- Rapoartele disponibile firmei (macheta c5-03): cele livrate (versiunea cea mai noua pe cod) si ale clientului.
    SET NOCOUNT ON;
    SELECT d.Id, d.Code, d.Name, d.Kind, d.Version, d.ValidFrom, d.ValidTo, (SELECT COUNT(*) FROM fin.ReportRow AS r WHERE r.DefinitionId = d.Id) AS Rows
    FROM fin.ReportDefinition AS d
    WHERE d.IsActive = 1 AND (d.CompanyId = @CompanyId OR d.CompanyId IS NULL)
    ORDER BY d.Kind, d.Code, d.Version DESC;
END;
