# Update sp_Partners_Update to include Ownership parameters
Write-Host "Updating sp_Partners_Update stored procedure..." -ForegroundColor Cyan

$sqlScript = @"
USE [ValyanERP];
GO

IF OBJECT_ID('dbo.sp_Partners_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_Update;
GO

CREATE PROCEDURE dbo.sp_Partners_Update
    @Id UNIQUEIDENTIFIER,
    @Categoria TINYINT,
    @TipEntitate NVARCHAR(20),
    @RolPartener INT = 0,
    @Denumire NVARCHAR(200) = NULL,
    @DenumireScurta NVARCHAR(50) = NULL,
    @Nume NVARCHAR(100) = NULL,
    @Prenume NVARCHAR(100) = NULL,
    @CNP NVARCHAR(13) = NULL,
    @CUI NVARCHAR(20) = NULL,
    @CIF NVARCHAR(20) = NULL,
    @VATID NVARCHAR(20) = NULL,
    @CodFiscalStrain NVARCHAR(50) = NULL,
    @RegCom NVARCHAR(50) = NULL,
    @NrAutorizatie NVARCHAR(50) = NULL,
    @Pasaport NVARCHAR(50) = NULL,
    @DataInregistrare DATE = NULL,
    @DataRadiere DATE = NULL,
    @TaraOrigine NVARCHAR(3) = 'RO',
    @CAENPrincipal NVARCHAR(10) = NULL,
    @CapitalSocial DECIMAL(18,2) = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @TelefonSecundar NVARCHAR(20) = NULL,
    @Website NVARCHAR(200) = NULL,
    @EstePlătitorTVA BIT = 0,
    @DataInregistrareTVA DATE = NULL,
    @StatusSplitTVA BIT = 0,
    @PartnerStatus VARCHAR(20) = 'Activ',
    @EsteActiv BIT = 1,
    @BlocatFacturare BIT = 0,
    @BlocatLivrare BIT = 0,
    @MotivBlocare NVARCHAR(500) = NULL,
    @LimitaCredit DECIMAL(18,2) = NULL,
    @TermenPlataDef INT = 30,
    @CategorieComercială VARCHAR(20) = NULL,
    @IdentificatorTemp VARCHAR(50) = NULL,
    @MotivLipsaIdentificator NVARCHAR(200) = NULL,
    @Observatii NVARCHAR(2000) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL,
    -- Ownership parameters (ADDED)
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Update SAF-T codes based on role
    DECLARE @TipSAFT VARCHAR(10) = CASE 
        WHEN (@RolPartener & 3) = 3 THEN 'CS'
        WHEN (@RolPartener & 2) = 2 THEN 'C'
        WHEN (@RolPartener & 1) = 1 THEN 'S'
        ELSE 'O'
    END;
    
    UPDATE Partners
    SET 
        Categoria = @Categoria,
        TipEntitate = @TipEntitate,
        RolPartener = @RolPartener,
        Denumire = @Denumire,
        DenumireScurta = @DenumireScurta,
        Nume = @Nume,
        Prenume = @Prenume,
        CNP = @CNP,
        CUI = @CUI,
        CIF = @CIF,
        VATID = @VATID,
        CodFiscalStrain = @CodFiscalStrain,
        RegCom = @RegCom,
        NrAutorizatie = @NrAutorizatie,
        Pasaport = @Pasaport,
        DataInregistrare = @DataInregistrare,
        DataRadiere = @DataRadiere,
        TaraOrigine = @TaraOrigine,
        CAENPrincipal = @CAENPrincipal,
        CapitalSocial = @CapitalSocial,
        Email = @Email,
        Telefon = @Telefon,
        TelefonSecundar = @TelefonSecundar,
        Website = @Website,
        EstePlătitorTVA = @EstePlătitorTVA,
        DataInregistrareTVA = @DataInregistrareTVA,
        StatusSplitTVA = @StatusSplitTVA,
        PartnerStatus = @PartnerStatus,
        EsteActiv = @EsteActiv,
        BlocatFacturare = @BlocatFacturare,
        BlocatLivrare = @BlocatLivrare,
        MotivBlocare = @MotivBlocare,
        LimitaCredit = @LimitaCredit,
        TermenPlataDef = @TermenPlataDef,
        CategorieComercială = @CategorieComercială,
        TipPartenerSAFT = @TipSAFT,
        SupplierID = CASE WHEN (@RolPartener & 1) = 1 THEN CodPartenerSAFT ELSE NULL END,
        CustomerID = CASE WHEN (@RolPartener & 2) = 2 THEN CodPartenerSAFT ELSE NULL END,
        IdentificatorTemp = @IdentificatorTemp,
        MotivLipsaIdentificator = @MotivLipsaIdentificator,
        Observatii = @Observatii,
        -- Ownership columns (ADDED)
        OwnerCompanyId = @OwnerCompanyId,
        OwnerWorkPlaceId = @OwnerWorkPlaceId,
        OwnerLocationId = @OwnerLocationId,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT 'sp_Partners_Update updated with Ownership parameters!';
GO
"@

# Get connection string from appsettings.json
$appSettingsPath = Join-Path $PSScriptRoot "..\ValyanERP.Web\appsettings.Development.json"
$appSettings = Get-Content $appSettingsPath | ConvertFrom-Json
$connectionString = $appSettings.ConnectionStrings.DefaultConnection

Write-Host "Connection String: $connectionString" -ForegroundColor Yellow

# Execute SQL
try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    $command = $connection.CreateCommand()
    $command.CommandText = $sqlScript
    $command.ExecuteNonQuery() | Out-Null
    
    Write-Host "✅ sp_Partners_Update updated successfully!" -ForegroundColor Green
    
    $connection.Close()
}
catch {
    Write-Host "❌ Error updating stored procedure: $_" -ForegroundColor Red
    exit 1
}
