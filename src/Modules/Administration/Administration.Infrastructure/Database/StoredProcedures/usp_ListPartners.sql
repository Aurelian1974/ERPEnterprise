CREATE OR ALTER PROCEDURE administration.usp_ListPartners
    @TenantId   UNIQUEIDENTIFIER,
    @Search     NVARCHAR(100) = NULL,
    @Page       INT           = 1,
    @PageSize   INT           = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        p.id                AS Id,
        p.code              AS Code,
        p.name              AS Name,
        p.cui               AS Cui,
        p.is_active         AS IsActive,
        COUNT(1) OVER ()    AS TotalCount
    FROM administration.partners p
    WHERE p.tenant_id = @TenantId
        AND (
        @Search IS NULL
        OR p.name LIKE N'%' + @Search + N'%'
        OR p.code LIKE N'%' + @Search + N'%'
        OR p.cui  LIKE N'%' + @Search + N'%'
      )
    ORDER BY p.name
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;
GO
