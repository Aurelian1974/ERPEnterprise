using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using ValyanERP.Web.Components;
using ValyanERP.Web.Infrastructure.Data;
using ValyanERP.Web.Infrastructure.Identity;
using ValyanERP.Web.Features.Administrare.Persoane.Repositories;
using ValyanERP.Web.Features.Administrare.Persoane.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register Dapper context for all database operations
builder.Services.AddSingleton<DapperContext>();

// Register Repositories
builder.Services.AddScoped<IPersoaneRepository, PersoaneRepository>();

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
    options.Password.RequiredLength = 8; // Increased for better security with Argon2
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
.AddDefaultTokenProviders();

// Configure authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

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
            var createPersoanaDto = new CreatePersoanaDto
            {
                Prenume = "Administrator",
                Nume = "System",
                Email = adminEmail,
                Telefon = "0700000000"
            };
            var persoanaId = await persoaneRepository.CreateAsync(createPersoanaDto);
            adminPersoana = await persoaneRepository.GetByIdAsync(persoanaId);
            Console.WriteLine($"✅ Admin Persoana created: {persoanaId}");
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
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
