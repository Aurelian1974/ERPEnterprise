-- Tipurile livrate de identificatori ai bucatii (02.05, S4 3.4): codul, denumirea, masca si verificarea speciala. MERGE pe CodeKey (MIG-06);
-- firma poate adauga altele.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE inv.SerialIdentifierType AS target
USING (VALUES
    (N'IMEI',     N'IMEI',                              N'^[0-9]{15}$',                                     1),
    (N'IMEI2',    N'IMEI (al doilea slot)',             N'^[0-9]{15}$',                                     1),
    (N'MAC',      N'Adresă MAC',                        N'^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$',         0),
    (N'VIN',      N'Seria de șasiu (VIN)',              N'^[A-HJ-NPR-Z0-9]{17}$',                           2),
    (N'ENGINE',   N'Seria motorului',                   NULL,                                               0),
    (N'WARRANTY', N'Codul de garanție',                 NULL,                                               0),
    (N'MPN',      N'Numărul de piesă al producătorului', NULL,                                              0)
) AS source (Code, Name, Mask, CheckKind)
ON target.CodeKey = UPPER(source.Code)
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR ISNULL(target.Mask, N'') <> ISNULL(source.Mask, N'') OR target.CheckKind <> source.CheckKind) THEN
    UPDATE SET Name = source.Name, Mask = source.Mask, CheckKind = source.CheckKind, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, Mask, CheckKind, IsSystem, CreatedBy) VALUES (source.Code, source.Name, source.Mask, source.CheckKind, 1, 1);
