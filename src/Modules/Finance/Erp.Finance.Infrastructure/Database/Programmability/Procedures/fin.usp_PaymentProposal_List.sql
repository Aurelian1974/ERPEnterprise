CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_List
    @CompanyId     int,
    @Status        tinyint = NULL,
    @PendingForUser int = NULL
AS
BEGIN
    -- Propunerile firmei, cele mai noi intai (macheta f5-01); cu @PendingForUser, doar cele in aprobare la nivelul pe care il poate aproba
    -- utilizatorul (rolul regulii nivelului urmator), fara cele create sau aprobate deja de el in trimiterea curenta (macheta f5-02).
    SET NOCOUNT ON;

    SELECT TOP (200) pp.Id, pp.Number, pp.Name, pp.ProposalDate, pp.PlannedPaymentDate, pp.TotalAmountRon, pp.Status, pp.RequiredApprovalLevel,
        pp.CurrentApprovalLevel, uc.DisplayName AS CreatedByName,
        (SELECT COUNT(*) FROM fin.PaymentProposalLine AS l WHERE l.PaymentProposalId = pp.Id AND l.LineStatus IN (1, 3)) AS LineCount,
        (SELECT COUNT(DISTINCT l.PartnerId) FROM fin.PaymentProposalLine AS l WHERE l.PaymentProposalId = pp.Id AND l.LineStatus IN (1, 3)) AS PartnerCount
    FROM fin.PaymentProposal AS pp
    LEFT JOIN core.[User] AS uc ON uc.Id = pp.CreatedBy
    WHERE pp.CompanyId = @CompanyId AND (@Status IS NULL OR pp.Status = @Status)
      AND (@PendingForUser IS NULL OR (pp.Status = 2 AND pp.CreatedBy <> @PendingForUser
           AND EXISTS (SELECT 1 FROM fin.PaymentApprovalRule AS r
                       JOIN core.UserRoleAssignment AS ura ON ura.RoleId = r.ApproverRoleId AND ura.UserId = @PendingForUser
                                                           AND (ura.CompanyId = @CompanyId OR ura.CompanyId IS NULL)
                       WHERE r.CompanyId = @CompanyId AND r.IsActive = 1 AND r.Level = pp.CurrentApprovalLevel + 1)
           AND NOT EXISTS (SELECT 1 FROM fin.PaymentApproval AS a
                           WHERE a.PaymentProposalId = pp.Id AND a.SubmissionNo = pp.SubmissionNo AND a.UserId = @PendingForUser)))
    ORDER BY pp.Id DESC;
END;
