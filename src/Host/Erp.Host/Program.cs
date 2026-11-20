using Erp.Auth;
using Erp.Auth.Identity;
using Erp.Configuration;
using Erp.Core.Api;
using Erp.Core.Application;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using Erp.Hosting;
using Erp.Core.Contracts;
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
// Licenta (08, 4.1): din baza, reincarcata de LicenseRefresher la pornire, la 5 minute si dupa fiecare incarcare.
builder.Services.AddCoreInfrastructure(connectionString, new LicenseOptions(development));
builder.Services.AddErpApi();

// Autentificarea (08, sectiunea 8): Identity cu cookie, paginile /account/*. Cheile Data Protection (cookie, antiforgery, cheile
// TOTP, secretele din core.Setting) stau in baza, protejate cu certificatul instalarii (08, 5.12); in Development, in profil.
builder.Services.AddErpDataProtection(builder.Configuration, Connect, development);

builder.Services.AddErpAuth(builder.Configuration);
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "ERP API";
    document.Info.Version = build.Version;
    return Task.CompletedTask;
}));

var app = builder.Build();

// Mentenanta e primul middleware din pipeline (10.4).
app.UseMiddleware<MaintenanceMiddleware>();
app.UseHsts();
app.UseHttpsRedirection();
app.UseMiddleware<SchemaGuardMiddleware>();
// Fisierele interfetei (assets/ cu hash in nume) nu cer autentificare; se comprima doar ele (fara date ale utilizatorului).
app.UseWhen(context => context.Request.Path.StartsWithSegments("/assets"), assets => assets.UseResponseCompression());
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
        context.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
});
app.UseAuthentication();
app.UseErpApi();
app.UseErpAccountGate();

app.MapErpHealth(build, () =>
[
    new DatabaseCheck(Connect),
    new SchemaCheck(schemaReader, build, allowUnversionedBuild: development),
    new LicenseCheck(() => app.Services.GetRequiredService<ILicenseInfo>() is { HasLicense: true, State: not LicenseState.ReadOnly }),
    new DataProtectionCheck(app.Services.GetRequiredService<IDataProtectionProvider>()),
    new WorkerCheck(Connect),
]);

app.MapCoreApi();
app.MapErpAuth();
if (development)
{
    // Documentul OpenAPI din care se genereaza clientul interfetei (src/Web/erp-web, npm run api); nu se expune in productie.
    app.MapOpenApi();
}

// Interfata React (src/Web/erp-web) publicata in wwwroot. Health-check-ul installer-ului cere 200 HTML pe / si /account/login,
// fara redirectari (8.14): index.html raspunde 200, iar aplicatia trimite browserul fara sesiune la /account/login.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound));
var indexFile = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
if (indexFile.Exists)
{
    app.MapFallbackToFile("index.html", new StaticFileOptions { OnPrepareResponse = NoCache });
}
else
{
    // Fara build-ul interfetei (dezvoltare inainte de npm run build): pagina provizorie.
    app.MapGet("/", (HttpContext http, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
        Results.Content(http.User.Identity?.IsAuthenticated == true
            ? PlaceholderPage.Home(http.User.FindFirst(ErpClaims.DisplayName)?.Value ?? http.User.Identity.Name ?? string.Empty,
                antiforgery.GetAndStoreTokens(http).RequestToken!)
            : PlaceholderPage.RedirectToLogin(), "text/html; charset=utf-8"));
}

app.Run();

// index.html se reincarca la fiecare deschidere (versiunea noua dupa upgrade); fisierele din assets/ au hash in nume.
static void NoCache(Microsoft.AspNetCore.StaticFiles.StaticFileResponseContext context) =>
    context.Context.Response.Headers.CacheControl = "no-cache, no-store";

internal static class PlaceholderPage
{
    public static string RedirectToLogin() => """
        <!DOCTYPE html>
        <html lang="ro">
        <head><meta charset="utf-8"><meta http-equiv="refresh" content="0;url=/account/login"><title>ERP</title>
        <meta name="color-scheme" content="light">
        <style>body{font-family:"Segoe UI",system-ui,sans-serif;margin:0;background:#dde8f7;color:#14233b}a{color:#2459c9}</style>
        </head>
        <body><main><h1>ERP</h1><p><a href="/account/login">Autentificare</a></p></main></body>
        </html>
        """;

    public static string Home(string displayName, string antiforgeryToken)
    {
        var name = System.Net.WebUtility.HtmlEncode(displayName);
        var token = System.Net.WebUtility.HtmlEncode(antiforgeryToken);
        return $$"""
            <!DOCTYPE html>
            <html lang="ro">
            <head><meta charset="utf-8"><title>ERP</title>
            <meta name="color-scheme" content="light">
            <style>body{font-family:"Segoe UI",system-ui,sans-serif;margin:0;background:#dde8f7;color:#14233b}main{max-width:640px;margin:10vh auto;background:#eaf1fb;border:1px solid #bcccea;border-radius:12px;padding:32px}a{color:#2459c9}a,button{font:inherit}nav{display:flex;gap:16px;align-items:center;margin-top:24px}button{border:1px solid #566fa8;background:#f4f8fd;color:#14233b;border-radius:8px;padding:8px 16px;cursor:pointer}</style>
            </head>
            <body><main>
            <h1>Bun venit, {{name}}</h1>
            <p>Sunteți autentificat. Interfața aplicației ERP va fi disponibilă într-o versiune următoare.</p>
            <nav>
            <a href="/account/password">Schimbă parola</a>
            <a href="/account/2fa">Autentificare în doi pași</a>
            <form method="post" action="/account/logout"><input type="hidden" name="__RequestVerificationToken" value="{{token}}"><button type="submit">Ieșire</button></form>
            </nav>
            </main></body>
            </html>
            """;
    }
}
