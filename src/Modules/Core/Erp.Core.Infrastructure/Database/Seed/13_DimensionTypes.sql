-- Tipurile de dimensiuni livrate (02.01, 3.5; macheta 07k), de sistem; clientul isi adauga tipuri proprii (3.6 punctul 15), pe care MERGE-ul
-- nu le atinge. Valorile le adauga clientul. MERGE pe Code (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.DimensionType AS target
USING (VALUES
    ('COST_CENTER', N'Centre de cost', 10),
    ('PROJECT',     N'Proiecte', 20),
    ('WAREHOUSE',   N'Gestiuni', 30),
    ('DEPOT',       N'Depozite', 35),
    ('SITE',        N'Șantiere', 40)
) AS source (Code, Name, SortOrder)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.SortOrder <> source.SortOrder OR target.IsSystem = 0 OR target.IsActive = 0) THEN
    UPDATE SET Name = source.Name, SortOrder = source.SortOrder, IsSystem = 1, IsActive = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, SortOrder, IsSystem, IsActive) VALUES (source.Code, source.Name, source.SortOrder, 1, 1);
