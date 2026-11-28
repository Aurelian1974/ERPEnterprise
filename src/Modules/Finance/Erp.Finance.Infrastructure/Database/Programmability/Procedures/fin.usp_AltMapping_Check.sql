CREATE OR ALTER PROCEDURE fin.usp_AltMapping_Check
    @ChartId int
AS
BEGIN
    -- Verificarea unui plan alternativ (C1 4.5), partea fara linii de nota: conturile alternative frunza, active, pe care nu le atinge nicio
    -- regula activa (informativ). Suprapunerile si regulile care nu se aplica niciodata se calculeaza in C# din reguli si din planul romanesc;
    -- nemapatele vin cu notele contabile (C2).
    SET NOCOUNT ON;

    SELECT a.Id, a.Code, a.Name
    FROM fin.GlAccount AS a
    WHERE a.ChartId = @ChartId AND a.IsPostable = 1 AND a.IsActive = 1
      AND NOT EXISTS (SELECT 1 FROM fin.AccountMappingTarget AS t JOIN fin.AccountMappingRule AS r ON r.Id = t.RuleId
                      WHERE t.TargetAccountId = a.Id AND r.IsActive = 1)
    ORDER BY a.Code;
END;
