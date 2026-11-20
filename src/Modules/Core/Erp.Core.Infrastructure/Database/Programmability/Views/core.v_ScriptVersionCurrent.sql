CREATE OR ALTER VIEW core.v_ScriptVersionCurrent
AS
-- Ce versiune a fiecarui script e instalata acum (08, 5.9): ultimul rand din core.ScriptVersion. Istoricul complet al unui
-- script ramane in core.ScriptVersion; ce s-a schimbat intre doua versiuni arata `git log` pe fisierul lui.
SELECT v.ScriptName,
       v.ScriptKind,
       v.Version,
       v.ContentHash,
       v.ProductVersion,
       v.AppliedAt,
       v.AppliedBy
FROM core.ScriptVersion AS v
WHERE v.Id = (SELECT MAX(l.Id) FROM core.ScriptVersion AS l WHERE l.ScriptName = v.ScriptName);
