CREATE OR ALTER PROCEDURE fin.usp_ReportDefinition_Get
    @CompanyId  int,
    @Id         int = NULL,
    @Code       varchar(30) = NULL,
    @OnDate     date = NULL
AS
BEGIN
    -- Un raport (dupa Id sau, pentru cele livrate, dupa cod si versiunea in vigoare la data), cu randurile lui; apoi codurile conturilor planului
    -- (pentru validarea formulelor) si dimensiunile firmei (filtrele CC=, PR=).
    SET NOCOUNT ON;
    IF @Id IS NULL
        SELECT TOP (1) @Id = Id FROM fin.ReportDefinition
        WHERE Code = @Code AND (CompanyId = @CompanyId OR CompanyId IS NULL) AND IsActive = 1
          AND (@OnDate IS NULL OR ((ValidFrom IS NULL OR ValidFrom <= @OnDate) AND (ValidTo IS NULL OR ValidTo >= @OnDate)))
        ORDER BY CASE WHEN CompanyId IS NULL THEN 1 ELSE 0 END, Version DESC;
    DECLARE @Chart int = (SELECT ISNULL(ChartId, (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1)) FROM fin.ReportDefinition WHERE Id = @Id);
    SELECT Id, Code, Name, Kind, ChartId, Version, ValidFrom, ValidTo, RowVersion FROM fin.ReportDefinition
    WHERE Id = @Id AND (CompanyId = @CompanyId OR CompanyId IS NULL);
    SELECT RowCode, Label, Formula, IsTotal, Indent FROM fin.ReportRow WHERE DefinitionId = @Id ORDER BY SortOrder;
    SELECT Code FROM fin.GlAccount WHERE ChartId = ISNULL(@Chart, (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1)) AND (CompanyId IS NULL OR CompanyId = @CompanyId);
    SELECT Id, DimensionType, Code FROM core.DimensionValue WHERE CompanyId = @CompanyId AND DimensionType IN ('COST_CENTER', 'PROJECT');
END;
