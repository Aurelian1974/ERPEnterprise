CREATE OR ALTER PROCEDURE fin.usp_CommercialPaper_ChangeStatus
    @Id                  int,
    @CompanyId           int,
    @Status              tinyint,
    @EventDate           date,
    @TreasuryAccountId   int = NULL,
    @BankStatementLineId bigint = NULL,
    @EndorsedToPartnerId int = NULL,
    @Note                nvarchar(500) = NULL,
    @RowVersion          binary(8),
    @UserId              int
AS
BEGIN
    -- Schimba starea unui efect (02.02, F3 3.5, 4.5), in tranzactia apelantului, cu evenimentul in istoric. Tranzitii permise (51413):
    -- primit: 1 -> 2 (depus, cu contul), 1 / 2 -> 3 (incasat), 1 / 2 -> 4 (refuzat), 1 -> 5 (girat, cu furnizorul); emis: 1 -> 3 (platit),
    -- 1 -> 4 (refuzat la plata); oricare activ -> 6 (stornat). Contul de depunere e al firmei (51023). Alta versiune: 51417.
    -- Intoarce starea veche, directia, partida efectului si versiunea noua.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @PaperCompanyId int, @Current tinyint, @Direction tinyint, @Version binary(8), @ItemId bigint;
    SELECT @PaperCompanyId = CompanyId, @Current = Status, @Direction = Direction, @Version = RowVersion, @ItemId = OpenItemId
    FROM fin.CommercialPaper WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @PaperCompanyId IS NULL OR @PaperCompanyId <> @CompanyId
        THROW 51021, N'Efectul comercial nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51417, N'Efectul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF NOT ((@Direction = 1 AND ((@Current = 1 AND @Status IN (2, 3, 4, 5, 6)) OR (@Current = 2 AND @Status IN (3, 4, 6))))
         OR (@Direction = 2 AND @Current = 1 AND @Status IN (3, 4, 6)))
        THROW 51413, N'Tranziția de stare a efectului nu este permisă.', 1;
    IF @Status = 2 AND (@TreasuryAccountId IS NULL OR NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId))
        THROW 51023, N'Alegeți contul bancar al firmei în care se depune efectul.', 1;
    IF @Status = 5 AND (@EndorsedToPartnerId IS NULL OR NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @EndorsedToPartnerId))
        THROW 51021, N'Alegeți furnizorul căruia i se girează efectul.', 1;

    UPDATE fin.CommercialPaper
    SET Status = @Status, TreasuryAccountId = CASE WHEN @Status = 2 THEN @TreasuryAccountId ELSE TreasuryAccountId END,
        EndorsedToPartnerId = CASE WHEN @Status = 5 THEN @EndorsedToPartnerId ELSE EndorsedToPartnerId END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;
    INSERT INTO fin.CommercialPaperEvent (CommercialPaperId, Status, EventDate, TreasuryAccountId, BankStatementLineId, Note, CreatedBy)
    VALUES (@Id, @Status, @EventDate, CASE WHEN @Status = 2 THEN @TreasuryAccountId END, @BankStatementLineId, @Note, @UserId);

    SELECT @Current AS PreviousStatus, @Direction AS Direction, @ItemId AS OpenItemId, RowVersion FROM fin.CommercialPaper WHERE Id = @Id;
END;
