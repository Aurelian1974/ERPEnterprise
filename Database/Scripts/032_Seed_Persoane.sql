-- Seed data for Persoane table
-- Add some test records for development

INSERT INTO [dbo].[Persoane] ([Id], [Nume], [Prenume], [CNP], [Email], [Oras], [Judet], [IsActive])
VALUES
(NEWID(), 'Popescu', 'Ion', '1234567890123', 'ion.popescu@email.com', 'Bucuresti', 'Bucuresti', 1),
(NEWID(), 'Ionescu', 'Maria', '2345678901234', 'maria.ionescu@email.com', 'Cluj-Napoca', 'Cluj', 1),
(NEWID(), 'Georgescu', 'Andrei', '3456789012345', 'andrei.georgescu@email.com', 'Timisoara', 'Timis', 1),
(NEWID(), 'Dumitrescu', 'Elena', '4567890123456', 'elena.dumitrescu@email.com', 'Iasi', 'Iasi', 1),
(NEWID(), 'Stanescu', 'Mihai', '5678901234567', 'mihai.stanescu@email.com', 'Constanta', 'Constanta', 1),
(NEWID(), 'Radulescu', 'Ana', '6789012345678', 'ana.radulescu@email.com', 'Craiova', 'Dolj', 1),
(NEWID(), 'Petrescu', 'Vlad', '7890123456789', 'vlad.petrescu@email.com', 'Brasov', 'Brasov', 1),
(NEWID(), 'Vasilescu', 'Ioana', '8901234567890', 'ioana.vasilescu@email.com', 'Galati', 'Galati', 1),
(NEWID(), 'Marinescu', 'Cristian', '9012345678901', 'cristian.marinescu@email.com', 'Ploiesti', 'Prahova', 1),
(NEWID(), 'Tudor', 'Laura', '0123456789012', 'laura.tudor@email.com', 'Oradea', 'Bihor', 1);

PRINT '10 test records inserted into Persoane table.';