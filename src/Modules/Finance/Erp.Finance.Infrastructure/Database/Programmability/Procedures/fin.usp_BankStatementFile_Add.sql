CREATE OR ALTER PROCEDURE fin.usp_BankStatementFile_Add
    @CompanyId         int,
    @TreasuryAccountId int,
    @FileName          nvarchar(260),
    @FileSha256        char(64),
    @Content           varbinary(max),
    @UserId            int
AS
BEGIN
    -- Fisierul original al unui extras (02.02, F2 3.3 si 4.1): acelasi fisier (hash) nu se importa de doua ori pe acelasi cont (51206).
    -- Ruleaza in tranzactia apelantului (SP-03).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId)
        THROW 51023, N'Contul bancar nu există în firma curentă.', 1;
    IF EXISTS (SELECT 1 FROM fin.BankStatementFile WITH (UPDLOCK, HOLDLOCK) WHERE TreasuryAccountId = @TreasuryAccountId AND FileSha256 = @FileSha256)
        THROW 51206, N'Acest fișier a fost deja importat în cont.', 1;

    INSERT INTO fin.BankStatementFile (CompanyId, TreasuryAccountId, FileName, FileSha256, SizeBytes, Content, CreatedBy)
    VALUES (@CompanyId, @TreasuryAccountId, @FileName, @FileSha256, DATALENGTH(@Content), @Content, @UserId);

    SELECT CAST(SCOPE_IDENTITY() AS int);
END;
