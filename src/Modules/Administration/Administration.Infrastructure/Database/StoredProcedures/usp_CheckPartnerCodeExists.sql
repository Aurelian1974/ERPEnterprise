CREATE OR ALTER PROCEDURE administration.usp_CheckPartnerCodeExists
    @TenantId  UNIQUEIDENTIFIER,
    @Code      NVARCHAR(20),
    @ExcludeId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(
        CASE WHEN EXISTS (
            SELECT 1
        FROM administration.partners p
        WHERE p.tenant_id = @TenantId
            AND p.code      = @Code
            AND (@ExcludeId IS NULL OR p.id <> @ExcludeId)
        ) THEN 1 ELSE 0 END
    AS BIT) AS CodeExists;
END;
GO
