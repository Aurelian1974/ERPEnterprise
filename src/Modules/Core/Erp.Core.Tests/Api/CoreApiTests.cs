using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Erp.Core.Api;
using Erp.Core.Application;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Me;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Licensing;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Core.Tests.Api;

/// <summary>Contextul cererii si ProblemDetails (NET-10) pe un server HTTP de test, fara baza de date.</summary>
public sealed class CoreApiTests
{
    private static readonly Guid Subject = Guid.Parse("8f0d2a5c-0000-4000-8000-000000000001");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(HttpClient Client, FakeUsers Users)> StartAsync()
    {
        var users = new FakeUsers();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", null);
        builder.Services.AddErpMediator(typeof(CoreApiTests).Assembly);
        builder.Services.AddErpApi();
        // Conexiunea nu se deschide: cererile testate se opresc inainte de tranzactie.
        builder.Services.AddCoreInfrastructure("Server=nefolosit;Database=nefolosit", new LicenseOptions(null, Development: true));
        builder.Services.AddSingleton<IUserContextLoader>(users);
        builder.Services.AddSingleton<ILicenseInfo>(new FakeLicense());
        builder.Services.AddSingleton<Erp.Core.Application.Organization.IOrganizationStore>(new FakeOrganizationStore());
        builder.Services.AddScoped<HandlerCalls>();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseErpApi();
        app.MapCoreApi();
        app.MapPost("/test/ping", (PingCommand command, ISender sender, CancellationToken ct) => sender.Send(command, ct));
        await app.StartAsync(Ct);
        return (app.GetTestClient(), users);
    }

    [Fact]
    public async Task Anonymous_request_gets_401_problem_details_with_code_and_correlation()
    {
        var (client, _) = await StartAsync();
        var correlation = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/core/me");
        request.Headers.Add(CurrentContextMiddleware.CorrelationHeader, correlation.ToString());

        using var response = await client.SendAsync(request, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CoreErrorCodes.Unauthenticated, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(correlation, body.RootElement.GetProperty("correlationId").GetGuid());
        Assert.Equal(correlation.ToString(), Assert.Single(response.Headers.GetValues(CurrentContextMiddleware.CorrelationHeader)));
    }

    [Fact]
    public async Task Authenticated_user_gets_profile_and_permissions_for_the_requested_company()
    {
        var (client, users) = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/core/me");
        request.Headers.Add(TestAuthentication.Header, Subject.ToString());
        request.Headers.Add(CurrentContextMiddleware.CompanyHeader, "7");

        using var response = await client.SendAsync(request, Ct);
        var me = await response.Content.ReadFromJsonAsync<CurrentUserDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((Subject, (int?)7), users.LastCall);
        Assert.Equal(("administrator", 7), (me!.UserName, me.CompanyId));
        Assert.Equal(["Core.Partner.View", "Core.Settings.Manage"], me.Permissions);
        Assert.Equal(["Core"], me.Modules);
        Assert.Equal("Valid", me.LicenseState);
    }

    [Fact]
    public async Task Inactive_user_with_a_valid_cookie_is_treated_as_anonymous()
    {
        var (client, users) = await StartAsync();
        users.Active = false;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/core/me");
        request.Headers.Add(TestAuthentication.Header, Subject.ToString());

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_aborted_by_the_client_is_not_reported_as_an_unexpected_error()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestAborted = aborted.Token };
        var problems = new RecordingProblems();
        var handler = new ErpExceptionHandler(problems, new NoTranslation(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ErpExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(http, new OperationCanceledException(), Ct);

        Assert.True(handled);
        Assert.Equal(499, http.Response.StatusCode);
        Assert.False(problems.Written);
    }

    private sealed class NoTranslation : ISqlErrorTranslator
    {
        public ErpException? Translate(Exception exception) => null;
    }

    private sealed class RecordingProblems : Microsoft.AspNetCore.Http.IProblemDetailsService
    {
        public bool Written { get; private set; }

        public ValueTask WriteAsync(Microsoft.AspNetCore.Http.ProblemDetailsContext context)
        {
            Written = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Validation_errors_are_returned_as_400_with_errors_per_field()
    {
        var (client, _) = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/test/ping") { Content = JsonContent.Create(new { text = "" }) };
        request.Headers.Add(TestAuthentication.Header, Subject.ToString());

        using var response = await client.SendAsync(request, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CoreErrorCodes.Validation, body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("text", out _));
    }

    private sealed class FakeUsers : IUserContextLoader
    {
        public bool Active { get; set; } = true;

        public (Guid, int?) LastCall { get; private set; }

        public Task<UserContextData?> LoadAsync(Guid subjectId, int? companyId, CancellationToken ct)
        {
            LastCall = (subjectId, companyId);
            return Task.FromResult(Active
                ? new UserContextData(1, "administrator", "Administrator ERP", "ro", companyId ?? 0, ["Core.Settings.Manage", "Core.Partner.View"])
                : null);
        }
    }

    /// <summary>Autentificare de test: antetul X-Test-Subject inlocuieste cookie-ul (etapa 2 aduce Identity).</summary>
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Header = "X-Test-Subject";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
