CREATE OR ALTER PROCEDURE core.usp_PartnerBankAccount_Save
    @Id         int = NULL,
    @PartnerId  int,
    @Iban       varchar(34),
    @BankName   nvarchar(100),
    @Currency   char(3),
    @IsDefault  bit,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Contul bancar al partenerului (02.01, 3.2): IBAN validat in aplicatie (ISO 13616), o singura data la acelasi partener;
    -- un singur cont implicit pe partener si moneda, cel copiat pe documente (PartnerSnapshotDto).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
        THROW 50027, N'Partenerul nu există.', 1;

    IF EXISTS (SELECT 1 FROM core.PartnerBankAccount WITH (UPDLOCK, HOLDLOCK)
               WHERE PartnerId = @PartnerId AND Iban = @Iban AND (@Id IS NULL OR Id <> @Id))
        THROW 50023, N'IBAN-ul este deja înregistrat la acest partener.', 1;

    IF @IsDefault = 1
        UPDATE core.PartnerBankAccount
        SET IsDefault = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE PartnerId = @PartnerId AND Currency = @Currency AND IsDefault = 1 AND (@Id IS NULL OR Id <> @Id);

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.PartnerBankAccount (PartnerId, Iban, BankName, Currency, IsDefault, IsActive, CreatedBy)
        VALUES (@PartnerId, @Iban, @BankName, @Currency, @IsDefault, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.PartnerBankAccount
        SET Iban = @Iban, BankName = @BankName, Currency = @Currency, IsDefault = @IsDefault, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND PartnerId = @PartnerId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.PartnerBankAccount WHERE Id = @Id AND PartnerId = @PartnerId)
                THROW 50027, N'Contul bancar nu există la acest partener.', 1;
            THROW 50006, N'Contul bancar a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.PartnerBankAccount WHERE Id = @Id;
END;
