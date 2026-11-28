CREATE OR ALTER PROCEDURE fin.usp_PostingModel_List
AS
BEGIN
    -- Modelele de contare (C2 3.2, macheta c2-03): evenimentul, jurnalul si numarul de variante active.
    SET NOCOUNT ON;

    SELECT m.Id, m.EventType, m.Name, j.Code AS JournalCode, m.IsSystem, m.IsActive,
        (SELECT COUNT(*) FROM fin.PostingModelVariant AS v WHERE v.ModelId = m.Id AND v.IsActive = 1) AS VariantCount
    FROM fin.PostingModel AS m JOIN fin.Journal AS j ON j.Id = m.JournalId
    ORDER BY m.Name;
END;
