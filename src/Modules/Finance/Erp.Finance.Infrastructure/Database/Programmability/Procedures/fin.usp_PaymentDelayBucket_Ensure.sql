CREATE OR ALTER PROCEDURE fin.usp_PaymentDelayBucket_Ensure
    @CompanyId int,
    @UserId    int
AS
BEGIN
    -- Intervalele de intarziere livrate (F5 3.4), create la prima folosire pe o firma fara intervale.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.PaymentDelayBucket WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId)
        INSERT INTO fin.PaymentDelayBucket (CompanyId, Code, Name, DaysFrom, DaysTo, SortOrder, IsUncollectible, CreatedBy)
        VALUES (@CompanyId, N'B0', N'La termen sau înainte', NULL, 0, 1, 0, @UserId),
               (@CompanyId, N'B1', N'1–30 zile', 1, 30, 2, 0, @UserId),
               (@CompanyId, N'B2', N'31–60 zile', 31, 60, 3, 0, @UserId),
               (@CompanyId, N'B3', N'61–90 zile', 61, 90, 4, 0, @UserId),
               (@CompanyId, N'B4', N'91–180 zile', 91, 180, 5, 0, @UserId),
               (@CompanyId, N'B5', N'Peste 180 de zile', 181, NULL, 6, 1, @UserId);
END;
