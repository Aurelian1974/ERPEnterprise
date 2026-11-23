-- Rolurile de sistem ale Finantelor si permisiunile lor (02.02, F1 8 si F2 8; Fin.PaymentOrder.FreeIban nu e in niciun rol livrat). Rolurile se creeaza daca lipsesc; legaturile se adauga doar daca
-- lipsesc: clientul poate extinde rolurile, iar o rerulare nu sterge nimic (MIG-06). ADMIN primeste si permisiunile Fin (matricea
-- rolurilor il arata complet), iar AUDITOR pe cele de consultare.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Role AS target
USING (VALUES
    (N'CASIER',              N'Casier',              N'Documentele de casă și închiderea zilei, doar pe casele atribuite.'),
    (N'CONTABIL_TREZORERIE', N'Contabil trezorerie', N'Casa, avansurile spre decontare și deconturile de cheltuieli.'),
    (N'CONTABIL_SEF',        N'Contabil șef',        N'Toate permisiunile Finanțelor.'),
    (N'APROBATOR_DECONTURI', N'Aprobator deconturi', N'Aprobarea deconturilor de cheltuieli (șeful direct).'),
    (N'DIRECTOR_FINANCIAR',  N'Director financiar',  N'Consultarea băncilor și aprobarea ordinelor de plată (patru ochi).')
) AS source (Code, Name, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR ISNULL(target.Description, N'') <> source.Description) THEN
    UPDATE SET Name = source.Name, Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Description, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.Description, 1, 1);

WITH RoleMap (RoleCode, PermissionCode) AS (
    SELECT N'ADMIN', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Fin' AND p.Code <> N'Fin.PaymentOrder.FreeIban'
    UNION ALL
    SELECT N'CONTABIL_SEF', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Fin' AND p.Code <> N'Fin.PaymentOrder.FreeIban'
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
        (N'AUDITOR',             N'Fin.Cash.ViewAll'),
        (N'CONTABIL_TREZORERIE', N'Fin.Bank.View'),
        -- Beneficiarul OP-ului si contul lui bancar se aleg din fisa partenerului (doar consultare, F2 3.6 regula 1).
        (N'CONTABIL_TREZORERIE', N'Core.Partner.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.BankStatement.Import'),
        (N'CONTABIL_TREZORERIE', N'Fin.BankStatement.Reconcile'),
        (N'CONTABIL_TREZORERIE', N'Fin.BankStatement.Validate'),
        (N'CONTABIL_TREZORERIE', N'Fin.PaymentOrder.Create'),
        (N'CONTABIL_TREZORERIE', N'Fin.PaymentOrder.Validate'),
        (N'CONTABIL_TREZORERIE', N'Fin.PaymentOrder.Export'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Bank.View'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.PaymentOrder.Approve'),
        (N'AUDITOR',             N'Fin.Bank.View')
    ) AS fixed (RoleCode, PermissionCode)
)
INSERT INTO core.RolePermission (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM RoleMap AS m
JOIN core.Role AS r ON r.Code = m.RoleCode AND r.IsSystem = 1
JOIN core.Permission AS p ON p.Code = m.PermissionCode
WHERE NOT EXISTS (SELECT 1 FROM core.RolePermission AS rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
