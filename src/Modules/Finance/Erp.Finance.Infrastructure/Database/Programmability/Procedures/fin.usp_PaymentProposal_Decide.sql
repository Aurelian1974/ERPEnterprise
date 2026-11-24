CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_Decide
    @Id               int,
    @CompanyId        int,
    @Decision         tinyint,
    @Comment          nvarchar(500) = NULL,
    @RejectedLineIds  nvarchar(max) = NULL,
    @RowVersion       binary(8),
    @UserId           int
AS
BEGIN
    -- Decizia unui aprobator (F5 3.2, 4.2): 1 Aprobat (optional cu linii respinse, care ies din propunere), 2 Returnat la ciorna (cu motiv),
    -- 3 Respins integral. Nivelurile se aproba in ordine; aprobatorul are rolul regulii nivelului urmator (51706), nu e autorul (51704) si nu a
    -- aprobat alt nivel in aceeasi trimitere (51705). Dupa ultimul nivel cerut, propunerea devine Aprobata. Refuzuri: 51701, 51702, 51703.
    -- Intoarce: starea, versiunea, ultimul aprobator.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8), @Author int, @Level tinyint, @Required tinyint, @Submission int;
    SELECT @Status = Status, @Version = RowVersion, @Author = CreatedBy, @Level = CurrentApprovalLevel + 1, @Required = RequiredApprovalLevel,
        @Submission = SubmissionNo
    FROM fin.PaymentProposal WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @Id AND CompanyId = @CompanyId;

    IF @Status IS NULL
        THROW 51701, N'Propunerea de plată nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51702, N'Propunerea a fost modificată de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 2
        THROW 51703, N'Propunerea nu este în aprobare.', 1;
    IF @Author = @UserId
        THROW 51704, N'Autorul propunerii nu o poate aproba.', 1;
    IF EXISTS (SELECT 1 FROM fin.PaymentApproval WHERE PaymentProposalId = @Id AND SubmissionNo = @Submission AND UserId = @UserId)
        THROW 51705, N'Ați aprobat deja un alt nivel al acestei propuneri.', 1;
    IF NOT EXISTS (SELECT 1 FROM fin.PaymentApprovalRule AS r
                   JOIN core.UserRoleAssignment AS ura ON ura.RoleId = r.ApproverRoleId AND ura.UserId = @UserId
                                                       AND (ura.CompanyId = @CompanyId OR ura.CompanyId IS NULL)
                   WHERE r.CompanyId = @CompanyId AND r.IsActive = 1 AND r.Level = @Level)
        THROW 51706, N'Nu sunteți aprobator pentru nivelul curent al propunerii.', 1;

    INSERT INTO fin.PaymentApproval (PaymentProposalId, SubmissionNo, Level, UserId, Decision, Comment, RejectedLineIds)
    VALUES (@Id, @Submission, @Level, @UserId, @Decision, @Comment, @RejectedLineIds);

    IF @Decision = 1
    BEGIN
        UPDATE fin.PaymentProposalLine
        SET LineStatus = 2, RejectReason = ISNULL(@Comment, N'Respinsă la aprobare')
        WHERE PaymentProposalId = @Id AND LineStatus = 1 AND Id IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(ISNULL(@RejectedLineIds, N'[]')) AS j);

        UPDATE fin.PaymentProposal
        SET CurrentApprovalLevel = @Level, Status = CASE WHEN @Level >= @Required THEN 3 ELSE 2 END,
            ApprovedAt = CASE WHEN @Level >= @Required THEN SYSUTCDATETIME() END, LastApproverId = @UserId,
            TotalAmountRon = ISNULL((SELECT SUM(ProposedAmountRon) FROM fin.PaymentProposalLine WHERE PaymentProposalId = @Id AND LineStatus = 1), 0),
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    END
    ELSE IF @Decision = 2
        UPDATE fin.PaymentProposal
        SET Status = 1, CurrentApprovalLevel = 0, LastApproverId = NULL, ReturnReason = @Comment, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    ELSE
    BEGIN
        UPDATE fin.PaymentProposal SET Status = 5, ReturnReason = @Comment, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
        UPDATE fin.PaymentProposalLine SET ProposalIsOpen = 0 WHERE PaymentProposalId = @Id;
    END;

    SELECT Status, RowVersion, LastApproverId FROM fin.PaymentProposal WHERE Id = @Id;
END;
