CREATE OR ALTER VIEW core.v_UserLoginStatus
AS
-- Starea utilizatorului din Nucleu, citita de autentificare (02.01, 3.4 si 4.6; P1: alte componente vad core doar prin view-uri).
-- CanSignIn = contul e activ, pentru externi inca valabil (ValidFrom..ValidUntil inclusiv, UTC), iar pentru angajati
-- contractul nu a incetat (LeaveDate in viitor). Jobul zilnic doar trece apoi contul pe inactiv (3.4).
SELECT u.Id AS UserId,
       u.SubjectId,
       u.UserName,
       u.DisplayName,
       u.IsSystem,
       CAST(CASE WHEN u.IsActive = 1
                  AND (u.ValidUntil IS NULL OR u.ValidUntil >= CAST(SYSUTCDATETIME() AS date))
                  AND (u.ValidFrom IS NULL OR u.ValidFrom <= CAST(SYSUTCDATETIME() AS date))
                  AND (e.LeaveDate IS NULL OR e.LeaveDate > CAST(SYSUTCDATETIME() AS date))
                 THEN 1 ELSE 0 END AS bit) AS CanSignIn
FROM core.[User] AS u
LEFT JOIN core.Employee AS e ON e.Id = u.EmployeeId;
