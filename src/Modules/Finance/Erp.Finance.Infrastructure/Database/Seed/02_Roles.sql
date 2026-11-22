-- Rolurile de sistem ale Finantelor si permisiunile lor (02.02, F1 8). Rolurile se creeaza daca lipsesc; legaturile se adauga doar daca
-- lipsesc: clientul poate extinde rolurile, iar o rerulare nu sterge nimic (MIG-06). ADMIN primeste si permisiunile Fin (matricea
-- rolurilor il arata complet), iar AUDITOR pe cele de consultare.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Role AS target
USING (VALUES
    (N'CASIER',              N'Casier',              N'Documentele de casă și închiderea zilei, doar pe casele atribuite.'),
    (N'CONTABIL_TREZORERIE', N'Contabil trezorerie', N'Casa, avansurile spre decontare și deconturile de cheltuieli.'),
    (N'CONTABIL_SEF',        N'Contabil șef',        N'Toate permisiunile Finanțelor.'),
    (N'APROBATOR_DECONTURI', N'Aprobator deconturi', N'Aprobarea deconturilor de cheltuieli (șeful direct).')
) AS source (Code, Name, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR ISNULL(target.Description, N'') <> source.Description) THEN
    UPDATE SET Name = source.Name, Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Description, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.Description, 1, 1);

WITH RoleMap (RoleCode, PermissionCode) AS (
    SELECT N'ADMIN', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Fin'
    UNION ALL
    SELECT N'CONTABIL_SEF', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Fin'
    UNION ALL
    SELECT RoleCode, PermissionCode FROM (VALUES
        (N'CASIER',              N'Fin.Cash.View'),
        (N'CASIER',              N'Fin.Cash.Create'),
        (N'CASIER',              N'Fin.Cash.Validate'),
        (N'CASIER',              N'Fin.Cash.CloseDay'),
        -- Casierul alege partenerul documentului din lista Nucleului (doar consultare).
        (N'CASIER',              N'Core.Partner.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.TreasuryAccount.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.Cash.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.Cash.ViewAll'),
        (N'CONTABIL_TREZORERIE', N'Fin.Cash.Create'),
        (N'CONTABIL_TREZORERIE', N'Fin.Cash.Validate'),
        (N'CONTABIL_TREZORERIE', N'Fin.Cash.Reverse'),
        (N'CONTABIL_TREZORERIE', N'Fin.Cash.CloseDay'),
        (N'CONTABIL_TREZORERIE', N'Fin.Advance.Manage'),
        (N'CONTABIL_TREZORERIE', N'Fin.ExpenseReport.Create'),
        (N'CONTABIL_TREZORERIE', N'Fin.ExpenseReport.Validate'),
        (N'APROBATOR_DECONTURI', N'Fin.ExpenseReport.Approve'),
        (N'AUDITOR',             N'Fin.TreasuryAccount.View'),
        (N'AUDITOR',             N'Fin.Cash.ViewAll')
    ) AS fixed (RoleCode, PermissionCode)
)
INSERT INTO core.RolePermission (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM RoleMap AS m
JOIN core.Role AS r ON r.Code = m.RoleCode AND r.IsSystem = 1
JOIN core.Permission AS p ON p.Code = m.PermissionCode
WHERE NOT EXISTS (SELECT 1 FROM core.RolePermission AS rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
