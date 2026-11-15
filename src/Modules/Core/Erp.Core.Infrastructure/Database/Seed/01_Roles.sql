-- Rolurile de sistem (02.01, 3.4 si 8; 08, 8.6): coduri fixe, comparate exact. MERGE pe Code (MIG-06); nu atinge rolurile clientului.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Role AS target
USING (VALUES
    (N'ADMIN',         N'Administrator',             N'Toate permisiunile; atribuit și la nivel de instalare.'),
    (N'NOMENCLATOARE', N'Responsabil nomenclatoare', N'Parteneri, articole, nomenclatoare, dimensiuni și importuri.'),
    (N'AUDITOR',       N'Auditor',                   N'Consultare și jurnal de audit, fără modificări.'),
    (N'STANDARD',      N'Utilizator standard',       N'Consultare parteneri, articole și firme.')
) AS source (Code, Name, Description)
ON target.Code = source.Code
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR ISNULL(target.Description, N'') <> source.Description) THEN
    UPDATE SET Name = source.Name, Description = source.Description, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Description, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.Description, 1, 1);
