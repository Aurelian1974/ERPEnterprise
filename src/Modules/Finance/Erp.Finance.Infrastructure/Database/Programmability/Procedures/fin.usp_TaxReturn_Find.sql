CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_Find
    @CompanyId   int,
    @FormCode    varchar(10),
    @PeriodFrom  date
AS
BEGIN
    -- Declaratia initiala a unui formular pentru perioada care incepe la data data (ecranele c4-02, c4-04), daca exista.
    SET NOCOUNT ON;
    SELECT TOP (1) Id FROM fin.TaxReturn
    WHERE CompanyId = @CompanyId AND FormCode = @FormCode AND PeriodFrom = @PeriodFrom AND Kind = 1 AND Status <> 5
    ORDER BY Id DESC;
END;
