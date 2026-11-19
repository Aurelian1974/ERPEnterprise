-- Setarile implicite ale instalarii (08, 5.9; 02.01, 3.5): se adauga doar daca lipsesc, fara a schimba valoarea aleasa de client (MIG-06).
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE core.Setting AS target
USING (VALUES
    ('Installation.SetupCompleted', N'false')
) AS source ([Key], Value)
ON target.Scope = 'Installation' AND target.ScopeId IS NULL AND target.[Key] = source.[Key]
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Scope, ScopeId, [Key], Value, CreatedBy)
    VALUES ('Installation', NULL, source.[Key], source.Value, 1);
