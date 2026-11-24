CREATE OR ALTER PROCEDURE fin.usp_PostingModel_Get
    @Id  int
AS
BEGIN
    -- Un model de contare cu toate variantele (si cele oprite) si liniile lor, cu codul contului implicit (macheta c2-03).
    SET NOCOUNT ON;

    SELECT m.Id, m.EventType, m.Name, j.Code AS JournalCode, j.Name AS JournalName, m.IsSystem, m.IsActive
    FROM fin.PostingModel AS m JOIN fin.Journal AS j ON j.Id = m.JournalId WHERE m.Id = @Id;

    SELECT v.Id, v.Priority, v.Name, v.RequiredFlags, v.ForbiddenFlags, v.PartnerKind, v.DocumentType, v.ValidFrom, v.ValidTo, v.IsActive, v.RowVersion
    FROM fin.PostingModelVariant AS v WHERE v.ModelId = @Id ORDER BY v.Priority, v.ValidFrom;

    SELECT l.VariantId, l.LineNumber, l.Component, l.FilterVatCategory, l.FilterOperationTypeCode, l.Side, l.Sign, l.AccountKey, a.Code AS DefaultAccountCode,
        l.CopyDimensions, l.GroupBy, l.DescriptionTemplate
    FROM fin.PostingModelLine AS l
    JOIN fin.PostingModelVariant AS v ON v.Id = l.VariantId
    LEFT JOIN fin.GlAccount AS a ON a.Id = l.DefaultAccountId
    WHERE v.ModelId = @Id
    ORDER BY l.VariantId, l.LineNumber;

    SELECT Code, Name, Chain FROM fin.AccountKey ORDER BY Code;
END;
