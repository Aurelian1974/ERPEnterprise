CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_Transition
    @Id             int,
    @CompanyId      int,
    @Action         tinyint,
    @RequiredLevel  tinyint = NULL,
    @Reason         nvarchar(500) = NULL,
    @RowVersion     binary(8),
    @UserId         int
AS
BEGIN
    -- Ciclul propunerii fara decizie de aprobare (F5 3.1): 1 Trimite la aprobare (ciorna → in aprobare, nivelul cerut calculat de apelant,
    -- aprobarile anterioare nu mai conteaza: o trimitere noua); 2 Retrage (in aprobare, de autor, sau aprobata inca negenerata → ciorna, cu
    -- motiv); 3 Sterge (ciorna). Inexistenta 51701, versiune 51702, stare nepermisa 51703. Intoarce versiunea (NULL la stergere).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8), @Author int;
    SELECT @Status = Status, @Version = RowVersion, @Author = CreatedBy FROM fin.PaymentProposal WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 51701, N'Propunerea de plată nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51702, N'Propunerea a fost modificată de alt utilizator; reîncărcați datele.', 1;

    IF @Action = 1
    BEGIN
        IF @Status <> 1
            THROW 51703, N'Doar o propunere ciornă se trimite la aprobare.', 1;
        IF NOT EXISTS (SELECT 1 FROM fin.PaymentProposalLine WHERE PaymentProposalId = @Id AND LineStatus = 1)
            THROW 51708, N'Propunerea nu are nicio linie de plătit.', 1;
        UPDATE fin.PaymentProposal
        SET Status = 2, RequiredApprovalLevel = @RequiredLevel, CurrentApprovalLevel = 0, SubmissionNo = SubmissionNo + 1, SubmittedAt = SYSUTCDATETIME(),
            ApprovedAt = NULL, LastApproverId = NULL, ReturnReason = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        OUTPUT inserted.RowVersion
        WHERE Id = @Id;
    END
    ELSE IF @Action = 2
    BEGIN
        IF NOT (@Status = 2 AND @Author = @UserId) AND @Status <> 3
            THROW 51703, N'Se retrage doar o propunere în aprobare (de autor) sau una aprobată, încă negenerată.', 1;
        UPDATE fin.PaymentProposal
        SET Status = 1, CurrentApprovalLevel = 0, ApprovedAt = NULL, LastApproverId = NULL, ReturnReason = @Reason, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        OUTPUT inserted.RowVersion
        WHERE Id = @Id;
    END
    ELSE
    BEGIN
        IF @Status <> 1
            THROW 51703, N'Doar o propunere ciornă se șterge.', 1;
        DELETE FROM fin.PaymentProposalLine WHERE PaymentProposalId = @Id;
        DELETE FROM fin.PaymentProposal WHERE Id = @Id;
        SELECT CAST(NULL AS binary(8)) AS RowVersion;
    END;
END;
