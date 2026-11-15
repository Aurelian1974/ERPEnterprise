using System.Globalization;
using System.Security.Claims;
using Erp.Auth.Data;
using Erp.Auth.Identity;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Erp.Auth;

public static class AuthRegistration
{
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";
    public const string AntiforgeryRequestCookie = "XSRF-TOKEN";
    public const string ApplicationCookie = "__Host-erp-auth";

    /// <summary>ASP.NET Core Identity cu store-uri Dapper, cookie-ul de autentificare si paginile /account/* (08, 8.7).</summary>
    public static IServiceCollection AddErpAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.PostConfigure<AuthOptions>(auth =>
        {
            if (auth.InitialAdminFilePath is null && configuration["Erp:DataRoot"] is { Length: > 0 } dataRoot && configuration["Erp:Instance"] is { Length: > 0 } instance)
            {
                auth.InitialAdminFilePath = Path.Combine(dataRoot, instance, "install", "initial-admin.txt");
            }
        });
        services.AddScoped<Web.AccountFlow>();
        services.AddLocalization();
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuthSessionStore>();
        services.AddScoped<UserAccountStore>();
        services.AddScoped<ErpIdentityErrorDescriber>();

        services.AddIdentityCore<UserAccount>(identity =>
            {
                // 02.01, 11.2: minimum 12 caractere, litera mare si mica, cifra; restul regulilor in ErpPasswordValidator.
                identity.Password.RequiredLength = 12;
                identity.Password.RequireDigit = true;
                identity.Password.RequireLowercase = true;
                identity.Password.RequireUppercase = true;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Password.RequiredUniqueChars = 1;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = options.MaxFailedAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(options.LockoutMinutes);
                identity.User.RequireUniqueEmail = false;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.ClaimsIdentity.UserIdClaimType = ClaimTypes.NameIdentifier;
            })
            .AddUserStore<UserAccountStore>()
            .AddErrorDescriber<ErpIdentityErrorDescriber>()
            .AddClaimsPrincipalFactory<ErpClaimsPrincipalFactory>()
            .AddSignInManager<ErpSignInManager>()
            .AddPasswordValidator<ErpPasswordValidator>()
            // Al doilea factor este numai aplicatia de autentificare (TOTP); fara coduri prin e-mail sau telefon.
            .AddTokenProvider<DataProtectorTokenProvider<UserAccount>>(TokenOptions.DefaultProvider)
            .AddTokenProvider<AuthenticatorTokenProvider<UserAccount>>(TokenOptions.DefaultAuthenticatorProvider);
        services.Replace(ServiceDescriptor.Scoped<ISecurityStampValidator, ErpSecurityStampValidator>());
        services.Configure<SecurityStampValidatorOptions>(validator =>
        {
            validator.ValidationInterval = TimeSpan.FromMinutes(1);
            // La reemiterea cookie-ului se pastreaza sesiunea si momentul autentificarii.
            validator.OnRefreshingPrincipal = context =>
            {
                if (context.NewPrincipal?.Identity is ClaimsIdentity identity && context.CurrentPrincipal is { } current)
                {
                    foreach (var type in new[] { ErpClaims.SessionId, ErpClaims.AuthTime })
                    {
                        if (current.FindFirst(type) is { } claim && !identity.HasClaim(c => c.Type == type))
                        {
                            identity.AddClaim(new Claim(type, claim.Value, claim.ValueType));
                        }
                    }
                }

                return Task.CompletedTask;
            };
        });

        services.AddAuthentication(authentication =>
            {
                authentication.DefaultScheme = IdentityConstants.ApplicationScheme;
                authentication.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies(cookies =>
            {
                cookies.ApplicationCookie!.Configure(cookie => ConfigureApplicationCookie(cookie, options));
                cookies.TwoFactorUserIdCookie!.Configure(cookie => Harden(cookie, "__Host-erp-2fa"));
                cookies.TwoFactorRememberMeCookie!.Configure(cookie => Harden(cookie, "__Host-erp-2fa-remember"));
                cookies.ExternalCookie!.Configure(cookie => Harden(cookie, "__Host-erp-external"));
            });
        services.AddAuthorization();

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = AntiforgeryHeader;
            antiforgery.Cookie.Name = "__Host-erp-antiforgery";
            antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;
        });
        // Paginile /account/* sunt in Erp.Auth; le inregistram explicit, independent de assembly-ul de pornire (Host sau teste).
        services.AddRazorPages().ConfigureApplicationPartManager(manager =>
        {
            var assembly = typeof(AccountText).Assembly;
            if (!manager.ApplicationParts.OfType<Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart>().Any(part => part.Assembly == assembly))
            {
                foreach (var part in Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPartFactory.GetApplicationPartFactory(assembly).GetApplicationParts(assembly))
                {
                    manager.ApplicationParts.Add(part);
                }
            }
        });
        // Diacriticele romanesti apar direct in HTML, nu ca entitati (&#x103;); caracterele speciale HTML raman codificate.
        services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(encoder =>
            encoder.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
        return services;
    }

