CREATE OR ALTER VIEW ids.v_UserAccountSecurity
AS
-- Starea de securitate a contului, citita de administrarea utilizatorilor din Nucleu (02.01, 3.4 si 11: lista cu filtrul
-- „Fără 2FA”, tab-ul Securitate). Fara hash-uri sau chei: doar faptele afisate administratorului.
SELECT ua.SubjectId,
       CAST(CASE WHEN ua.PasswordHash IS NULL THEN 0 ELSE 1 END AS bit) AS HasPassword,
       ua.PasswordChangedAt,
       ua.MustChangePassword,
       ua.TwoFactorEnabled,
       ua.TwoFactorRequired,
       CASE WHEN ua.LockoutEndAt > SYSUTCDATETIME() THEN ua.LockoutEndAt END AS LockedUntil,
       ua.AccessFailedCount,
       ua.IsActive,
       (SELECT COUNT(*) FROM ids.UserSession AS s WHERE s.UserAccountId = ua.Id AND s.RevokedAt IS NULL) AS ActiveSessions
FROM ids.UserAccount AS ua;
