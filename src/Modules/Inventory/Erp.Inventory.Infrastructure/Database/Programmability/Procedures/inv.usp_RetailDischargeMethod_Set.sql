CREATE OR ALTER PROCEDURE inv.usp_RetailDischargeMethod_Set
    @CompanyId   int,
    @WarehouseId int = NULL,
    @Method      tinyint = NULL,
    @Today       date
AS
BEGIN
    -- Metoda de descarcare (02.05, S3 3.7, decizia 4): pe firma (@WarehouseId NULL, metoda obligatorie) sau pe gestiune (NULL = ca pe firma).
    -- Se schimba doar de la inceputul lunii: nicio miscare din luna curenta in gestiunile la pret cu amanuntul atinse (54315).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @MonthStart date = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    IF @WarehouseId IS NULL AND @Method IS NULL
        THROW 54315, N'Metoda de descărcare a firmei este obligatorie.', 1;
    IF @WarehouseId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id = @WarehouseId AND CompanyId = @CompanyId AND ValuationBasis = 2)
        THROW 54312, N'Metoda de descărcare se setează doar pentru o gestiune la preț cu amănuntul a firmei.', 1;
    IF EXISTS (SELECT 1 FROM inv.StockMovement AS m JOIN inv.Warehouse AS w ON w.Id = m.WarehouseId
               WHERE w.CompanyId = @CompanyId AND w.ValuationBasis = 2 AND m.MovementDate >= @MonthStart
                 AND (@WarehouseId IS NULL OR w.Id = @WarehouseId))
        THROW 54315, N'Metoda de descărcare se schimbă doar de la începutul unei luni, fără mișcări în luna respectivă.', 1;

    IF @WarehouseId IS NULL
        UPDATE inv.StockSettings SET RetailDischargeMethod = @Method WHERE CompanyId = @CompanyId;
    ELSE
        UPDATE inv.Warehouse SET RetailDischargeMethod = @Method WHERE Id = @WarehouseId;
END;
