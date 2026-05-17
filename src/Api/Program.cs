using Administration.Infrastructure;
using Finance.Infrastructure;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Shared.Infrastructure.Behaviors;
using Shared.Infrastructure.Extensions;
using System.Reflection;
using System.Text;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // ── Serilog ─────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console()
        .WriteTo.Seq(ctx.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341"));

    // ── Controllers ─────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ── OpenAPI ─────────────────────────────────────────────────────────────
    builder.Services.AddOpenApi();

    // ── Authentication / JWT ─────────────────────────────────────────────────
    string jwtKey = builder.Configuration["Jwt:SecretKey"]
        ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };
        });

    builder.Services.AddAuthorization();

    // ── Shared Infrastructure ────────────────────────────────────────────────
    builder.Services.AddSharedInfrastructure(builder.Configuration);

    // ── MediatR (all module assemblies) ─────────────────────────────────────
    Assembly[] applicationAssemblies =
    [
        typeof(Finance.Application.Features.Invoices.Create.CreateInvoiceCommand).Assembly,
        typeof(Administration.Application.Features.PartnerTypes.GetAll.GetAllPartnerTypesQuery).Assembly,
    ];

    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblies(applicationAssemblies);
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    });

    // ── FluentValidation ────────────────────────────────────────────────────
    builder.Services.AddValidatorsFromAssemblies(applicationAssemblies);

    // ── Module registrations ─────────────────────────────────────────────────
    new FinanceModule().Install(builder.Services, builder.Configuration);
    new AdministrationModule().Install(builder.Services, builder.Configuration);

    // ── Hangfire ─────────────────────────────────────────────────────────────
    string connStr = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(connStr));

    builder.Services.AddHangfireServer();

    // ── Health Checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddSqlServer(connStr, name: "sql-server", tags: ["db"]);

    // ── Feature Management ───────────────────────────────────────────────────
    builder.Services.AddFeatureManagement();

    // ─────────────────────────────────────────────────────────────────────────
    WebApplication app = builder.Build();
    // ─────────────────────────────────────────────────────────────────────────

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "ERP API";
            options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = h => h.Tags.Contains("db")
    });
    app.MapHealthChecks("/health/full");

    // ── Run DB migrations ────────────────────────────────────────────────────
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    FinanceModule.RunMigrations(connStr, logger);
    AdministrationModule.RunMigrations(connStr, logger);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
