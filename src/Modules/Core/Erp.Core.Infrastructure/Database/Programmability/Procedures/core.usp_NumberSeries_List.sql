CREATE OR ALTER PROCEDURE core.usp_NumberSeries_List
    @CompanyId int,
    @Year      smallint = NULL
AS
BEGIN
    -- Seriile unei firme (macheta 07c), cu tipul de document, punctul de lucru si numarul de numere folosite.
    SET NOCOUNT ON;

    SELECT s.Id, s.CompanyId, s.BranchId, b.Code AS BranchCode, b.Name AS BranchName, t.Code AS DocumentTypeCode,
           t.Name AS DocumentTypeName, s.Prefix, s.[Year], s.NextNumber, s.FirstNumber, s.NextNumber - s.FirstNumber AS UsedCount,
           s.Padding, s.IsFiscal, s.IsDefault, s.AutoRollover, s.LastUsedAt, s.ClosedAt, s.RowVersion
    FROM core.NumberSeries AS s
    JOIN core.DocumentType AS t ON t.Id = s.DocumentTypeId
    LEFT JOIN core.Branch AS b ON b.Id = s.BranchId
    WHERE s.CompanyId = @CompanyId AND (@Year IS NULL OR s.[Year] = @Year)
    ORDER BY s.[Year] DESC, CASE WHEN s.ClosedAt IS NULL THEN 0 ELSE 1 END, t.Name, b.Code, s.Prefix;
END;
