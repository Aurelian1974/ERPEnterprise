CREATE OR ALTER PROCEDURE fin.usp_AccountingPeriod_Get
    @CompanyId  int,
    @Id         int
AS
BEGIN
    -- O luna a firmei: datele si starea, pentru lista de verificari (C3 3.3).
    SET NOCOUNT ON;
    SELECT Id, StartDate, EndDate, Status FROM fin.AccountingPeriod WHERE Id = @Id AND CompanyId = @CompanyId;
END;
