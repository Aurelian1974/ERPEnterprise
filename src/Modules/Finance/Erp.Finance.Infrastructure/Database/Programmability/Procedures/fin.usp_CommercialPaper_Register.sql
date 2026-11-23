CREATE OR ALTER PROCEDURE fin.usp_CommercialPaper_Register
    @CompanyId     int,
    @PartnerId     int,
    @Direction     tinyint,
    @Kind          tinyint,
    @Series        nvarchar(20),
    @Number        nvarchar(30),
    @IssuerBank    nvarchar(100) = NULL,
    @IssuerAccount varchar(34) = NULL,
    @Amount        decimal(19,2),
    @CurrencyCode  char(3),
    @ExchangeRate  decimal(19,6),
    @IssueDate     date,
    @MaturityDate  date,
    @Note          nvarchar(500) = NULL,
    @UserId        int
AS
BEGIN
    -- Inregistreaza un efect comercial (02.02, F3 3.5, 4.5 pasul 1), in tranzactia apelantului: instrumentul unic pe partener (51412), partida
    -- lui de tip 8 (primit: credit la client; emis: debit la furnizor) si primul eveniment din istoric; alocarile pe facturi le face apelantul.
    -- Contul de referinta: 5112 CEC de incasat, 5113 efecte de incasat, 403 efecte de platit.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
        THROW 51021, N'Partenerul nu există.', 1;
    IF EXISTS (SELECT 1 FROM fin.CommercialPaper WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND PartnerId = @PartnerId AND Kind = @Kind AND Series = @Series AND Number = @Number)
        THROW 51412, N'Efectul cu această serie și acest număr este deja înregistrat.', 1;

    DECLARE @Gl nvarchar(20) = CASE WHEN @Direction = 2 THEN N'403' WHEN @Kind = 1 THEN N'5112' ELSE N'5113' END;
    INSERT INTO fin.CommercialPaper (CompanyId, PartnerId, Direction, Kind, Series, Number, IssuerBank, IssuerAccount, Amount, CurrencyCode, IssueDate, MaturityDate,
        GlAccountCode, Note, CreatedBy)
    VALUES (@CompanyId, @PartnerId, @Direction, @Kind, @Series, @Number, @IssuerBank, @IssuerAccount, @Amount, @CurrencyCode, @IssueDate, @MaturityDate, @Gl, @Note,
        @UserId);
    DECLARE @Id int = SCOPE_IDENTITY();

    DECLARE @Signed decimal(19,2) = CASE WHEN @Direction = 1 THEN -@Amount ELSE @Amount END;
    INSERT INTO fin.OpenItem (CompanyId, PartnerId, Role, Kind, SourceModule, SourceDocumentType, SourceDocumentId, SourceLineKey, DocumentNo, DocumentDate,
        AccountingDate, DueDate, CurrencyCode, ExchangeRate, Amount, AmountRon, OpenAmount, OpenAmountRon, CarryingRate, GlAccountCode, CreatedBy)
    VALUES (@CompanyId, @PartnerId, @Direction, 8, 'Finance', 'Finance.CommercialPaper', @Id, N'',
        LEFT(CASE WHEN @Kind = 1 THEN N'CEC ' ELSE N'BO ' END + @Series + N' ' + @Number, 50), @IssueDate, @IssueDate, @MaturityDate, @CurrencyCode, @ExchangeRate,
        @Signed, ROUND(@Signed * @ExchangeRate, 2), @Signed, ROUND(@Signed * @ExchangeRate, 2), @ExchangeRate, @Gl, @UserId);
    DECLARE @ItemId bigint = SCOPE_IDENTITY();

    UPDATE fin.CommercialPaper SET OpenItemId = @ItemId WHERE Id = @Id;
    INSERT INTO fin.CommercialPaperEvent (CommercialPaperId, Status, EventDate, Note, CreatedBy) VALUES (@Id, 1, @IssueDate, @Note, @UserId);

    SELECT @Id AS Id, @ItemId AS OpenItemId;
END;
