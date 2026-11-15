-- Utilizatorul tehnic system (02.01, 3.4; 08, 8.6): Id = 1, IsSystem = 1, IsActive = 0, fara cont in ids.
-- CreatedBy pentru datele livrate si joburi. SubjectId fix, pentru ca nu are cont in ids.UserAccount.
SET IDENTITY_INSERT core.[User] ON;

INSERT INTO core.[User] (Id, SubjectId, UserName, DisplayName, IsActive, IsSystem, IsEmployee, Language, CreatedBy)
VALUES (1, '00000000-0000-0000-0000-000000000001', N'system', N'Sistem', 0, 1, 0, 'ro', 1);

SET IDENTITY_INSERT core.[User] OFF;
