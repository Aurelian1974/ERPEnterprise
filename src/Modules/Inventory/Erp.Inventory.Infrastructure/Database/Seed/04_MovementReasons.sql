-- Motivele livrate de incarcare si descarcare (02.05, S2 3.5 si 3.6), comune tuturor firmelor (CompanyId NULL), nesterse (IsSystem). Clientul isi
-- adauga motive proprii; o rerulare actualizeaza doar campurile motivelor de sistem (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

WITH target AS (SELECT * FROM inv.MovementReason WHERE CompanyId IS NULL)
MERGE target
USING (VALUES
    ('OPENING',          N'Sold inițial la punerea în funcțiune',                 1, 0, 1, 0, NULL,    NULL,                1, 1),
    ('SURPLUS',          N'Plus constatat în afara inventarului',                 1, 0, 1, 1, NULL,    NULL,                0, 2),
    ('FREE_RECEIVED',    N'Bunuri primite cu titlu gratuit',                      1, 0, 1, 1, NULL,    NULL,                0, 1),
    ('EMPLOYEE_RETURN',  N'Restituire de la angajat',                             1, 0, 1, 0, NULL,    NULL,                0, 2),
    ('TRANSFER_SURPLUS', N'Plus la primirea unui transfer',                       1, 0, 1, 0, NULL,    NULL,                0, 2),
    ('SCRAP',            N'Casare sau degradare, cu proces-verbal',              -1, 2, 1, 1, 5000.00, N'CONTABIL_SEF',     0, 1),
    ('EXPIRED',          N'Produse expirate distruse',                           -1, 2, 1, 1, 5000.00, N'CONTABIL_SEF',     0, 1),
    ('DONATION',         N'Donație, sponsorizare în natură',                     -1, 1, 1, 1, 0.00,    N'DIRECTOR_GENERAL', 0, 1),
    ('PROTOCOL',         N'Protocol',                                            -1, 1, 1, 0, NULL,    NULL,                0, 1),
    ('SAMPLES',          N'Mostre, bunuri promoționale',                         -1, 1, 1, 0, NULL,    NULL,                0, 1),
    ('SHORTAGE_CHARGED', N'Lipsă imputată (angajat, transportator)',             -1, 1, 1, 1, NULL,    NULL,                0, 1),
    ('SHORTAGE_TRANSIT', N'Lipsă sau degradare la transport, neimputabilă',      -1, 2, 0, 1, 5000.00, N'CONTABIL_SEF',     0, 1),
    ('EMPLOYEE_ISSUE',   N'Ieșire către angajați (echipament, beneficii în natură)', -1, 1, 1, 0, NULL, NULL,               0, 1)
) AS source (Code, Name, Direction, VatTreatment, IsDeductible, RequiresAttachment, ApprovalThreshold, ApproverRoleCode, NoPosting, CostSource)
ON target.Code = source.Code
WHEN MATCHED AND target.IsSystem = 1 THEN
    UPDATE SET Name = source.Name, Direction = source.Direction, VatTreatment = source.VatTreatment, IsDeductible = source.IsDeductible,
        RequiresAttachment = source.RequiresAttachment, ApprovalThreshold = source.ApprovalThreshold, ApproverRoleCode = source.ApproverRoleCode,
        NoPosting = source.NoPosting, CostSource = source.CostSource
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CompanyId, Code, Name, Direction, VatTreatment, IsDeductible, RequiresAttachment, ApprovalThreshold, ApproverRoleCode, NoPosting, CostSource,
        IsSystem, CreatedBy)
    VALUES (NULL, source.Code, source.Name, source.Direction, source.VatTreatment, source.IsDeductible, source.RequiresAttachment,
        source.ApprovalThreshold, source.ApproverRoleCode, source.NoPosting, source.CostSource, 1, 1);
