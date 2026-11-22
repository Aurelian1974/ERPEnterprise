CREATE OR ALTER PROCEDURE fin.usp_CashDocument_Delete
    @CashDocumentId    int,
    @CompanyId         int,
    @RowVersion        binary(8),
    @RequireAssignment bit,
    @UserId            int
AS
BEGIN
    -- Stergerea unei ciorne de casa (02.02, F1 3.3): doar ciornele, care nu au numar si nu au miscat soldul; liniile se sterg in cascada
    -- (SQL-15). Un document validat se storneaza (51003).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @CurrentVersion binary(8), @DocCompanyId int, @AccountId int;
    SELECT @Status = Status, @CurrentVersion = RowVersion, @DocCompanyId = CompanyId, @AccountId = TreasuryAccountId
    FROM fin.CashDocument WITH (UPDLOCK, ROWLOCK) WHERE Id = @CashDocumentId;

    IF @Status IS NULL OR @DocCompanyId <> @CompanyId
        THROW 51001, N'Documentul de casă nu există.', 1;
    IF @RequireAssignment = 1 AND NOT EXISTS (SELECT 1 FROM fin.TreasuryAccountUser WHERE TreasuryAccountId = @AccountId AND UserId = @UserId)
        THROW 51014, N'Nu sunteți atribuit casei documentului.', 1;
    IF @CurrentVersion <> @RowVersion
        THROW 51002, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 51003, N'Doar o ciornă se poate șterge; un document validat se stornează.', 1;

    DELETE FROM fin.CashDocument WHERE Id = @CashDocumentId;
    SELECT @CashDocumentId;
END;
