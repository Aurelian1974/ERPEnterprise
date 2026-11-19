CREATE OR ALTER PROCEDURE core.usp_DataProtectionKey_List
AS
BEGIN
    -- Inelul de chei Data Protection (08, 5.12), citit de Erp.Host si Erp.Worker la pornire si la rotirea cheilor.
    SET NOCOUNT ON;

    SELECT k.FriendlyName, k.Xml
    FROM core.DataProtectionKey AS k
    ORDER BY k.Id;
END;
