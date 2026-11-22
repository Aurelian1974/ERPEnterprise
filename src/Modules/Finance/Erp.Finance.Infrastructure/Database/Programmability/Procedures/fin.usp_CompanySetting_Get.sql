CREATE OR ALTER PROCEDURE fin.usp_CompanySetting_Get
    @CompanyId int,
    @Code      varchar(60)
AS
BEGIN
    -- O setare a Finantelor pe firma; lipsa inseamna valoarea implicita (FinanceSettings in C#).
    SET NOCOUNT ON;
    SELECT Value FROM fin.CompanySetting WHERE CompanyId = @CompanyId AND Code = @Code;
END;
