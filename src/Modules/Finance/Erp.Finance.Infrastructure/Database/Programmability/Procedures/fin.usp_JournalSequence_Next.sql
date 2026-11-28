CREATE OR ALTER PROCEDURE fin.usp_JournalSequence_Next
    @CompanyId  int,
    @JournalId  int,
    @Year       smallint,
    @Number     int OUTPUT
AS
BEGIN
    -- Urmatorul numar de nota pe firma, jurnal si an (C2 3.5), fara goluri: se apeleaza in tranzactia validarii, cu blocare pe rand.
    SET NOCOUNT ON;

    UPDATE fin.JournalSequence WITH (UPDLOCK, HOLDLOCK)
    SET @Number = LastNumber = LastNumber + 1
    WHERE CompanyId = @CompanyId AND JournalId = @JournalId AND [Year] = @Year;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO fin.JournalSequence (CompanyId, JournalId, [Year], LastNumber) VALUES (@CompanyId, @JournalId, @Year, 1);
        SET @Number = 1;
    END;
END;
