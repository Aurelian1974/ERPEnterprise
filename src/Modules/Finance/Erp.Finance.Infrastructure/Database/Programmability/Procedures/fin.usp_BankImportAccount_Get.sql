CREATE OR ALTER PROCEDURE fin.usp_BankImportAccount_Get
    @CompanyId         int,
    @TreasuryAccountId int = NULL,
    @Iban              varchar(34) = NULL
AS
BEGIN
    -- Contul in care se importa un extras (02.02, F2 4.1): ales dupa Id sau dupa IBAN-ul din fisier, doar din firma curenta; cu
    -- formatul profilului bancar. Cautarea dupa IBAN ia doar conturile active de tip Banca, Card sau Depozit.
    SET NOCOUNT ON;

    SELECT TOP (1) a.Id, a.Code, a.CurrencyCode, b.Iban, p.StatementFormat, p.CsvMapping
    FROM fin.TreasuryAccount AS a
    LEFT JOIN core.CompanyBankAccount AS b ON b.Id = a.CompanyBankAccountId
    LEFT JOIN fin.BankFormatProfile AS p ON p.Id = a.BankFormatProfileId
    WHERE a.CompanyId = @CompanyId
      AND (@TreasuryAccountId IS NULL OR a.Id = @TreasuryAccountId)
      AND (@Iban IS NULL OR (b.Iban = @Iban AND a.Kind IN (1, 3, 5) AND a.IsActive = 1))
    ORDER BY a.Id;
END;
