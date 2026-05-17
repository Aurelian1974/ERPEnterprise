CREATE OR ALTER PROCEDURE administration.usp_GetNextPartnerCode
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MaxNum INT;

    SELECT @MaxNum = MAX(TRY_CAST(SUBSTRING(code, 5, LEN(code) - 4) AS INT))
    FROM administration.partners
    WHERE tenant_id = @TenantId
      AND code LIKE 'PART[0-9]%'
      AND TRY_CAST(SUBSTRING(code, 5, LEN(code) - 4) AS INT) IS NOT NULL;

    SET @MaxNum = ISNULL(@MaxNum, 0) + 1;

    SELECT 'PART' + RIGHT('00000' + CAST(@MaxNum AS VARCHAR(10)), 5) AS NextCode;
END;
GO
