CREATE OR ALTER PROCEDURE inv.usp_RetailDischarge_Save
    @CompanyId     int,
    @WarehouseId   int,
    @PeriodStart   date,
    @Number        nvarchar(40),
    @OpeningRetail decimal(19,2),
    @RetailIn      decimal(19,2),
    @RetailOut     decimal(19,2),
    @OpeningMarkup decimal(19,2),
    @MarkupIn      decimal(19,2),
    @OpeningVat    decimal(19,2),
    @VatIn         decimal(19,2),
    @SalesVat      decimal(19,2),
    @SalesNet      decimal(19,2),
    @K             decimal(9,6),
    @Markup        decimal(19,2),
    @Cost          decimal(19,2),
    @UserId        int
AS
BEGIN
    -- Documentul DA validat (02.05, S3 3.7, 4.4): una pe gestiune si luna (54307).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM inv.RetailDischarge WITH (UPDLOCK, HOLDLOCK) WHERE WarehouseId = @WarehouseId AND PeriodStart = @PeriodStart)
        THROW 54307, N'Descărcarea la preț cu amănuntul pentru această gestiune și lună există deja.', 1;

    INSERT INTO inv.RetailDischarge (CompanyId, WarehouseId, PeriodStart, Number, OpeningRetail, RetailIn, RetailOut, OpeningMarkup, MarkupIn, OpeningVat,
        VatIn, SalesVat, SalesNet, K, Markup, Cost, ValidatedBy)
    VALUES (@CompanyId, @WarehouseId, @PeriodStart, @Number, @OpeningRetail, @RetailIn, @RetailOut, @OpeningMarkup, @MarkupIn, @OpeningVat, @VatIn,
        @SalesVat, @SalesNet, @K, @Markup, @Cost, @UserId);

    SELECT CAST(SCOPE_IDENTITY() AS int);
END;
