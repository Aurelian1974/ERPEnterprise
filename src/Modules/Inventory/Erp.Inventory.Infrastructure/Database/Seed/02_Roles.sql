-- Rolurile de sistem ale Stocurilor si permisiunile lor (02.05, S1 8). Rolurile se creeaza daca lipsesc; legaturile se adauga doar daca
-- lipsesc (MIG-06). ADMIN si CONTABIL_SEF primesc toate permisiunile Inv, AUDITOR consultarea.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Role AS target
USING (VALUES
    (N'SEF_DEPOZIT',      N'Șef depozit',      N'Gestiunile, blocarea lor, gestionarii și predarea, setările de stoc ale articolelor.'),
    (N'GESTIONAR',        N'Gestionar',        N'Consultarea gestiunilor, de obicei limitată la gestiunea proprie.'),
    (N'CONTABIL_STOCURI', N'Contabil stocuri', N'Toate permisiunile Stocurilor, fără schimbarea metodei de evaluare.')
) AS source (Code, Name, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR ISNULL(target.Description, N'') <> source.Description) THEN
    UPDATE SET Name = source.Name, Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Description, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.Description, 1, 1);

WITH RoleMap (RoleCode, PermissionCode) AS (
    SELECT N'ADMIN', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Inv'
    UNION ALL
    SELECT N'CONTABIL_SEF', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Inv'
    UNION ALL
    SELECT N'CONTABIL_STOCURI', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Inv' AND p.Code <> N'Inv.ValuationMethod.Change'
    UNION ALL
    SELECT RoleCode, PermissionCode FROM (VALUES
        (N'SEF_DEPOZIT', N'Inv.Warehouse.View'),
        (N'SEF_DEPOZIT', N'Inv.Warehouse.Block'),
        (N'SEF_DEPOZIT', N'Inv.Warehouse.Keeper'),
        (N'SEF_DEPOZIT', N'Inv.ItemSettings.Manage'),
        (N'GESTIONAR',   N'Inv.Warehouse.View'),
        (N'AUDITOR',     N'Inv.Warehouse.View')
    ) AS fixed (RoleCode, PermissionCode)
)
INSERT INTO core.RolePermission (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM RoleMap AS m
JOIN core.Role AS r ON r.Code = m.RoleCode AND r.IsSystem = 1
JOIN core.Permission AS p ON p.Code = m.PermissionCode
WHERE NOT EXISTS (SELECT 1 FROM core.RolePermission AS rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
