-- Template stored procedure for paged Stoc report
-- Adjust indexes and query according to your schema

IF OBJECT_ID('dbo.sp_Rapoarte_Stoc_GetPaged', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Rapoarte_Stoc_GetPaged;
GO

CREATE PROCEDURE dbo.sp_Rapoarte_Stoc_GetPaged
    @PunctLucruId UNIQUEIDENTIFIER = NULL,
    @TipArticolId UNIQUEIDENTIFIER = NULL,
    @MinStoc DECIMAL(18,2) = NULL,
    @SortField NVARCHAR(200) = 'Cod',
    @SortDirection NVARCHAR(10) = 'ASC',
    @Skip INT = 0,
    @Take INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    -- Example implementation:
    -- 1) Build base query selecting required columns
    -- 2) Apply filters if provided
    -- 3) Return paged resultset and then total count as second resultset

    SELECT
        A.ArticolId,
        A.Cod,
        A.Denumire,
        A.TipArticolId,
        A.PunctLucruId,
        A.Cantitate,
        A.Valoare
    FROM dbo.vw_Rapoarte_Stoc AS A -- replace with actual view or join
    WHERE (@PunctLucruId IS NULL OR A.PunctLucruId = @PunctLucruId)
      AND (@TipArticolId IS NULL OR A.TipArticolId = @TipArticolId)
      AND (@MinStoc IS NULL OR A.Cantitate >= @MinStoc)
    ORDER BY
        CASE WHEN @SortField = 'Cod' AND @SortDirection = 'ASC' THEN A.Cod END ASC,
        CASE WHEN @SortField = 'Cod' AND @SortDirection = 'DESC' THEN A.Cod END DESC,
        CASE WHEN @SortField = 'Denumire' AND @SortDirection = 'ASC' THEN A.Denumire END ASC,
        CASE WHEN @SortField = 'Denumire' AND @SortDirection = 'DESC' THEN A.Denumire END DESC
    OFFSET @Skip ROWS
    FETCH NEXT @Take ROWS ONLY;

    -- Return total count as second resultset
    SELECT COUNT(1) AS TotalCount
    FROM dbo.vw_Rapoarte_Stoc AS A
    WHERE (@PunctLucruId IS NULL OR A.PunctLucruId = @PunctLucruId)
      AND (@TipArticolId IS NULL OR A.TipArticolId = @TipArticolId)
      AND (@MinStoc IS NULL OR A.Cantitate >= @MinStoc);
END
GO
