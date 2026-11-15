-- Permisiunile Nucleului ale rolurilor de sistem (02.01, 8). Se adaugă doar legăturile lipsă: clientul poate extinde
-- rolurile, iar o rerulare nu șterge nimic (MIG-06). ADMIN are drepturi depline prin core.usp_User_GetPermissions
-- (toate permisiunile din catalog); legăturile de mai jos îl arată complet și în matricea rolurilor.
-- Completare (15.11.2026): rolul NOMENCLATOARE primește și Partner.View / Item.View, fără de care nu ar vedea listele pe care le gestionează.
SET NOCOUNT ON;
SET XACT_ABORT ON;

WITH RoleMap (RoleCode, PermissionCode) AS (
    SELECT N'ADMIN', p.Code FROM core.Permission AS p
    UNION ALL
    SELECT RoleCode, PermissionCode FROM (VALUES
        (N'NOMENCLATOARE', N'Core.Partner.View'),
        (N'NOMENCLATOARE', N'Core.Partner.Manage'),
        (N'NOMENCLATOARE', N'Core.Item.View'),
        (N'NOMENCLATOARE', N'Core.Item.Manage'),
        (N'NOMENCLATOARE', N'Core.Nomenclature.Manage'),
        (N'NOMENCLATOARE', N'Core.Dimension.Manage'),
        (N'NOMENCLATOARE', N'Core.Import.Run'),
        (N'AUDITOR',       N'Core.Company.View'),
        (N'AUDITOR',       N'Core.Partner.View'),
        (N'AUDITOR',       N'Core.Item.View'),
        (N'AUDITOR',       N'Core.Audit.View'),
        (N'STANDARD',      N'Core.Partner.View'),
        (N'STANDARD',      N'Core.Item.View'),
        (N'STANDARD',      N'Core.Company.View')
    ) AS fixed (RoleCode, PermissionCode)
)
INSERT INTO core.RolePermission (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM RoleMap AS m
JOIN core.Role AS r ON r.Code = m.RoleCode AND r.IsSystem = 1
JOIN core.Permission AS p ON p.Code = m.PermissionCode
WHERE NOT EXISTS (SELECT 1 FROM core.RolePermission AS rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
