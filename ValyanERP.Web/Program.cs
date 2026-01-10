using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using ValyanERP.Web.Components;
using ValyanERP.Web.Infrastructure.Data;
using ValyanERP.Web.Infrastructure.Identity;
using ValyanERP.Web.Features.Administrare.Persoane.Repositories;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Infrastructure.Sessions;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Syncfusion.Blazor;
using Syncfusion.Licensing;
using FluentValidation;
using FluentValidation.AspNetCore;
using Blazored.FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// ================================================================
// PERFORMANCE: Response Compression
// ================================================================
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.SmallestSize;
});

// ================================================================
// PERFORMANCE: Memory Cache
// ================================================================
// Note: Cache parameters are configured from DB (Cache.SizeLimit, Cache.CompactionPercentage)
// Default values used here, actual values loaded at runtime
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024; // Default: 1024 entries max (configurable via Cache.SizeLimit)
    options.CompactionPercentage = 0.25; // Default: Remove 25% when limit reached (configurable via Cache.CompactionPercentage)
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register Dapper context for all database operations
builder.Services.AddSingleton<DapperContext>();

// Register HttpContextAccessor (required for audit system)
builder.Services.AddHttpContextAccessor();

// Register Repositories
builder.Services.AddScoped<IPersoaneRepository, PersoaneRepository>();
builder.Services.AddScoped<ValyanERP.Web.Features.Infrastructure.SystemParameters.Repositories.ISystemParametersRepository, ValyanERP.Web.Features.Infrastructure.SystemParameters.Repositories.SystemParametersRepository>();

// Register Syncfusion Custom Adaptors
builder.Services.AddScoped<ValyanERP.Web.Features.Administrare.Persoane.PersoaneAdaptor>();
builder.Services.AddScoped<ValyanERP.Web.Features.Administrare.Utilizatori.UsersAdaptor>();

// Register Services (Business Logic Layer)
builder.Services.AddScoped<ValyanERP.Web.Features.Administrare.Persoane.Services.IPersoaneService, ValyanERP.Web.Features.Administrare.Persoane.Services.PersoaneService>();
builder.Services.AddScoped<ValyanERP.Web.Features.Infrastructure.SystemParameters.Services.ISystemParametersService, ValyanERP.Web.Features.Infrastructure.SystemParameters.Services.SystemParametersService>();

// Audit System
builder.Services.AddScoped<ValyanERP.Web.Features.Infrastructure.Audit.Repositories.IAuditRepository, ValyanERP.Web.Features.Infrastructure.Audit.Repositories.AuditRepository>();
builder.Services.AddScoped<ValyanERP.Web.Features.Infrastructure.Audit.Services.IAuditService, ValyanERP.Web.Features.Infrastructure.Audit.Services.AuditService>();
builder.Services.AddSingleton<ValyanERP.Web.Features.Infrastructure.Audit.Services.SensitiveDataMasker>();

// FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<ValyanERP.Web.Features.Administrare.Persoane.Validators.PersoanaValidator>();

// Blazored FluentValidation for Blazor EditForm integration
// (no explicit service registration required for Blazored.FluentValidation v2+)

// Sessions repository & service
builder.Services.AddScoped<ISessionsRepository, SessionsRepository>();
builder.Services.AddScoped<ISessionService, SessionService>();

// Add background cleanup job
builder.Services.AddHostedService<SessionCleanupService>();

// Add circuit handler to invalidate sessions on circuit close
builder.Services.AddSingleton<CircuitHandler, SessionCircuitHandler>();
// Note: SessionCircuitHandler resolves ISessionService from scope inside the handler
// Users repository (administration)
builder.Services.AddScoped<ValyanERP.Web.Features.Administrare.Utilizatori.Repositories.IUsersRepository, ValyanERP.Web.Features.Administrare.Utilizatori.Repositories.UsersRepository>();
// Users service (business logic)
builder.Services.AddScoped<ValyanERP.Web.Features.Administrare.Utilizatori.Services.IUsersService, ValyanERP.Web.Features.Administrare.Utilizatori.Services.UsersService>();

// Configure Identity with custom Dapper stores
builder.Services.AddScoped<IUserStore<ApplicationUser>, DapperUserStore>();
builder.Services.AddScoped<IRoleStore<ApplicationRole>, DapperRoleStore>();

// Use Argon2id for password hashing (most secure algorithm)
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2PasswordHasher<ApplicationUser>>();

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8; // Default: 8 (configurable via Validation.Password.MinLength)
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
.AddDefaultTokenProviders();

