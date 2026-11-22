CREATE OR ALTER PROCEDURE fin.usp_TreasuryAccount_SetBankFormat
    @CompanyId           int,
    @TreasuryAccountId   int,
    @BankFormatProfileId int = NULL,
    @ImportFolder        nvarchar(260) = NULL,
    @UserId              int
AS
BEGIN
    -- Profilul bancar si folderul de import al unui cont (02.02, F2 3.1): doar conturile de tip Banca, Card sau Depozit (51021), doar
    -- profile active.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @AccountCompanyId int, @Kind tinyint;
    SELECT @AccountCompanyId = CompanyId, @Kind = Kind FROM fin.TreasuryAccount WITH (UPDLOCK, ROWLOCK) WHERE Id = @TreasuryAccountId;
    IF @AccountCompanyId IS NULL OR @AccountCompanyId <> @CompanyId
        THROW 51023, N'Contul de trezorerie nu există în firma curentă.', 1;
    IF @Kind NOT IN (1, 3, 5)
        THROW 51021, N'Profilul bancar se alege doar pentru conturi de tip bancă, card sau depozit.', 1;
    IF @BankFormatProfileId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fin.BankFormatProfile WHERE Id = @BankFormatProfileId AND IsActive = 1)
        THROW 51021, N'Profilul bancar nu există sau este inactiv.', 1;

    UPDATE fin.TreasuryAccount
    SET BankFormatProfileId = @BankFormatProfileId, ImportFolder = @ImportFolder, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @TreasuryAccountId;
END;
