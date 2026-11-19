CREATE OR ALTER PROCEDURE core.usp_InstallationLicense_GetCurrent
AS
BEGIN
    -- Licenta activa (08, 4.1): fisierul se verifica in C# la fiecare citire (Erp.Licensing); un rand fara fisier
    -- (salvat inainte de migrarea 2026.11.007) nu conteaza ca licenta.
    SET NOCOUNT ON;

    SELECT TOP (1) l.Id, l.LicenseFile
    FROM core.InstallationLicense AS l
    WHERE l.IsCurrent = 1 AND l.LicenseFile IS NOT NULL;
END;
