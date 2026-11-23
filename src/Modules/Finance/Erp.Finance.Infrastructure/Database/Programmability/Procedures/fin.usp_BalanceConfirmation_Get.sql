CREATE OR ALTER PROCEDURE fin.usp_BalanceConfirmation_Get
    @CompanyId int,
    @Id        int = NULL,
    @BatchId   int = NULL
AS
BEGIN
    -- O confirmare de sold (@Id) sau toate confirmarile unui lot (@BatchId), cu adresa de email si starea mesajului trimis (F3 3.9, 4.6).
    SET NOCOUNT ON;

    SELECT c.Id, c.BatchId, c.PartnerId, p.Name AS PartnerName, p.FiscalCode, c.Role, c.Number, c.AsOfDate, c.OurBalance, c.GeneratedAttachmentId, c.SentAt,
        c.SentTo, c.ResponseStatus, c.ResponseDate, c.PartnerBalance, c.Difference, c.ResponseNote, c.ResponseAttachmentId, c.IsResolved,
        COALESCE(pf.BalanceConfirmationEmail, p.Email) AS Email, m.Status AS EmailStatus, m.LastError AS EmailError, c.RowVersion
    FROM fin.BalanceConfirmation AS c
    JOIN core.Partner AS p ON p.Id = c.PartnerId
    LEFT JOIN fin.PartnerFinanceProfile AS pf ON pf.CompanyId = c.CompanyId AND pf.PartnerId = c.PartnerId AND pf.Role = c.Role
    LEFT JOIN core.EmailMessage AS m ON m.Id = c.EmailMessageId
    WHERE c.CompanyId = @CompanyId AND (c.Id = @Id OR c.BatchId = @BatchId)
    ORDER BY c.Number;
END;
