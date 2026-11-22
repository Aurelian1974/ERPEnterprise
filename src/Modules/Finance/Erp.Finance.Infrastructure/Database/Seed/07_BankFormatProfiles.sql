-- Profilele de format bancar livrate (02.02, F2 3.1; MIG-06): formatele standard, folosite de orice banca din Romania care le respecta.
-- Clientul adauga profile CSV pentru bancile lui. StatementFormat: 1 CAMT.053, 2 MT940, 3 CSV, 4 fara import. PaymentFormat:
-- 1 pain.001.001.03, 2 pain.001.001.09, 3 CSV, 4 fara export.
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE fin.BankFormatProfile AS target
USING (VALUES
    (N'CAMT053-STD', N'CAMT.053 (ISO 20022) / pain.001.001.03', 1, 1),
    (N'MT940-STD',   N'MT940 (SWIFT) / pain.001.001.03',        2, 1),
    (N'MANUAL',      N'Fără import și fără export',             4, 4)
) AS source (Code, Name, StatementFormat, PaymentFormat)
ON target.CodeKey = UPPER(source.Code)
WHEN MATCHED AND target.IsSystem = 1 AND (target.Name <> source.Name OR target.StatementFormat <> source.StatementFormat
        OR target.PaymentFormat <> source.PaymentFormat) THEN
    UPDATE SET Name = source.Name, StatementFormat = source.StatementFormat, PaymentFormat = source.PaymentFormat,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, StatementFormat, PaymentFormat, IsSystem, CreatedBy)
    VALUES (source.Code, source.Name, source.StatementFormat, source.PaymentFormat, 1, 1);
