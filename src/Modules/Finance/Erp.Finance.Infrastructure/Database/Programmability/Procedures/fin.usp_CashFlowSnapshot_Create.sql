CREATE OR ALTER PROCEDURE fin.usp_CashFlowSnapshot_Create
    @CompanyId          int,
    @Name               nvarchar(200),
    @UserId             int,
    @StartDate          date,
    @HorizonFrom        date,
    @HorizonTo          date,
    @Granularity        tinyint,
    @TreasuryAccountId  int = NULL,
    @CurrencyCode       char(3) = NULL,
    @IsAutomatic        bit,
    @OpeningBalanceRon  decimal(19,2),
    @Parameters         nvarchar(max) = NULL,
    @Lines              nvarchar(max)
AS
BEGIN
    -- Un instantaneu al prognozei (F5 3.6): antetul si sumele grupate pe perioada, categorie, cont, moneda si sursa, intr-o singura
    -- tranzactie. Lines: JSON [{periodStart, categoryId, treasuryAccountId, currencyCode, amount, amountRon, sourceKind}].
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    INSERT INTO fin.CashFlowSnapshot (CompanyId, Name, TakenBy, StartDate, HorizonFrom, HorizonTo, Granularity, TreasuryAccountId, CurrencyCode, IsAutomatic,
        OpeningBalanceRon, Parameters)
    VALUES (@CompanyId, @Name, @UserId, @StartDate, @HorizonFrom, @HorizonTo, @Granularity, @TreasuryAccountId, @CurrencyCode, @IsAutomatic,
        @OpeningBalanceRon, @Parameters);
    DECLARE @Id int = CAST(SCOPE_IDENTITY() AS int);

    INSERT INTO fin.CashFlowSnapshotLine (SnapshotId, PeriodStart, CategoryId, TreasuryAccountId, CurrencyCode, Amount, AmountRon, SourceKind)
    SELECT @Id, PeriodStart, CategoryId, TreasuryAccountId, CurrencyCode, Amount, AmountRon, SourceKind
    FROM OPENJSON(@Lines) WITH (PeriodStart date '$.periodStart', CategoryId int '$.categoryId', TreasuryAccountId int '$.treasuryAccountId',
        CurrencyCode char(3) '$.currencyCode', Amount decimal(19,2) '$.amount', AmountRon decimal(19,2) '$.amountRon', SourceKind tinyint '$.sourceKind');
    COMMIT TRANSACTION;

    SELECT @Id AS Id;
END;
