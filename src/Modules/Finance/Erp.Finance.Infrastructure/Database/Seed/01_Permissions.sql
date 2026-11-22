-- Catalogul permisiunilor Finantelor (02.02, F1 8): coduri tehnice fixe „Fin.*”, comparate exact (SQL-21). MERGE pe Code (MIG-06);
-- seed-ul fin ruleaza dupa cel al Nucleului (ordinea modulelor), deci tabelele core.Permission / core.Role exista deja.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Permission AS target
USING (VALUES
    (N'Fin.TreasuryAccount.View',   N'Vizualizare conturi de trezorerie'),
    (N'Fin.TreasuryAccount.Manage', N'Configurare conturi de trezorerie și atribuire casieri'),
    (N'Fin.Cash.View',              N'Documentele și registrul caselor atribuite'),
    (N'Fin.Cash.ViewAll',           N'Toate casele firmei, fără atribuire'),
    (N'Fin.Cash.Create',            N'Creare și modificare ciorne de casă'),
    (N'Fin.Cash.Validate',          N'Validare documente de casă (doar pe casele cu drept de validare)'),
    (N'Fin.Cash.Reverse',           N'Stornare documente de casă'),
    (N'Fin.Cash.CloseDay',          N'Închiderea zilei de casă'),
    (N'Fin.Cash.ReopenDay',         N'Redeschiderea unei zile de casă închise (auditată)'),
    (N'Fin.CashLimit.Manage',       N'Plafoane de numerar proprii ale firmei'),
    (N'Fin.Advance.Manage',         N'Avansuri spre decontare'),
    (N'Fin.ExpenseReport.Create',   N'Deconturi de cheltuieli pentru orice angajat'),
    (N'Fin.ExpenseReport.Approve',  N'Aprobare deconturi de cheltuieli'),
    (N'Fin.ExpenseReport.Validate', N'Validare deconturi de cheltuieli'),
    (N'Fin.Treasury.Configure',     N'Tipuri de operațiuni de trezorerie, tipuri de cheltuieli și diurnă')
) AS source (Code, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.Description <> source.Description THEN
    UPDATE SET Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, ModuleCode, Description, CreatedBy)
    VALUES (source.Code, N'Fin', source.Description, 1);
