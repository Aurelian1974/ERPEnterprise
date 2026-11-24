CREATE OR ALTER PROCEDURE fin.usp_GlAccount_RefreshPostable
    @ChartId int
AS
BEGIN
    -- Recalculeaza frunzele unui plan (C1 2, regula 3): un cont activ fara copii activi primeste note; clasele si grupele planului
    -- romanesc (nivelurile 1 si 2) nu primesc niciodata.
    SET NOCOUNT ON;

    UPDATE a SET IsPostable = x.IsPostable
    FROM fin.GlAccount AS a
    JOIN fin.ChartOfAccounts AS c ON c.Id = a.ChartId
    CROSS APPLY (SELECT CAST(CASE WHEN a.IsActive = 1 AND NOT (c.Kind = 1 AND a.Level <= 2)
                                       AND NOT EXISTS (SELECT 1 FROM fin.GlAccount AS k WHERE k.ParentId = a.Id AND k.IsActive = 1)
                                  THEN 1 ELSE 0 END AS bit) AS IsPostable) AS x
    WHERE a.ChartId = @ChartId AND a.IsPostable <> x.IsPostable;
END;
