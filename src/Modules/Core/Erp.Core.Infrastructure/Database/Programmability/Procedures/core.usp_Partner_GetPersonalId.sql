CREATE OR ALTER PROCEDURE core.usp_Partner_GetPersonalId
    @Id int
AS
BEGIN
    -- CNP-ul criptat al persoanei fizice (GDPR, A13); decriptarea si jurnalizarea accesului se fac in aplicatie.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @Id)
        THROW 50027, N'Partenerul nu există.', 1;

    SELECT PersonalIdEncrypted FROM core.Partner WHERE Id = @Id;
END;
