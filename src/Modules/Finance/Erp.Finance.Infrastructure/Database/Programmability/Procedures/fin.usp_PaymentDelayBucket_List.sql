CREATE OR ALTER PROCEDURE fin.usp_PaymentDelayBucket_List
    @CompanyId int
AS
BEGIN
    -- Intervalele de intarziere ale firmei (F5 3.4), in ordine.
    SET NOCOUNT ON;

    SELECT Id, Code, Name, DaysFrom, DaysTo, SortOrder, IsUncollectible
    FROM fin.PaymentDelayBucket
    WHERE CompanyId = @CompanyId
    ORDER BY SortOrder;
END;
