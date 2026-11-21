-- Catalogul permisiunilor Nucleului (02.01, 8): coduri tehnice fixe, comparate exact (SQL-21). MERGE pe Code (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Permission AS target
USING (VALUES
    (N'Core.Company.View',                 N'Vizualizare firme, puncte de lucru și conturi bancare'),
    (N'Core.Company.Manage',               N'Creare și modificare firme, puncte de lucru și conturi bancare'),
    (N'Core.Partner.View',                 N'Vizualizare parteneri'),
    (N'Core.Partner.Manage',               N'Creare și modificare parteneri'),
    (N'Core.Partner.ViewPersonalData',     N'Vizualizare CNP și date personale ale persoanelor fizice (jurnalizată)'),
    (N'Core.Partner.Anonymize',            N'Export și anonimizare GDPR a datelor unei persoane fizice'),
    (N'Core.Item.View',                    N'Vizualizare articole'),
    (N'Core.Item.Manage',                  N'Creare și modificare articole'),
    (N'Core.Nomenclature.Manage',          N'Valute, cote TVA, UM, țări, județe'),
    (N'Core.Dimension.Manage',             N'Centre de cost, proiecte, gestiuni, șantiere'),
    (N'Core.ExchangeRate.ManageManual',    N'Cursuri manuale (nu BNR)'),
    (N'Core.NumberSeries.Manage',          N'Creare și modificare serii de numerotare'),
    (N'Core.Precision.Manage',             N'Politici de zecimale'),
    (N'Core.Import.Run',                   N'Importuri din Excel'),
    (N'Core.Employee.View',                N'Vizualizare fișe de angajat'),
    (N'Core.Employee.Manage',              N'Creare și modificare fișe de angajat'),
    (N'Core.User.Manage',                  N'Utilizatori, atribuiri de roluri, deblocare cont și resetare 2FA'),
    (N'Core.Role.Manage',                  N'Roluri și permisiunile lor'),
    (N'Core.Audit.View',                   N'Consultare jurnal de audit și jurnal de autentificări'),
    (N'Core.License.Manage',               N'Încărcare licență'),
    (N'Core.Settings.Manage',              N'Setări de instalare și de firmă (inclusiv SMTP)'),
    (N'Core.Property.Manage',              N'Definițiile de proprietăți pentru parteneri și articole'),
    (N'Core.Integration.Monitor',          N'Ecranul de outbox, reîncercare mesaje eșuate')
) AS source (Code, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.Description <> source.Description THEN
    UPDATE SET Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, ModuleCode, Description, CreatedBy)
    VALUES (source.Code, N'Core', source.Description, 1);
