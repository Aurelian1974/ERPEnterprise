CREATE OR ALTER PROCEDURE core.usp_InstallationLicense_List
AS
BEGIN
    -- Istoricul licentelor (08, 4.1): licentele inlocuite raman, cu IsCurrent = 0. Fara fisiere.
    SET NOCOUNT ON;

    SELECT l.Id, l.LicenseId, l.KeyId, l.CustomerName, l.LimitCompanies, l.IssuedDate, l.ValidUntil, l.UpdatesUntil,
           l.IsCurrent, l.CreatedAt
    FROM core.InstallationLicense AS l
    ORDER BY l.Id DESC;
END;
