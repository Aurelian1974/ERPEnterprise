CREATE OR ALTER PROCEDURE fin.usp_ReportDefinition_AccountCodes
    @CompanyId  int,
    @ChartId    int = NULL
AS
BEGIN
    -- Codurile conturilor planului unui raport (planul romanesc implicit), pentru validarea formulelor la salvare (51974).
    SET NOCOUNT ON;
    SELECT Code FROM fin.GlAccount
    WHERE ChartId = ISNULL(@ChartId, (SELECT Id FROM fin.ChartOfAccounts WHERE Kind = 1)) AND (CompanyId IS NULL OR CompanyId = @CompanyId);
END;
