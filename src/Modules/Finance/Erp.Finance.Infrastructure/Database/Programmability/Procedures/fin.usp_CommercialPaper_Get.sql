CREATE OR ALTER PROCEDURE fin.usp_CommercialPaper_Get
    @Id        int,
    @CompanyId int
AS
BEGIN
    -- Un efect comercial cu istoricul starilor si facturile stinse (F3 3.5; macheta f3-06).
    SET NOCOUNT ON;

    SELECT c.Id, c.PartnerId, p.Name AS PartnerName, c.Direction, c.Kind, c.Series, c.Number, c.IssuerBank, c.IssuerAccount, c.Amount, c.CurrencyCode, c.IssueDate,
        c.MaturityDate, c.TreasuryAccountId, ta.Code AS TreasuryAccountCode, c.Status, c.GlAccountCode, c.OpenItemId, c.EndorsedToPartnerId, ep.Name AS EndorsedToPartnerName,
        c.Note, c.RowVersion
    FROM fin.CommercialPaper AS c
    JOIN core.Partner AS p ON p.Id = c.PartnerId
    LEFT JOIN fin.TreasuryAccount AS ta ON ta.Id = c.TreasuryAccountId
    LEFT JOIN core.Partner AS ep ON ep.Id = c.EndorsedToPartnerId
    WHERE c.Id = @Id AND c.CompanyId = @CompanyId;

    SELECT e.Status, e.EventDate, ta.Code AS TreasuryAccountCode, e.BankStatementLineId, e.Note, e.CreatedAt, u.DisplayName AS CreatedByName
    FROM fin.CommercialPaperEvent AS e
    JOIN fin.CommercialPaper AS c ON c.Id = e.CommercialPaperId AND c.CompanyId = @CompanyId
    LEFT JOIN fin.TreasuryAccount AS ta ON ta.Id = e.TreasuryAccountId
    LEFT JOIN core.[User] AS u ON u.Id = e.CreatedBy
    WHERE e.CommercialPaperId = @Id
    ORDER BY e.Id;

    SELECT o.Id AS OpenItemId, o.DocumentNo, o.DocumentDate, a.DebitAmount AS Amount, a.Status AS AllocationStatus
    FROM fin.Allocation AS a
    JOIN fin.CommercialPaper AS c ON c.Id = a.CommercialPaperId AND c.CompanyId = @CompanyId
    JOIN fin.OpenItem AS o ON o.Id = CASE WHEN a.DebitOpenItemId = c.OpenItemId THEN a.CreditOpenItemId ELSE a.DebitOpenItemId END
    WHERE a.CommercialPaperId = @Id
    ORDER BY a.Id;
END;
