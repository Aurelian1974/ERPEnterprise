CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_Save
    @Id                        int = NULL,
    @CompanyId                 int,
    @Number                    nvarchar(40) = NULL,
    @Name                      nvarchar(200) = NULL,
    @ProposalDate              date,
    @PlannedPaymentDate        date,
    @DueDateUntil              date,
    @DefaultTreasuryAccountId  int,
    @Lines                     fin.PaymentProposalLineList READONLY,
    @RowVersion                binary(8) = NULL,
    @UserId                    int
AS
BEGIN
    -- Salveaza o propunere ciorna cu liniile ei (F5 3.1, 4.1 pasul 6). Doar ciorna se modifica (51703); versiune diferita 51702; o factura
    -- aflata deja intr-o propunere activa (indexul unic) 51707; suma propusa peste rest 51708. Totalul = suma liniilor propuse, in lei.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM @Lines WHERE ProposedAmount <= 0 OR ProposedAmount > OpenAmount)
        THROW 51708, N'Suma propusă depășește restul de plată al facturii.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId WHERE o.CompanyId <> @CompanyId OR l.ProposedAmount > -o.OpenAmount)
        THROW 51708, N'Suma propusă depășește restul de plată al facturii.', 1;
    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @DefaultTreasuryAccountId AND CompanyId = @CompanyId)
        THROW 51021, N'Contul de trezorerie nu există în firma curentă.', 1;

    IF @Id IS NULL
    BEGIN
        IF @Number IS NULL
            SET @Number = CONCAT(N'PP ', YEAR(@ProposalDate), N'/', 1 + (SELECT COUNT(*) FROM fin.PaymentProposal WITH (UPDLOCK, HOLDLOCK)
                                                                         WHERE CompanyId = @CompanyId AND YEAR(ProposalDate) = YEAR(@ProposalDate)));
        INSERT INTO fin.PaymentProposal (CompanyId, Number, Name, ProposalDate, PlannedPaymentDate, DueDateUntil, DefaultTreasuryAccountId, CreatedBy)
        VALUES (@CompanyId, @Number, @Name, @ProposalDate, @PlannedPaymentDate, @DueDateUntil, @DefaultTreasuryAccountId, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Status tinyint, @Version binary(8);
        SELECT @Status = Status, @Version = RowVersion FROM fin.PaymentProposal WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Status IS NULL
            THROW 51701, N'Propunerea de plată nu există.', 1;
        IF @Version <> @RowVersion
            THROW 51702, N'Propunerea a fost modificată de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 51703, N'Doar o propunere ciornă se poate modifica.', 1;
        DELETE FROM fin.PaymentProposalLine WHERE PaymentProposalId = @Id;
        UPDATE fin.PaymentProposal
        SET Name = @Name, PlannedPaymentDate = @PlannedPaymentDate, DueDateUntil = @DueDateUntil, DefaultTreasuryAccountId = @DefaultTreasuryAccountId,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    END;

    BEGIN TRY
        INSERT INTO fin.PaymentProposalLine (PaymentProposalId, LineNumber, OpenItemId, PartnerId, DueDate, CurrencyCode, OpenAmount, ProposedAmount, ProposedAmountRon,
            TreasuryAccountId, Priority, Warnings, LineStatus, RejectReason)
        SELECT @Id, LineNumber, OpenItemId, PartnerId, DueDate, CurrencyCode, OpenAmount, ProposedAmount, ProposedAmountRon, TreasuryAccountId, Priority, Warnings,
            LineStatus, RejectReason
        FROM @Lines;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2601, 2627)
            THROW 51707, N'O factură este deja într-o altă propunere de plată activă.', 1;
        THROW;
    END CATCH;

    UPDATE fin.PaymentProposal
    SET TotalAmountRon = ISNULL((SELECT SUM(ProposedAmountRon) FROM fin.PaymentProposalLine WHERE PaymentProposalId = @Id AND LineStatus = 1), 0)
    WHERE Id = @Id;

    SELECT Id, RowVersion FROM fin.PaymentProposal WHERE Id = @Id;
END;