// ⚠️ SECURITY: Configure authentication cookie as SESSION COOKIE (non-persistent)
// Cookie expires when browser is closed (not after a time period)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true; // Protect against XSS
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Allow HTTP in development
    options.Cookie.SameSite = SameSiteMode.Lax; // CSRF protection
    options.Cookie.IsEssential = true; // Required for GDPR
    
    // ⚠️ CRITICAL: Force session cookie (expires when browser closes)
    options.Cookie.MaxAge = null; // NO MaxAge = session cookie
    options.Cookie.Expiration = null; // NO explicit expiration
    
    // ⚠️ Server-side session timeout (if browser stays open)
    options.ExpireTimeSpan = TimeSpan.FromHours(8); // Max 8 hours if browser stays open
    options.SlidingExpiration = true; // Reset expiration on activity
    
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Configure authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// 🔧 Register Syncfusion Blazor services
builder.Services.AddSyncfusionBlazor();

// Register Syncfusion license (set in appsettings.json or environment variable `Syncfusion:LicenseKey`)
var syncfusionLicenseKey = builder.Configuration["Syncfusion:LicenseKey"];
if (!string.IsNullOrWhiteSpace(syncfusionLicenseKey))
{
    SyncfusionLicenseProvider.RegisterLicense(syncfusionLicenseKey);
}
else
{
    Console.WriteLine("⚠️ Syncfusion license key not found. Add 'Syncfusion:LicenseKey' to appsettings.json or as an environment variable.");
}

var app = builder.Build();

// Seed admin user on startup (with Argon2id password hash)
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var persoaneRepository = scope.ServiceProvider.GetRequiredService<IPersoaneRepository>();
    var adminEmail = "admin@valyanerp.ro";
    var existingUser = await userManager.FindByEmailAsync(adminEmail);
    
    if (existingUser == null)
    {
        // First, create or get the admin Persoana (required for User)
        var adminPersoana = await persoaneRepository.GetByEmailAsync(adminEmail);
        
        if (adminPersoana == null)
        {
            // Create admin person
            adminPersoana = new Persoana
            {
                Id = Guid.NewGuid(),
                Prenume = "Administrator",
                Nume = "System",
                Email = adminEmail,
                Telefon = "0700000000",
                IsActive = true
            };
            await persoaneRepository.CreateAsync(adminPersoana);
            Console.WriteLine($"✅ Admin Persoana created: {adminPersoana.Id}");
        }
        
        if (adminPersoana != null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                PersoanaId = adminPersoana.Id,
                IsActive = true
            };
            
            // Password will be hashed with Argon2id via custom IPasswordHasher
            var result = await userManager.CreateAsync(adminUser, "Admin123!");
            if (result.Succeeded)
            {
                Console.WriteLine($"✅ Admin user created with Argon2id hash: {adminEmail} / Admin123!");
            }
            else
            {
                Console.WriteLine($"❌ Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    // Ensure admin password is set to Admin123! using UserManager (idempotent)
    existingUser = await userManager.FindByEmailAsync(adminEmail);
    if (existingUser != null)
    {
        var passwordOk = await userManager.CheckPasswordAsync(existingUser, "Admin123!");
        if (!passwordOk)
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(existingUser);
            var resetResult = await userManager.ResetPasswordAsync(existingUser, resetToken, "Admin123!");
            if (resetResult.Succeeded)
            {
                Console.WriteLine($"✅ Admin password reset to 'Admin123!' for {adminEmail}");
                // Re-fetch and verify
                existingUser = await userManager.FindByEmailAsync(adminEmail);
                if (existingUser != null)
                {
                    var verify = await userManager.CheckPasswordAsync(existingUser, "Admin123!");
                    Console.WriteLine($"🔐 Password verification after reset: {verify}");
                }
            }
            else
            {
                Console.WriteLine($"❌ Failed to reset admin password: {string.Join(", ", resetResult.Errors.Select(e => e.Description))}");
            }
        }
        else
        {
            Console.WriteLine($"ℹ️ Admin password already matches desired password for {adminEmail}");
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// ================================================================
// PERFORMANCE: Enable Response Compression
// ================================================================
app.UseResponseCompression();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();

// Validate server-side sessions on each request
app.UseMiddleware<ValyanERP.Web.Features.Infrastructure.Sessions.SessionValidationMiddleware>();

app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
