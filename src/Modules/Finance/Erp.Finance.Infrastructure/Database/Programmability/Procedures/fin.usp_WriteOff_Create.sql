CREATE OR ALTER PROCEDURE fin.usp_WriteOff_Create
    @CompanyId           int,
    @OpenItemId          bigint,
    @Amount              decimal(19,2) = NULL,
    @Reason              tinyint,
    @Note                nvarchar(500) = NULL,
    @DocumentDate        date,
    @MaxAmountRon        decimal(19,2),
    @AllowAboveThreshold bit,
    @UserId              int
AS
BEGIN
    -- O ajustare de diferenta mica (02.02, F3 3.6, 4.8), in tranzactia apelantului: partida ajustata (blocata) e a firmei si deschisa (51021),
    -- suma (implicit tot restul) nu depaseste restul (51403); peste pragul in lei cere permisiunea dedicata (51414). Creeaza documentul si
    -- partida lui de tip 9, de semn opus; alocarea (Source = 6) o face apelantul. Intoarce documentul, partida noua si suma.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ItemCompanyId int, @PartnerId int, @Role tinyint, @Kind tinyint, @Open decimal(19,2), @Currency char(3), @Rate decimal(19,6), @DocumentNo nvarchar(50);
    SELECT @ItemCompanyId = CompanyId, @PartnerId = PartnerId, @Role = Role, @Kind = Kind, @Open = OpenAmount, @Currency = CurrencyCode, @Rate = ExchangeRate,
        @DocumentNo = DocumentNo
    FROM fin.OpenItem WITH (UPDLOCK, ROWLOCK) WHERE Id = @OpenItemId;

    IF @ItemCompanyId IS NULL OR @ItemCompanyId <> @CompanyId
        THROW 51021, N'Partida nu există în firma curentă.', 1;
    IF @Open = 0 OR @Kind = 9
        THROW 51021, N'Partida este închisă sau este chiar o ajustare.', 1;

    SET @Amount = ISNULL(@Amount, ABS(@Open));
    IF @Amount <= 0 OR @Amount > ABS(@Open)
        THROW 51403, N'Ajustarea depășește restul de plată al partidei.', 1;

    DECLARE @AmountRon decimal(19,2) = ROUND(@Amount * @Rate, 2);
    IF @AmountRon > @MaxAmountRon AND @AllowAboveThreshold = 0
        THROW 51414, N'Ajustarea depășește pragul pentru diferențe mici; cere permisiunea Fin.WriteOff.AboveThreshold.', 1;

    INSERT INTO fin.WriteOff (CompanyId, OpenItemId, Amount, AmountRon, Reason, Note, DocumentDate, AboveThreshold, CreatedBy)
    VALUES (@CompanyId, @OpenItemId, @Amount, @AmountRon, @Reason, @Note, @DocumentDate, CASE WHEN @AmountRon > @MaxAmountRon THEN 1 ELSE 0 END, @UserId);
    DECLARE @WriteOffId int = SCOPE_IDENTITY();

    DECLARE @Signed decimal(19,2) = CASE WHEN @Open > 0 THEN -@Amount ELSE @Amount END;
    INSERT INTO fin.OpenItem (CompanyId, PartnerId, Role, Kind, SourceModule, SourceDocumentType, SourceDocumentId, SourceLineKey, DocumentNo, DocumentDate,
        AccountingDate, DueDate, CurrencyCode, ExchangeRate, Amount, AmountRon, OpenAmount, OpenAmountRon, CarryingRate, CreatedBy)
    VALUES (@CompanyId, @PartnerId, @Role, 9, 'Finance', 'Finance.WriteOff', @WriteOffId, N'', LEFT(N'AJ ' + @DocumentNo, 50), @DocumentDate, @DocumentDate,
        @DocumentDate, @Currency, @Rate, @Signed, CASE WHEN @Signed > 0 THEN @AmountRon ELSE -@AmountRon END, @Signed,
        CASE WHEN @Signed > 0 THEN @AmountRon ELSE -@AmountRon END, @Rate, @UserId);
    DECLARE @WriteOffItemId bigint = SCOPE_IDENTITY();

    UPDATE fin.WriteOff SET WriteOffOpenItemId = @WriteOffItemId WHERE Id = @WriteOffId;

    SELECT @WriteOffId AS WriteOffId, @WriteOffItemId AS WriteOffOpenItemId, @Amount AS Amount, CAST(CASE WHEN @Open > 0 THEN 1 ELSE 0 END AS bit) AS ItemIsDebit;
END;
