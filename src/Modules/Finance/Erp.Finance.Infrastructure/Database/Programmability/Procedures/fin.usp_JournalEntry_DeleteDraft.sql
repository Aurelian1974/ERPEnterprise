CREATE OR ALTER PROCEDURE fin.usp_JournalEntry_DeleteDraft
    @CompanyId  int,
    @Id         bigint
AS
BEGIN
    -- Sterge o ciorna (C2 3.8); o nota validata nu se sterge (51850), se storneaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.JournalEntry WHERE Id = @Id AND CompanyId = @CompanyId)
        THROW 51841, N'Nota nu există.', 1;
    IF EXISTS (SELECT 1 FROM fin.JournalEntry WHERE Id = @Id AND Status <> 1)
        THROW 51850, N'O notă validată nu se șterge; se stornează.', 1;
    DELETE FROM fin.JournalEntry WHERE Id = @Id;
END;
