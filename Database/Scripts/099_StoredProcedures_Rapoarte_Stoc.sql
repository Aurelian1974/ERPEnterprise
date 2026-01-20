IF OBJECT_ID('dbo.sp_Rapoarte_Stoc_Get', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Rapoarte_Stoc_Get;
GO

CREATE PROCEDURE dbo.sp_Rapoarte_Stoc_Get
    @PunctLucruId UNIQUEIDENTIFIER = NULL,
    @TipArticolId UNIQUEIDENTIFIER = NULL,
    @MinStoc DECIMAL(18,4) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Use actual table/column names: ArticolCode, ArticolName, PretVanzare / PretAchizitie and Stock.Quantity
    IF OBJECT_ID('dbo.Stock') IS NOT NULL
    BEGIN
        SELECT
            a.Id AS ArticolId,
            a.ArticolCode AS Cod,
            a.ArticolName AS Denumire,
            a.TipArticolId,
            s.LocationId AS PunctLucruId,
            ISNULL(s.Quantity, 0) AS Cantitate,
            ISNULL(s.Quantity, 0) * ISNULL(COALESCE(a.PretVanzare, a.PretAchizitie), 0) AS Valoare
        FROM dbo.Articole a
        LEFT JOIN dbo.Stock s ON s.ItemId = a.Id
        WHERE (@PunctLucruId IS NULL OR s.LocationId = @PunctLucruId)
          AND (@TipArticolId IS NULL OR a.TipArticolId = @TipArticolId)
          AND (@MinStoc IS NULL OR ISNULL(s.Quantity,0) >= @MinStoc);
    END
    ELSE
    BEGIN
        -- Fallback to listing articles with zero stock if Stock table missing
        SELECT
            a.Id AS ArticolId,
            a.ArticolCode AS Cod,
            a.ArticolName AS Denumire,
            a.TipArticolId,
            NULL AS PunctLucruId,
            0.0 AS Cantitate,
            0.0 AS Valoare
        FROM dbo.Articole a
        WHERE (@TipArticolId IS NULL OR a.TipArticolId = @TipArticolId);
    END
END
GO
