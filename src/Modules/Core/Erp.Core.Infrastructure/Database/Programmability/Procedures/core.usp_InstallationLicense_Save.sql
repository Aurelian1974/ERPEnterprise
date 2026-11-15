CREATE OR ALTER PROCEDURE core.usp_InstallationLicense_Save
    @LicenseId              nvarchar(50),
    @KeyId                  nvarchar(50),
    @CustomerName           nvarchar(200),
    @FiscalCodes            nvarchar(max),
    @Modules                nvarchar(max),
    @LimitCompanies         int,
    @LimitUsers             int,
    @LimitPayrollEmployees  int,
    @LimitPosTerminals      int,
    @IssuedDate             date,
    @ValidUntil             date,
    @Signature              varchar(200),
    @UserId                 int
AS
BEGIN
    -- Pasul I-12 (08, 8.9) si inlocuirea licentei din aplicatie. Licenta a fost verificata in C# (Erp.Licensing);
    -- procedura doar o pastreaza. Tranzactia e deschisa de apelant (SP-03). Rulata din nou cu aceeasi licenta: fara efect (R8).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ValidUntil < @IssuedDate
        THROW 50017, N'Licența are data de expirare înaintea datei de emitere.', 1;

    DECLARE @CurrentId int;

    SELECT @CurrentId = l.Id
    FROM core.InstallationLicense AS l WITH (UPDLOCK, HOLDLOCK)
    WHERE l.IsCurrent = 1 AND l.LicenseId = @LicenseId AND l.Signature = @Signature;

    IF @CurrentId IS NULL
    BEGIN
        UPDATE core.InstallationLicense
        SET IsCurrent = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE IsCurrent = 1;

        INSERT INTO core.InstallationLicense
            (LicenseId, KeyId, CustomerName, FiscalCodes, Modules, LimitCompanies, LimitUsers, LimitPayrollEmployees,
             LimitPosTerminals, IssuedDate, ValidUntil, Signature, IsCurrent, CreatedBy)
        VALUES
            (@LicenseId, @KeyId, @CustomerName, @FiscalCodes, @Modules, @LimitCompanies, @LimitUsers, @LimitPayrollEmployees,
             @LimitPosTerminals, @IssuedDate, @ValidUntil, @Signature, 1, @UserId);

        SET @CurrentId = SCOPE_IDENTITY();
    END;

    SELECT l.Id, l.LicenseId, l.ValidUntil
    FROM core.InstallationLicense AS l
    WHERE l.Id = @CurrentId;
END;
