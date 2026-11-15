using Erp.Auth;
using Erp.Configuration;
using Erp.Core.Api;
using Erp.Core.Application;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using Erp.Hosting;
using Erp.Licensing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

// Erp.Host (8.7, 8.10, 8.14, 10.4, 10.5). Configurarea vine din ERP_INSTANCE_DIR (web.config); interfata React se adauga ulterior.
// In mediul Development, fara ERP_INSTANCE_DIR, aceleasi chei (Erp:*, Database:*) vin din appsettings.Development.json / user-secrets.
var builder = WebApplication.CreateBuilder(args);
var development = builder.Environment.IsDevelopment();
var instanceDirectory = Environment.GetEnvironmentVariable(InstanceDataPaths.InstanceDirectoryVariable);
if (instanceDirectory is not null)
{
    builder.Configuration.AddErpInstallation(instanceDirectory);
}
else if (!development)
{
    throw new InvalidOperationException($"Variabila {InstanceDataPaths.InstanceDirectoryVariable} lipsește (web.config).");
}

var build = ErpBuildInfo.From(typeof(Program).Assembly);
var connectionString = InstallationConfiguration.ConnectionString(builder.Configuration, "Host");
SqlConnection Connect() => new(connectionString);
var schemaReader = new SchemaVersionReader(Connect);
var licenseVerifier = new LicenseVerifier(LicenseKeys.LoadEmbedded(), TimeProvider.System);
var licensePath = builder.Configuration["Erp:LicensePath"]
    ?? (instanceDirectory is null ? string.Empty : new InstanceDataPaths(instanceDirectory).License);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(build);
builder.Services.AddSingleton<ISchemaVersionSource>(schemaReader);
builder.Services.AddSingleton(sp => new SchemaVersionGuard(sp.GetRequiredService<ISchemaVersionSource>(), build,
    sp.GetRequiredService<ILogger<SchemaVersionGuard>>(), TimeProvider.System, allowUnversionedBuild: development));
builder.Services.AddHostedService(sp => sp.GetRequiredService<SchemaVersionGuard>());
builder.Services.AddSingleton(new MaintenanceFlag(builder.Configuration["Erp:MaintenanceFlagPath"], TimeProvider.System));
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

// Nucleul (02.01): pipeline-ul comun al comenzilor, contextul cererii, ProblemDetails. Modulele se adauga aici, dupa licenta.
builder.Services.AddErpMediator();
builder.Services.AddCoreInfrastructure(connectionString, new LicenseOptions(licensePath, development));
builder.Services.AddErpApi();

// Autentificarea (08, 8.7): Identity cu cookie, paginile /account/*. Cheile Data Protection (cookie, antiforgery, cheile TOTP)
// stau in DataRoot\<Instanta>\keys, criptate DPAPI (8.10); in Development, in profilul utilizatorului.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName($"ERP-{builder.Configuration["Erp:Instance"]}");
if (builder.Configuration["Erp:DataProtectionKeysPath"] is { Length: > 0 } keysPath && OperatingSystem.IsWindows())
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath)).ProtectKeysWithDpapi(protectToLocalMachine: true);
}

builder.Services.AddErpAuth(builder.Configuration);

var app = builder.Build();

// Mentenanta e primul middleware din pipeline (10.4).
app.UseMiddleware<MaintenanceMiddleware>();
app.UseHsts();
app.UseHttpsRedirection();
app.UseMiddleware<SchemaGuardMiddleware>();
app.UseAuthentication();
app.UseErpApi();
app.UseErpAccountGate();

app.MapErpHealth(build, () =>
[
    new DatabaseCheck(Connect),
    new SchemaCheck(schemaReader, build, allowUnversionedBuild: development),
    new LicenseCheck(licensePath, licenseVerifier),
    new WorkerCheck(Connect),
]);

app.MapCoreApi();
app.MapErpAuth();

// Pagina provizorie pana la interfata React; health-check-ul installer-ului cere 200 HTML pe / si /account/login (8.14).
app.MapGet("/", () => Results.Content(PlaceholderPage.Render("ERP"), "text/html; charset=utf-8"));

app.Run();

internal static class PlaceholderPage
{
    public static string Render(string title) => $"""
        <!DOCTYPE html>
        <html lang="ro">
        <head><meta charset="utf-8"><title>{title} · ERP</title></head>
        <body><main><h1>{title}</h1><p>Aplicația ERP rulează. Interfața va fi disponibilă într-o versiune următoare.</p></main></body>
        </html>
        """;
}
