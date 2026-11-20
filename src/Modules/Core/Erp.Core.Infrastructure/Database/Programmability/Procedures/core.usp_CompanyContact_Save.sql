CREATE OR ALTER PROCEDURE core.usp_CompanyContact_Save
    @Id         int = NULL,
    @CompanyId  int,
    @Name       nvarchar(150),
    @Role       nvarchar(100) = NULL,
    @Phone      nvarchar(30) = NULL,
    @Email      nvarchar(254) = NULL,
    @Notes      nvarchar(500) = NULL,
    @BranchId   int = NULL,
    @LocationId int = NULL,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Persoana de contact a firmei (02.01, 3.1): cel putin un telefon sau un e-mail.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma nu există.', 1;

    IF @Phone IS NULL AND @Email IS NULL
        THROW 50021, N'Dați cel puțin un telefon sau un e-mail.', 1;

    IF @BranchId IS NOT NULL AND @LocationId IS NOT NULL
        THROW 50021, N'Persoana de contact aparține fie unui punct de lucru, fie unei locații.', 1;

    IF (@BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @BranchId AND CompanyId = @CompanyId))
       OR (@LocationId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Location WHERE Id = @LocationId AND CompanyId = @CompanyId))
        THROW 50021, N'Punctul de lucru sau locația nu aparține firmei.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.CompanyContact (CompanyId, BranchId, LocationId, Name, Role, Phone, Email, Notes, IsActive, CreatedBy)
        VALUES (@CompanyId, @BranchId, @LocationId, @Name, @Role, @Phone, @Email, @Notes, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.CompanyContact
        SET Name = @Name, Role = @Role, Phone = @Phone, Email = @Email, Notes = @Notes, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND CompanyId = @CompanyId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.CompanyContact WHERE Id = @Id AND CompanyId = @CompanyId)
                THROW 50027, N'Persoana de contact nu există în această firmă.', 1;
            THROW 50006, N'Persoana de contact a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.CompanyContact WHERE Id = @Id;
END;
