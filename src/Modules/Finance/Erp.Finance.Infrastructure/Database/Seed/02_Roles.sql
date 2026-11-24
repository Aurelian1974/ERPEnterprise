-- Rolurile de sistem ale Finantelor si permisiunile lor (02.02, F1 8, F2 8 si F3 8; Fin.PaymentOrder.FreeIban nu e in niciun rol livrat). Rolurile se creeaza daca lipsesc; legaturile se adauga doar daca
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
    (N'DIRECTOR_FINANCIAR',  N'Director financiar',  N'Consultarea băncilor și aprobarea ordinelor de plată (patru ochi).'),
    (N'CONTABIL_DECONTARI',  N'Contabil clienți / furnizori', N'Soldurile partenerilor, alocări, litigii, compensări, efecte, ajustări sub prag, confirmări de sold și calculul penalităților.'),
    (N'DIRECTOR_GENERAL',    N'Director general',    N'Aprobarea propunerilor de plată de la cel mai înalt nivel și prognoza de cash-flow.'),
    (N'CONTABIL',            N'Contabil',            N'Notele contabile și consultarea planului de conturi.'),
    (N'CONTROLLER_GRUP',     N'Controller de grup',  N'Planurile de conturi alternative, regulile de mapare și raportarea de grup.'),
    (N'DEBLOCARE_AN',        N'Deblocare an',        N'Doar deblocarea unui an financiar blocat; se atribuie unui utilizator dedicat, fără alt rol.')
) AS source (Code, Name, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR ISNULL(target.Description, N'') <> source.Description) THEN
    UPDATE SET Name = source.Name, Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Description, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.Description, 1, 1);

WITH RoleMap (RoleCode, PermissionCode) AS (
    SELECT N'ADMIN', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Fin' AND p.Code NOT IN (N'Fin.PaymentOrder.FreeIban', N'Fin.FiscalYear.Unlock')
    UNION ALL
    SELECT N'CONTABIL_SEF', p.Code FROM core.Permission AS p WHERE p.ModuleCode = N'Fin' AND p.Code NOT IN (N'Fin.PaymentOrder.FreeIban', N'Fin.FiscalYear.Unlock')
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
        (N'AUDITOR',             N'Fin.Bank.View'),
        (N'CONTABIL_DECONTARI',  N'Fin.PartnerBalance.View'),
        (N'CONTABIL_DECONTARI',  N'Fin.Allocation.Manage'),
        (N'CONTABIL_DECONTARI',  N'Fin.OpenItem.SetDispute'),
        (N'CONTABIL_DECONTARI',  N'Fin.Compensation.Create'),
        (N'CONTABIL_DECONTARI',  N'Fin.CommercialPaper.Manage'),
        (N'CONTABIL_DECONTARI',  N'Fin.WriteOff.Create'),
        (N'CONTABIL_DECONTARI',  N'Fin.BalanceConfirmation.Manage'),
        (N'CONTABIL_DECONTARI',  N'Fin.Penalty.Calculate'),
        (N'CONTABIL_DECONTARI',  N'Core.Partner.View'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.PartnerBalance.View'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.OpenItem.BlockPayment'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Compensation.Validate'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Penalty.Approve'),
        (N'AUDITOR',             N'Fin.PartnerBalance.View'),
        (N'CONTABIL_DECONTARI',  N'Fin.Fx.View'),
        (N'CONTABIL_DECONTARI',  N'Fin.Fx.ManualCrossRate'),
        (N'CONTABIL_TREZORERIE', N'Fin.Fx.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.Fx.ManualCrossRate'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Fx.View'),
        (N'AUDITOR',             N'Fin.Fx.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.PaymentProposal.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.PaymentProposal.Create'),
        (N'CONTABIL_TREZORERIE', N'Fin.PaymentProposal.GenerateOrders'),
        (N'CONTABIL_TREZORERIE', N'Fin.CashFlow.View'),
        (N'CONTABIL_TREZORERIE', N'Fin.CashFlow.Plan'),
        (N'CONTABIL_TREZORERIE', N'Fin.CashFlow.Snapshot'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.PaymentProposal.View'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.PaymentProposal.Approve'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.CashFlow.View'),
        (N'DIRECTOR_GENERAL',    N'Fin.PaymentProposal.View'),
        (N'DIRECTOR_GENERAL',    N'Fin.PaymentProposal.Approve'),
        (N'DIRECTOR_GENERAL',    N'Fin.CashFlow.View'),
        (N'AUDITOR',             N'Fin.PaymentProposal.View'),
        (N'AUDITOR',             N'Fin.CashFlow.View'),
        (N'CONTABIL',            N'Fin.Account.View'),
        (N'CONTROLLER_GRUP',     N'Fin.Account.View'),
        (N'CONTROLLER_GRUP',     N'Fin.Chart.Manage'),
        (N'CONTROLLER_GRUP',     N'Fin.Mapping.Manage'),
        (N'CONTROLLER_GRUP',     N'Fin.Mapping.Freeze'),
        (N'CONTROLLER_GRUP',     N'Fin.Mapping.Unfreeze'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Account.View'),
        (N'AUDITOR',             N'Fin.Account.View'),
        (N'CONTABIL',            N'Fin.Period.View'),
        (N'CONTABIL',            N'Fin.Period.SoftClose'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Period.View'),
        (N'AUDITOR',             N'Fin.Period.View'),
        (N'DEBLOCARE_AN',        N'Fin.Period.View'),
        (N'DEBLOCARE_AN',        N'Fin.FiscalYear.Unlock'),
        (N'CONTABIL',            N'Fin.JournalEntry.View'),
        (N'CONTABIL',            N'Fin.ManualEntry.Create'),
        (N'CONTABIL',            N'Fin.ManualEntry.Validate'),
        (N'CONTABIL',            N'Fin.Posting.Retry'),
        (N'AUDITOR',             N'Fin.JournalEntry.View'),
        (N'AUDITOR',             N'Fin.PostingModel.View'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.JournalEntry.View'),
        (N'CONTABIL',            N'Fin.Report.TrialBalance'),
        (N'AUDITOR',             N'Fin.Report.TrialBalance'),
        (N'CONTROLLER_GRUP',     N'Fin.Report.TrialBalance'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Report.TrialBalance'),
        (N'CONTABIL',            N'Fin.Tax.View'),
        (N'AUDITOR',             N'Fin.Tax.View'),
        (N'DIRECTOR_FINANCIAR',  N'Fin.Tax.View')
    ) AS fixed (RoleCode, PermissionCode)
)
INSERT INTO core.RolePermission (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM RoleMap AS m
JOIN core.Role AS r ON r.Code = m.RoleCode AND r.IsSystem = 1
JOIN core.Permission AS p ON p.Code = m.PermissionCode
WHERE NOT EXISTS (SELECT 1 FROM core.RolePermission AS rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
