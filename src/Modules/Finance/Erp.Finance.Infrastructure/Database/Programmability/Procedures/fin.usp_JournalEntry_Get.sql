CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_Get
    @CompanyId  int,
    @Id         bigint
AS
BEGIN
    -- O nota cu liniile ei (C2 11, macheta c2-02): contul, partenerul, angajatul si dimensiunile, cu denumirile.
    SET NOCOUNT ON;

    SELECT e.Id, e.JournalId, j.Code AS JournalCode, e.Number, e.EntryDate, e.AccountingDate, e.SourceKind, e.SourceModule, e.SourceDocumentType,
           e.SourceDocumentId, e.SourceDocumentNumber, e.Status, e.ReversalOfId, r.Id AS ReversedById, e.Description, e.TotalDebit, e.TotalCredit,
           e.ValidatedAt, v.DisplayName AS ValidatedByName, c.DisplayName AS CreatedByName, e.CreatedBy, e.RowVersion
    FROM fin.JournalEntry AS e
    JOIN fin.Journal AS j ON j.Id = e.JournalId
    LEFT JOIN fin.JournalEntry AS r ON r.ReversalOfId = e.Id
    LEFT JOIN core.[User] AS v ON v.Id = e.ValidatedBy
    LEFT JOIN core.[User] AS c ON c.Id = e.CreatedBy
    WHERE e.Id = @Id AND e.CompanyId = @CompanyId;

    SELECT l.Id, l.LineNumber AS [LineNo], l.AccountId, a.Code AS AccountCode, a.Name AS AccountName, l.Side, l.AmountRon, l.CurrencyCode, l.AmountCurrency, l.PartnerId,
           p.Name AS PartnerName, l.EmployeeId, l.CostCenterId, cc.Code AS CostCenterCode, l.ProjectId, pr.Code AS ProjectCode, l.WarehouseId,
           w.Code AS WarehouseCode, l.SiteId, s.Code AS SiteCode, l.Description, l.ResolvedFrom
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId
    JOIN fin.GlAccount AS a ON a.Id = l.AccountId
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    LEFT JOIN core.DimensionValue AS cc ON cc.Id = l.CostCenterId
    LEFT JOIN core.DimensionValue AS pr ON pr.Id = l.ProjectId
    LEFT JOIN core.DimensionValue AS w ON w.Id = l.WarehouseId
    LEFT JOIN core.DimensionValue AS s ON s.Id = l.SiteId
    WHERE l.EntryId = @Id AND e.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
