CREATE OR ALTER PROCEDURE fin.usp_CardAcquirer_List
    @CompanyId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Code, Name, CounterpartyIban, IsActive, RowVersion FROM fin.CardAcquirer WHERE CompanyId = @CompanyId ORDER BY IsActive DESC, CodeKey;
END;
