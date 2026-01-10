-- Populate Users.FirstName and Users.LastName from Persoane when missing
UPDATE u
SET u.FirstName = p.Prenume,
    u.LastName = p.Nume
FROM dbo.Users u
INNER JOIN dbo.Persoane p ON u.PersoanaId = p.Id
WHERE (u.FirstName IS NULL OR u.FirstName = '')
  OR (u.LastName IS NULL OR u.LastName = '');

-- Verify
SELECT TOP 20 u.Email, u.FirstName, u.LastName, u.PersoanaId FROM dbo.Users u ORDER BY u.CreatedAt DESC;