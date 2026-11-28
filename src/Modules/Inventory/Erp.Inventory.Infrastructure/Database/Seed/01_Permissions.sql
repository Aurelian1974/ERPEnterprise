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
    (N'Inv.ValuationMethod.Change', N'Documentul de schimbare a metodei de evaluare'),
    (N'Inv.Document.View',          N'Documentele de stoc și registrul de mișcări (în limita gestiunilor permise)'),
    (N'Inv.Transfer.Edit',          N'Note de predare și de primire în ciornă'),
    (N'Inv.Transfer.Validate',      N'Validarea notelor de predare și de primire'),
    (N'Inv.LoadUnload.Edit',        N'Încărcări și descărcări în ciornă'),
    (N'Inv.LoadUnload.Validate',    N'Validarea încărcărilor și descărcărilor'),
    (N'Inv.LoadUnload.Approve',     N'Aprobarea descărcărilor peste prag'),
    (N'Inv.Consumption.Edit',       N'Bonuri de consum în ciornă'),
    (N'Inv.Consumption.Validate',   N'Validarea bonurilor de consum'),
    (N'Inv.ProductionNote.Edit',    N'Note de predare produse în ciornă (fără Producție)'),
    (N'Inv.ProductionNote.Validate', N'Validarea notelor de predare produse'),
    (N'Inv.Custody.Edit',           N'Intrări și ieșiri de custodie în ciornă'),
    (N'Inv.Custody.Validate',       N'Validarea documentelor de custodie'),
    (N'Inv.Document.Reverse',       N'Stornarea documentelor de stoc'),
    (N'Inv.Movement.Backdate',      N'Mișcări cu dată mai veche decât limita firmei'),
    (N'Inv.Reason.Manage',          N'Motivele de încărcare și descărcare'),
    (N'Inv.Cost.View',              N'Costurile, straturile, ajustările și coada de recalculare'),
    (N'Inv.Cost.Recalculate',       N'Recalcularea la cerere a costului'),
    (N'Inv.LandedCost.Edit',        N'Costuri adăugate în ciornă'),
    (N'Inv.LandedCost.Validate',    N'Validarea costurilor adăugate'),
    (N'Inv.RetailPrice.Edit',       N'Modificări de preț cu amănuntul în ciornă'),
    (N'Inv.RetailPrice.Validate',   N'Validarea modificărilor de preț cu amănuntul'),
    (N'Inv.RetailDischarge.Validate', N'Descărcarea lunară la preț cu amănuntul'),
    (N'Inv.StandardCost.Update',    N'Actualizarea costului standard'),
    (N'Inv.Lot.View',               N'Loturile, seriile, trasabilitatea'),
    (N'Inv.Lot.Edit',               N'Atributele lotului (lot furnizor, țară, termen, cu motiv)'),
    (N'Inv.Lot.Release',            N'Eliberarea din carantină și trecerea în carantină'),
    (N'Inv.Lot.Block',              N'Blocarea și deblocarea loturilor'),
    (N'Inv.Lot.OverrideShelfLife',  N'Livrarea unui lot sub durata minimă a clientului, cu motiv'),
    (N'Inv.Lot.SellAfterBestBefore', N'Vânzarea sau donarea după data durabilității minimale'),
    (N'Inv.Lot.ReceiveExpired',     N'Recepția unui lot deja expirat (pentru retur sau casare)'),
    (N'Inv.Serial.EditIdentifiers', N'Corectarea identificatorilor unei bucăți (IMEI, MAC, VIN), cu motiv'),
    (N'Inv.Settings.SerialIdentifiers', N'Tipurile de identificatori și regulile pe grupă sau model'),
    (N'Inv.LotChange.Edit',         N'Documentul de schimbare a lotului (SL) în ciornă'),
    (N'Inv.LotChange.Validate',     N'Validarea schimbării lotului'),
    (N'Inv.Recall.Edit',            N'Retragerile de lot în ciornă'),
    (N'Inv.Recall.Validate',        N'Validarea retragerii (blocarea loturilor)'),
    (N'Inv.Recall.Close',           N'Închiderea retragerii')
) AS source (Code, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.Description <> source.Description THEN
    UPDATE SET Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, ModuleCode, Description, CreatedBy)
    VALUES (source.Code, N'Inv', source.Description, 1);
