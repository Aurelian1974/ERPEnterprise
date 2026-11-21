CREATE OR ALTER PROCEDURE core.usp_NumberSeriesRule_List
    @CompanyId int
AS
BEGIN
    -- Regulile de alegere a seriei ale firmei (Configurare › Serii; 02.01, 3.6 punctul 16), in ordinea aplicarii, si utilizatorii firmei.
    SET NOCOUNT ON;

    SELECT r.Id, r.CompanyId, d.Code AS DocumentTypeCode, d.Name AS DocumentTypeName, r.Priority, r.UserId, u.DisplayName AS UserName,
           r.BranchId, b.Name AS BranchName, r.LocationId, l.Name AS LocationName, r.PartnerScope, r.Prefix, r.IsActive, r.RowVersion
    FROM core.NumberSeriesRule AS r
    JOIN core.DocumentType AS d ON d.Id = r.DocumentTypeId
    LEFT JOIN core.[User] AS u ON u.Id = r.UserId
    LEFT JOIN core.Branch AS b ON b.Id = r.BranchId
    LEFT JOIN core.Location AS l ON l.Id = r.LocationId
    WHERE r.CompanyId = @CompanyId
    ORDER BY d.Name, r.Priority, r.Id;

    -- Utilizatorii activi cu un rol care acopera firma (conditia „utilizator” a regulii).
    SELECT u.Id, u.DisplayName
    FROM core.[User] AS u
    WHERE u.IsActive = 1 AND u.Id <> 1 AND u.Id IN (SELECT s.UserId FROM core.fn_AssignmentScope(NULL) AS s WHERE s.CompanyId = @CompanyId)
    ORDER BY u.NameSearch, u.Id;
END;
