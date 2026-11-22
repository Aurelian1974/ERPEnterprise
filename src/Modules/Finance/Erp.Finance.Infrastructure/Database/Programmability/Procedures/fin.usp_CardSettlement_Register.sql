CREATE OR ALTER PROCEDURE fin.usp_CardSettlement_Register
    @CompanyId              int,
    @AcquirerCode           nvarchar(30),
    @SalesDate              date,
    @GrossAmount            decimal(19,2),
    @ExpectedSettlementDate date,
    @SourceModule           varchar(30),
    @SourceRef              nvarchar(100),
    @UserId                 int
AS
BEGIN
    -- Decontarea de card asteptata (02.02, F2 3.7; ICardSettlementWriter), idempotenta pe (SourceModule, SourceRef): un raport Z livrat de
    -- doua ori creeaza o singura decontare. Ruleaza in tranzactia apelantului.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id int = (SELECT Id FROM fin.CardSettlementExpected WITH (UPDLOCK, HOLDLOCK) WHERE SourceModule = @SourceModule AND SourceRef = @SourceRef);
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.CardSettlementExpected (CompanyId, AcquirerCode, SalesDate, GrossAmount, ExpectedSettlementDate, SourceModule, SourceRef, CreatedBy)
        VALUES (@CompanyId, @AcquirerCode, @SalesDate, @GrossAmount, @ExpectedSettlementDate, @SourceModule, @SourceRef, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END;

    SELECT @Id;
END;
