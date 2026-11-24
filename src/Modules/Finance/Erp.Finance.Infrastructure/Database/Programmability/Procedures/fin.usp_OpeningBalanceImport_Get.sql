CREATE OR ALTER PROCEDURE fin.usp_OpeningBalanceImport_Get
    @CompanyId  int
AS
BEGIN
    -- Ultimul import de solduri initiale al firmei (macheta c3-05) si randurile lui, cu contul si partenerul.
    SET NOCOUNT ON;

    DECLARE @Id int = (SELECT TOP (1) Id FROM fin.OpeningBalanceImport WHERE CompanyId = @CompanyId ORDER BY Id DESC);
    SELECT Id, StartDate, Status, FileName, OpeningEntryId, TurnoverEntryId, ValidatedAt, RowVersion FROM fin.OpeningBalanceImport WHERE Id = @Id;
    SELECT r.RowNumber, a.Code AS AccountCode, a.Name AS AccountName, r.PartnerId, p.Name AS PartnerName, r.CurrencyCode, r.OpeningDebit, r.OpeningCredit,
        r.TurnoverDebit, r.TurnoverCredit, r.ClosingDebit, r.ClosingCredit
    FROM fin.OpeningBalanceImportRow AS r JOIN fin.GlAccount AS a ON a.Id = r.AccountId LEFT JOIN core.Partner AS p ON p.Id = r.PartnerId
    WHERE r.ImportId = @Id ORDER BY r.RowNumber;
END;
