-- Catalogul permisiunilor Stocurilor (02.05, S1 8): coduri tehnice fixe „Inv.*”, comparate exact (SQL-21). MERGE pe Code (MIG-06);
-- seed-ul inv ruleaza dupa cel al Nucleului si al Finantelor (ordinea modulelor).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Permission AS target
USING (VALUES
    (N'Inv.Warehouse.View',         N'Lista și fișa gestiunilor (în limita gestiunilor permise)'),
    (N'Inv.Warehouse.Manage',       N'Crearea, modificarea și dezactivarea gestiunilor și a depozitelor, regulile de propunere'),
    (N'Inv.Warehouse.Block',        N'Blocarea și deblocarea manuală a gestiunilor'),
    (N'Inv.Warehouse.Keeper',       N'Gestionarii și predarea-primirea gestiunii'),
    (N'Inv.Settings.Manage',        N'Setările de stoc ale firmei'),
    (N'Inv.ItemSettings.Manage',    N'Tab-ul Stoc pe fișa articolului și pragurile pe gestiune'),
    (N'Inv.ValuationMethod.Change', N'Documentul de schimbare a metodei de evaluare')
) AS source (Code, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.Description <> source.Description THEN
    UPDATE SET Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, ModuleCode, Description, CreatedBy)
    VALUES (source.Code, N'Inv', source.Description, 1);