    /// <summary>
    /// Dupa UseAuthentication si UseErpApi (contextul cererii si ProblemDetails): pasii obligatorii ai contului,
    /// verificarea antiforgery pe API si autorizarea.
    /// </summary>
    public static IApplicationBuilder UseErpAccountGate(this IApplicationBuilder app)
    {
        app.UseMiddleware<AccountGateMiddleware>();
        app.UseMiddleware<ApiAntiforgeryMiddleware>();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Paginile /account/* si GET /api/antiforgery (08, 8.7, protectia CSRF).</summary>
    public static IEndpointRouteBuilder MapErpAuth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        endpoints.MapGet("/api/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(http);
                http.Response.Cookies.Append(AntiforgeryRequestCookie, tokens.RequestToken!, new CookieOptions
                {
                    HttpOnly = false,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                });
                return Results.NoContent();
            })
            .WithTags("Auth")
            .AllowAnonymous();
        return endpoints;
    }

    private static void ConfigureApplicationCookie(CookieAuthenticationOptions cookie, AuthOptions options)
    {
        Harden(cookie, ApplicationCookie);
        cookie.ExpireTimeSpan = TimeSpan.FromMinutes(options.IdleTimeoutMinutes);
        cookie.SlidingExpiration = true;
        cookie.LoginPath = "/account/login";
        cookie.LogoutPath = "/account/logout";
        cookie.AccessDeniedPath = "/account/login";
        cookie.ReturnUrlParameter = "returnUrl";
        // Pentru /api: 401 si 403 fara redirect (interfata React trimite la /account/login).
        cookie.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes.Status401Unauthorized);
        cookie.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes.Status403Forbidden);
        cookie.Events.OnValidatePrincipal = async context =>
        {
            // Durata maxima a sesiunii (8.7), indiferent de activitate.
            if (long.TryParse(context.Principal?.FindFirstValue(ErpClaims.AuthTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var authTime))
            {
                var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                if (time.GetUtcNow() > DateTimeOffset.FromUnixTimeSeconds(authTime).AddHours(options.AbsoluteTimeoutHours))
                {
                    if (Guid.TryParse(context.Principal?.FindFirstValue(ErpClaims.SessionId), out var sessionId))
                    {
                        await context.HttpContext.RequestServices.GetRequiredService<AuthSessionStore>().RevokeAsync(sessionId, CancellationToken.None);
                    }

                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    return;
                }
            }

            await SecurityStampValidator.ValidatePrincipalAsync(context);
        };
    }

    private static void Harden(CookieAuthenticationOptions cookie, string name)
    {
        cookie.Cookie.Name = name;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.Path = "/";
    }

    private static Task ApiAwareRedirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Pasii obligatorii ai contului (02.01, 11.2): cu parola de schimbat sau 2FA de activat, utilizatorul ajunge doar la
/// paginile /account/*; API-ul raspunde 403 cu codul pasului, celelalte pagini redirectioneaza la pasul respectiv.
/// </summary>
public sealed class AccountGateMiddleware(RequestDelegate next)
{
    public const string PasswordChangeRequired = "IDS_PASSWORD_CHANGE_REQUIRED";
    public const string TwoFactorSetupRequired = "IDS_TWO_FACTOR_SETUP_REQUIRED";

    public Task InvokeAsync(HttpContext http, IStringLocalizer<AccountText> text)
    {
        var user = http.User;
        var passwordChange = user.HasClaim(c => c.Type == ErpClaims.MustChangePassword);
        var twoFactorSetup = user.HasClaim(c => c.Type == ErpClaims.TwoFactorSetupRequired);
        var path = http.Request.Path;
        if (user.Identity?.IsAuthenticated != true || (!passwordChange && !twoFactorSetup)
            || path.StartsWithSegments("/account") || path.StartsWithSegments("/health"))
        {
            return next(http);
        }

        if (path.StartsWithSegments("/api"))
        {
            throw passwordChange
                ? new ErpException(ErpErrorKind.Forbidden, PasswordChangeRequired, text["Api.PasswordChangeRequired"])
                : new ErpException(ErpErrorKind.Forbidden, TwoFactorSetupRequired, text["Api.TwoFactorSetupRequired"]);
        }

        http.Response.Redirect(passwordChange ? "/account/password" : "/account/2fa");
        return Task.CompletedTask;
    }
}

/// <summary>Protectia CSRF pe API (08, 8.7): scrierile autentificate cu cookie cer antetul X-XSRF-TOKEN valid.</summary>
public sealed class ApiAntiforgeryMiddleware(RequestDelegate next)
{
    public const string InvalidCode = "IDS_ANTIFORGERY_INVALID";

    public async Task InvokeAsync(HttpContext http, IAntiforgery antiforgery, IStringLocalizer<AccountText> text)
    {
        if (http.Request.Path.StartsWithSegments("/api")
            && !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method)
            && http.User.Identity is { IsAuthenticated: true, AuthenticationType: var type } && type == IdentityConstants.ApplicationScheme)
        {
            if (!await antiforgery.IsRequestValidAsync(http))
            {
                throw new ErpException(ErpErrorKind.Forbidden, InvalidCode, text["Api.AntiforgeryInvalid"]);
            }
        }

        await next(http);
    }
}
