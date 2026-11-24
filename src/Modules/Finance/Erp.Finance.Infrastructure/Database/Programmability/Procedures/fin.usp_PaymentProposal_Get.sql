CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Fisa propunerii (macheta f5-01): antetul, liniile cu furnizorul si contul, aprobarile (toate trimiterile) si cel mai mare rand propus.
    SET NOCOUNT ON;

    SELECT pp.Id, pp.Number, pp.Name, pp.ProposalDate, pp.PlannedPaymentDate, pp.DueDateUntil, pp.DefaultTreasuryAccountId, ta.Code AS DefaultTreasuryAccountCode,
        pp.TotalAmountRon, pp.RequiredApprovalLevel, pp.CurrentApprovalLevel, pp.SubmissionNo, pp.Status, pp.LastApproverId, pp.ReturnReason, pp.SubmittedAt,
        pp.ApprovedAt, pp.GeneratedAt, pp.CreatedBy, uc.DisplayName AS CreatedByName, pp.CreatedAt, pp.RowVersion,
        ISNULL((SELECT MAX(ProposedAmountRon) FROM fin.PaymentProposalLine WHERE PaymentProposalId = pp.Id AND LineStatus = 1), 0) AS MaxLineRon
    FROM fin.PaymentProposal AS pp
    JOIN fin.TreasuryAccount AS ta ON ta.Id = pp.DefaultTreasuryAccountId
    LEFT JOIN core.[User] AS uc ON uc.Id = pp.CreatedBy
    WHERE pp.Id = @Id AND pp.CompanyId = @CompanyId;

    SELECT l.Id, l.LineNumber, l.OpenItemId, o.DocumentNo, l.PartnerId, p.Name AS PartnerName, l.DueDate, l.CurrencyCode, l.OpenAmount, l.ProposedAmount,
        l.ProposedAmountRon, l.TreasuryAccountId, ta.Code AS TreasuryAccountCode, ta.CurrencyCode AS TreasuryCurrencyCode, l.Priority, l.Warnings, l.LineStatus,
        l.RejectReason, l.PaymentOrderId, po.Number AS PaymentOrderNumber
    FROM fin.PaymentProposalLine AS l
    JOIN fin.PaymentProposal AS pp ON pp.Id = l.PaymentProposalId
    JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
    JOIN core.Partner AS p ON p.Id = l.PartnerId
    JOIN fin.TreasuryAccount AS ta ON ta.Id = l.TreasuryAccountId
    LEFT JOIN fin.PaymentOrder AS po ON po.Id = l.PaymentOrderId
    WHERE l.PaymentProposalId = @Id AND pp.CompanyId = @CompanyId
    ORDER BY l.LineNumber;

    SELECT a.Id, a.SubmissionNo, a.Level, a.UserId, u.DisplayName AS UserName, a.Decision, a.DecidedAt, a.Comment, a.RejectedLineIds
    FROM fin.PaymentApproval AS a
    JOIN fin.PaymentProposal AS pp ON pp.Id = a.PaymentProposalId
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE a.PaymentProposalId = @Id AND pp.CompanyId = @CompanyId
    ORDER BY a.Id;
END;
