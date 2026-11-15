CREATE OR ALTER VIEW core.v_UserLoginStatus
AS
-- Starea utilizatorului din Nucleu, citita de autentificare (02.01, 3.4 si 4.6; P1: alte componente vad core doar prin view-uri).
-- CanSignIn = contul e activ si, pentru externi, inca valabil (ValidUntil inclusiv, UTC).
SELECT u.Id AS UserId,
       u.SubjectId,
       u.UserName,
       u.DisplayName,
       u.IsSystem,
       CAST(CASE WHEN u.IsActive = 1 AND (u.ValidUntil IS NULL OR u.ValidUntil >= CAST(SYSUTCDATETIME() AS date)) THEN 1 ELSE 0 END AS bit) AS CanSignIn
FROM core.[User] AS u;
