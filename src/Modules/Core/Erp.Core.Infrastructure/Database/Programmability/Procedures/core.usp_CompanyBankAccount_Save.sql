CREATE OR ALTER PROCEDURE core.usp_CompanyBankAccount_Save
    @Id         int = NULL,
    @CompanyId  int,
    @Iban       varchar(34),
    @BankName   nvarchar(100),
    @Currency   char(3),
    @IsDefault  bit,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Contul bancar al firmei (02.01, 3.1): IBAN unic (validat in aplicatie, ISO 13616); un singur cont implicit pe firma
    -- si moneda, cel tiparit pe facturi: alegerea unui cont implicit il inlocuieste pe cel vechi.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma nu există.', 1;

    -- Moneda din nomenclatorul de valute (21.11.2026): activa sau, la un cont existent, cea pe care o avea deja.
    IF NOT EXISTS (SELECT 1 FROM core.Currency WHERE Code = @Currency
                   AND (IsActive = 1 OR EXISTS (SELECT 1 FROM core.CompanyBankAccount WHERE Id = @Id AND Currency = @Currency)))
        THROW 50021, N'Moneda contului nu există sau nu e activă în nomenclatorul de valute.', 1;

    IF EXISTS (SELECT 1 FROM core.CompanyBankAccount WITH (UPDLOCK, HOLDLOCK) WHERE Iban = @Iban AND (@Id IS NULL OR Id <> @Id))
        THROW 50023, N'IBAN-ul este deja înregistrat.', 1;

    IF @IsDefault = 1
        UPDATE core.CompanyBankAccount
        SET IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE CompanyId = @CompanyId AND Currency = @Currency AND IsDefault = 1 AND (@Id IS NULL OR Id <> @Id);

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.CompanyBankAccount (CompanyId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
        VALUES (@CompanyId, @Iban, @BankName, @Currency, @IsDefault, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.CompanyBankAccount
        SET Iban = @Iban, BankName = @BankName, Currency = @Currency, IsDefault = @IsDefault, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.CompanyBankAccount WHERE Id = @Id AND CompanyId = @CompanyId)
                THROW 50027, N'Contul bancar nu există în această firmă.', 1;
            THROW 50006, N'Contul bancar a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.CompanyBankAccount WHERE Id = @Id;
END;
