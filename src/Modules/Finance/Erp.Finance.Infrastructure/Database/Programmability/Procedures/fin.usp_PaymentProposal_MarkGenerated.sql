CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_MarkGenerated
    @Id         int,
    @CompanyId  int,
    @Lines      nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Dupa generarea OP-urilor (F5 4.3 pasii 5–6): liniile primesc OP-ul (Generata) sau sunt sarite (factura schimbata, cu motiv); propunerea
    -- trece in Generata si iese din propunerile active. @Lines: JSON [{id, paymentOrderId, reason}].
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE l
    SET LineStatus = CASE WHEN j.PaymentOrderId IS NULL THEN 4 ELSE 3 END, PaymentOrderId = j.PaymentOrderId, RejectReason = j.Reason, ProposalIsOpen = 0
    FROM fin.PaymentProposalLine AS l
    JOIN OPENJSON(@Lines) WITH (Id bigint '$.id', PaymentOrderId int '$.paymentOrderId', Reason nvarchar(500) '$.reason') AS j ON j.Id = l.Id
    WHERE l.PaymentProposalId = @Id;
    UPDATE fin.PaymentProposalLine SET ProposalIsOpen = 0 WHERE PaymentProposalId = @Id;

    UPDATE fin.PaymentProposal
    SET Status = 4, GeneratedAt = SYSUTCDATETIME(), GeneratedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.RowVersion
    WHERE Id = @Id AND CompanyId = @CompanyId AND Status = 3;
END;
