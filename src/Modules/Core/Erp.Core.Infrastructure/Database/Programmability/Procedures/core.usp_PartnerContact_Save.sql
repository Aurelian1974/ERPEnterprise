CREATE OR ALTER PROCEDURE core.usp_PartnerContact_Save
    @Id         int = NULL,
    @PartnerId  int,
    @Name       nvarchar(150),
    @Role       nvarchar(100) = NULL,
    @Phone      nvarchar(30) = NULL,
    @Email      nvarchar(254) = NULL,
    @Notes      nvarchar(500) = NULL,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Persoana de contact a partenerului (02.01, 3.2): cel putin un telefon sau un e-mail.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
        THROW 50027, N'Partenerul nu există.', 1;

    IF @Phone IS NULL AND @Email IS NULL
        THROW 50021, N'Dați cel puțin un telefon sau un e-mail.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.PartnerContact (PartnerId, Name, Role, Phone, Email, Notes, IsActive, CreatedBy)
        VALUES (@PartnerId, @Name, @Role, @Phone, @Email, @Notes, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.PartnerContact
        SET Name = @Name, Role = @Role, Phone = @Phone, Email = @Email, Notes = @Notes, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND PartnerId = @PartnerId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.PartnerContact WHERE Id = @Id AND PartnerId = @PartnerId)
                THROW 50027, N'Persoana de contact nu există la acest partener.', 1;
            THROW 50006, N'Persoana de contact a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.PartnerContact WHERE Id = @Id;
END;
