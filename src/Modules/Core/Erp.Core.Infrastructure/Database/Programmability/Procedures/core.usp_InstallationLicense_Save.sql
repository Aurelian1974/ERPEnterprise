CREATE OR ALTER PROCEDURE core.usp_InstallationLicense_Save
    @LicenseFile            varbinary(max),
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
    @UpdatesUntil           date,
    @UserId                 int
AS
BEGIN
    -- Pasul I-13 al installer-ului si incarcarea licentei din aplicatie (08, 4.1). Licenta a fost verificata in C#
    -- (Erp.Licensing, 08, 4.2); procedura pastreaza fisierul si proiectia lui. Tranzactia e deschisa de apelant (SP-03).
    -- Aceeasi licenta (acelasi numar si aceiasi octeti) salvata din nou: fara efect (R10).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ValidUntil < @IssuedDate OR @UpdatesUntil < @IssuedDate
        THROW 50017, N'Licența are o dată de valabilitate înaintea datei de emitere.', 1;

    DECLARE @CurrentId int;

    SELECT @CurrentId = l.Id
    FROM core.InstallationLicense AS l WITH (UPDLOCK, HOLDLOCK)
    WHERE l.IsCurrent = 1 AND l.LicenseId = @LicenseId AND l.LicenseFile = @LicenseFile;

    IF @CurrentId IS NULL
    BEGIN
        UPDATE core.InstallationLicense
        SET IsCurrent = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE IsCurrent = 1;

        INSERT INTO core.InstallationLicense
            (LicenseFile, LicenseId, KeyId, CustomerName, FiscalCodes, Modules, LimitCompanies, LimitUsers, LimitPayrollEmployees,
             LimitPosTerminals, IssuedDate, ValidUntil, UpdatesUntil, IsCurrent, CreatedBy)
        VALUES
            (@LicenseFile, @LicenseId, @KeyId, @CustomerName, @FiscalCodes, @Modules, @LimitCompanies, @LimitUsers, @LimitPayrollEmployees,
             @LimitPosTerminals, @IssuedDate, @ValidUntil, @UpdatesUntil, 1, @UserId);

        SET @CurrentId = SCOPE_IDENTITY();
    END;

    SELECT l.Id, l.LicenseId, l.ValidUntil
    FROM core.InstallationLicense AS l
    WHERE l.Id = @CurrentId;
END;
