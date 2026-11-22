CREATE OR ALTER PROCEDURE fin.usp_ExpenseReport_Transition
    @Id             int,
    @CompanyId      int,
    @RowVersion     binary(8),
    @Action         varchar(10),
    @Number         nvarchar(30) = NULL,
    @SeparateDuties bit = 1,
    @UserId         int
AS
BEGIN
    -- Pasii decontului (02.02, F1 3.7 si 4.4): Submit 1 -> 2, Approve 2 -> 3 (cine a intocmit decontul nu il aproba, cu separarea
    -- sarcinilor activa: 51019), Validate 3 -> 4 (numar din serie; avansul primeste suma justificata si isi recalculeaza starea).
    -- Starea gresita: 51025. Intoarce decontul dupa pas.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8), @DocCompanyId int, @CreatedBy int, @AdvanceId int, @Total decimal(19,2);
    SELECT @Status = Status, @Version = RowVersion, @DocCompanyId = CompanyId, @CreatedBy = CreatedBy, @AdvanceId = CashAdvanceId,
           @Total = TotalJustified
    FROM fin.ExpenseReport WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;

    IF @Status IS NULL OR @DocCompanyId <> @CompanyId
        THROW 51024, N'Decontul nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51002, N'Decontul a fost modificat de alt utilizator; reîncărcați datele.', 1;

    IF @Action = 'Submit'
    BEGIN
        IF @Status <> 1 THROW 51025, N'Doar o ciornă se trimite spre aprobare.', 1;
        IF NOT EXISTS (SELECT 1 FROM fin.ExpenseReportLine WHERE ExpenseReportId = @Id)
            THROW 51021, N'Decontul nu are cheltuieli.', 1;
        UPDATE fin.ExpenseReport SET Status = 2, SubmittedAt = SYSUTCDATETIME(), SubmittedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    END
    ELSE IF @Action = 'Approve'
    BEGIN
        IF @Status <> 2 THROW 51025, N'Doar un decont trimis spre aprobare se aprobă.', 1;
        IF @SeparateDuties = 1 AND @CreatedBy = @UserId
            THROW 51019, N'Cine a întocmit decontul nu îl poate aproba (separarea sarcinilor).', 1;
        UPDATE fin.ExpenseReport SET Status = 3, ApprovedAt = SYSUTCDATETIME(), ApprovedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
    END
    ELSE IF @Action = 'Validate'
    BEGIN
        IF @Status <> 3 THROW 51025, N'Doar un decont aprobat se validează.', 1;
        IF @Number IS NULL THROW 51021, N'Decontul primește numărul din serie la validare.', 1;
        UPDATE fin.ExpenseReport SET Status = 4, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ModifiedAt = SYSUTCDATETIME(),
            ModifiedBy = @UserId
        WHERE Id = @Id;
        IF @AdvanceId IS NOT NULL
        BEGIN
            UPDATE fin.CashAdvance WITH (UPDLOCK) SET SettledAmount = SettledAmount + @Total, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
            WHERE Id = @AdvanceId;
            EXEC fin.usp_CashAdvance_RefreshStatus @CashAdvanceId = @AdvanceId, @UserId = @UserId;
        END;
    END
    ELSE
        THROW 51021, N'Pas necunoscut pentru decont.', 1;

    SELECT Id, Number, Status, Difference, CashAdvanceId, RowVersion FROM fin.ExpenseReport WHERE Id = @Id;
END;
