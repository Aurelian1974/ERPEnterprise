CREATE OR ALTER PROCEDURE fin.usp_AccountingPeriod_State
    @CompanyId  int,
    @Day        date
AS
BEGIN
    -- Starea lunii care contine ziua (C3 6, IAccountingPeriodGuard); fara rand daca firma nu are an financiar pentru data.
    SET NOCOUNT ON;

    SELECT TOP (1) Status FROM fin.AccountingPeriod WHERE CompanyId = @CompanyId AND StartDate <= @Day AND EndDate >= @Day;
END;
