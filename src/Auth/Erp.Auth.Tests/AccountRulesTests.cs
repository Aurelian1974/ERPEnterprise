using System.Security.Claims;
using Erp.Auth.Data;
using Erp.Auth.Identity;
using Erp.Auth.Web;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace Erp.Auth.Tests;

/// <summary>Regulile fara baza de date: pasul urmator dupa autentificare, pasii obligatorii, codurile de rezerva.</summary>
public sealed class AccountRulesTests
{
    private static readonly IUrlHelper Url = new UrlHelper(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()));

    [Theory]
    [InlineData(true, false, false, "/rapoarte", "/account/password")]
    [InlineData(false, true, false, "/rapoarte", "/account/2fa")]
    [InlineData(false, true, true, "/rapoarte", "/rapoarte")]
    [InlineData(false, false, false, "https://atacator.example/", "/")]
    [InlineData(false, false, false, "//atacator.example/", "/")]
    [InlineData(false, false, false, "/account/logout", "/")]
    [InlineData(false, false, false, null, "/")]
    public void Next_step_puts_mandatory_steps_first_and_accepts_only_local_return_urls(
        bool mustChangePassword, bool twoFactorRequired, bool twoFactorEnabled, string? returnUrl, string expected)
    {
        var user = new UserAccount { MustChangePassword = mustChangePassword, TwoFactorRequired = twoFactorRequired, TwoFactorEnabled = twoFactorEnabled };

        Assert.Equal(expected, AccountFlow.NextStep(user, returnUrl, Url));
    }

    [Fact]
    public void Lockout_message_counts_the_minutes_left_not_the_configured_duration()
    {
        var now = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

        // Blocarea a inceput acum 26 de minute din 30: au mai rămas 4, nu 30.
        Assert.Equal(4, AccountFlow.RemainingLockoutMinutes(now.AddMinutes(4), now, 30));
        // Rotunjire in sus, ca 3 minute si 10 secunde sa nu apara ca 3.
        Assert.Equal(4, AccountFlow.RemainingLockoutMinutes(now.AddMinutes(3).AddSeconds(10), now, 30));
        // Niciodata „0 de minute”, nici la blocare deja expirata.
        Assert.Equal(1, AccountFlow.RemainingLockoutMinutes(now.AddSeconds(-5), now, 30));
        // Fara moment de deblocare cunoscut: durata configurata.
        Assert.Equal(30, AccountFlow.RemainingLockoutMinutes(null, now, 30));
    }

    [Fact]
    public void Recovery_codes_match_regardless_of_case_spaces_and_dashes()
    {
        Assert.Equal(UserAccountStore.HashRecoveryCode("ABCDE-12345"), UserAccountStore.HashRecoveryCode(" abcde 12345 "));
        Assert.Equal(UserAccountStore.HashRecoveryCode("ABCDE-12345"), UserAccountStore.HashRecoveryCode("abcde12345"));
        Assert.NotEqual(UserAccountStore.HashRecoveryCode("ABCDE-12345"), UserAccountStore.HashRecoveryCode("ABCDE-12346"));
        Assert.Equal(64, UserAccountStore.HashRecoveryCode("x").Length);
    }

    [Theory]
    [InlineData(ErpClaims.MustChangePassword, "/api/core/me", AccountGateMiddleware.PasswordChangeRequired, null)]
    [InlineData(ErpClaims.TwoFactorSetupRequired, "/api/core/me", AccountGateMiddleware.TwoFactorSetupRequired, null)]
    [InlineData(ErpClaims.MustChangePassword, "/", null, "/account/password")]
    [InlineData(ErpClaims.TwoFactorSetupRequired, "/rapoarte", null, "/account/2fa")]
    [InlineData(ErpClaims.MustChangePassword, "/account/logout", null, null)]
    [InlineData(ErpClaims.MustChangePassword, "/health/live", null, null)]
    public async Task Pending_account_steps_block_the_api_and_redirect_pages(string claim, string path, string? apiCode, string? redirect)
    {
        var called = false;
        var gate = new AccountGateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claim, "true")], "Identity.Application")) };
        http.Request.Path = path;

        if (apiCode is not null)
        {
            var ex = await Assert.ThrowsAsync<ErpException>(() => gate.InvokeAsync(http, new KeyText()));
            Assert.Equal((ErpErrorKind.Forbidden, apiCode), (ex.Kind, ex.Code));
            return;
        }

        await gate.InvokeAsync(http, new KeyText());

        Assert.Equal(redirect is null, called);
        Assert.Equal(redirect, redirect is null ? null : http.Response.Headers.Location.ToString());
    }

    private sealed class KeyText : IStringLocalizer<AccountText>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
