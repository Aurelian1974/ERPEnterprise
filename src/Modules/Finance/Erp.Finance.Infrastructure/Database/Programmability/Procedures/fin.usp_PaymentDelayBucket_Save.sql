CREATE OR ALTER PROCEDURE fin.usp_PaymentDelayBucket_Save
    @CompanyId  int,
    @Buckets    nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Intervalele de intarziere ale firmei (F5 3.4), inlocuite in bloc: JSON [{code, name, daysFrom, daysTo, isUncollectible}] in ordine,
    -- validate in C# (fara suprapuneri, toata axa). Istoricul vechi se sterge; apelantul il recalculeaza imediat (F5 4.6).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DELETE FROM fin.PartnerPaymentBehavior WHERE CompanyId = @CompanyId;
    DELETE FROM fin.PaymentDelayBucket WHERE CompanyId = @CompanyId;
    INSERT INTO fin.PaymentDelayBucket (CompanyId, Code, Name, DaysFrom, DaysTo, SortOrder, IsUncollectible, CreatedBy)
    SELECT @CompanyId, Code, Name, DaysFrom, DaysTo, CAST([key] AS int) + 1, IsUncollectible, @UserId
    FROM OPENJSON(@Buckets) AS j
    CROSS APPLY OPENJSON(j.value) WITH (Code nvarchar(10) '$.code', Name nvarchar(100) '$.name', DaysFrom int '$.daysFrom', DaysTo int '$.daysTo',
        IsUncollectible bit '$.isUncollectible');
    COMMIT TRANSACTION;
END;
