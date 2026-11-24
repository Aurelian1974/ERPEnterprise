CREATE OR ALTER PROCEDURE fin.usp_PostingModel_Load
    @EventType  varchar(80),
    @OnDate     date
AS
BEGIN
    -- Modelul activ al unui tip de eveniment (C2 4.1 pasul 2): antetul cu jurnalul, variantele active in vigoare la data contabila si liniile
    -- lor, cu codul contului implicit. Fara rand daca tipul nu are model activ.
    SET NOCOUNT ON;

    SELECT m.Id, m.EventType, m.JournalId
    FROM fin.PostingModel AS m
    WHERE m.EventType = @EventType AND m.IsActive = 1;

    SELECT v.Id, v.Priority, v.Name, v.RequiredFlags, v.ForbiddenFlags, v.PartnerKind, v.DocumentType, v.ValidFrom, v.ValidTo
    FROM fin.PostingModelVariant AS v
    JOIN fin.PostingModel AS m ON m.Id = v.ModelId
    WHERE m.EventType = @EventType AND m.IsActive = 1 AND v.IsActive = 1 AND v.ValidFrom <= @OnDate AND (v.ValidTo IS NULL OR v.ValidTo >= @OnDate);

    SELECT l.VariantId, l.LineNumber, l.Component, l.FilterVatCategory, l.FilterOperationTypeCode, l.Side, l.Sign, l.AccountKey, a.Code AS DefaultAccountCode,
           l.CopyDimensions, l.GroupBy, l.DescriptionTemplate
    FROM fin.PostingModelLine AS l
    JOIN fin.PostingModelVariant AS v ON v.Id = l.VariantId
    JOIN fin.PostingModel AS m ON m.Id = v.ModelId
    LEFT JOIN fin.GlAccount AS a ON a.Id = l.DefaultAccountId
    WHERE m.EventType = @EventType AND m.IsActive = 1 AND v.IsActive = 1;
END;
