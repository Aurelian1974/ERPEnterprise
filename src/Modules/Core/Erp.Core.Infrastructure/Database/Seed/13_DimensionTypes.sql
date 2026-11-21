-- Tipurile de dimensiuni (02.01, 3.5; macheta 07k): fixe, livrate cu kitul; valorile le adauga clientul. MERGE pe Code (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.DimensionType AS target
USING (VALUES
    ('COST_CENTER', N'Centre de cost', 10),
    ('PROJECT',     N'Proiecte', 20),
    ('WAREHOUSE',   N'Gestiuni', 30),
    ('SITE',        N'Șantiere', 40)
) AS source (Code, Name, SortOrder)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.SortOrder <> source.SortOrder) THEN
    UPDATE SET Name = source.Name, SortOrder = source.SortOrder
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, SortOrder) VALUES (source.Code, source.Name, source.SortOrder);
