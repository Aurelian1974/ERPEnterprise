-- Jurnalele livrate (02.02, C2 3.6; MIG-06), comune instalarii (CompanyId NULL). MERGE pe cod: cele livrate se actualizeaza.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.Journal AS target
USING (VALUES
    (N'VZ', N'Vânzări'), (N'AC', N'Achiziții'), (N'TR', N'Trezorerie (casă și bancă)'), (N'ST', N'Stocuri'), (N'SA', N'Salarii'),
    (N'MF', N'Mijloace fixe'), (N'VL', N'Valută (diferențe de curs și reevaluări)'), (N'DV', N'Diverse (note manuale)'),
    (N'IN', N'Închideri și deschideri'), (N'DC', N'Decontări (compensări, ajustări, efecte)')
) AS source (Code, Name)
ON target.CompanyId IS NULL AND target.CodeKey = UPPER(source.Code)
WHEN MATCHED AND target.IsSystem = 1 AND target.Name <> source.Name THEN
    UPDATE SET Name = source.Name, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CompanyId, Code, Name, IsSystem, CreatedBy) VALUES (NULL, source.Code, source.Name, 1, 1);
