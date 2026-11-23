using System.Net.Mail;
using System.Text.Json;
using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Settings;

/// <summary>Criptarea secretelor din setari (NET-17): inelul de chei Data Protection al instalarii.</summary>
public interface ISecretProtector
{
    string Protect(string value);

    string Unprotect(string value);
}

/// <summary>Criptarea conexiunii la serverul SMTP: 0 fara, 1 STARTTLS, 2 SSL de la conectare.</summary>
public enum SmtpSecurity : byte
{
    None = 0,
    StartTls = 1,
    SslOnConnect = 2,
}

/// <summary>Setarile SMTP ale instalarii (macheta 07f), fara parola.</summary>
public sealed record SmtpOptions(string Host, int Port, SmtpSecurity Security, string? UserName, string FromAddress, string? FromName, string? ReplyTo);

/// <summary>Setarile SMTP cu parola decriptata, pentru trimitere.</summary>
public sealed record SmtpConnection(SmtpOptions Options, string? Password);

/// <summary>Un mesaj gata de trimis: destinatarii, continutul si fisierele.</summary>
public sealed record OutgoingEmail(string To, string? Cc, string Subject, string Body, bool IsHtml, IReadOnlyList<(string FileName, string ContentType, byte[] Content)> Files);

/// <summary>Trimiterea prin SMTP (MailKit).</summary>
public interface IEmailTransport
{
    Task SendAsync(SmtpConnection connection, OutgoingEmail email, CancellationToken ct);
}

public static class SmtpSettings
{
    public const string OptionsKey = "Smtp.Settings";
    public const string PasswordKey = "Smtp.Password";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(SmtpOptions options) => JsonSerializer.Serialize(options, Json);

    public static SmtpOptions? Deserialize(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<SmtpOptions>(value, Json);

    /// <summary>Setarile si parola decriptata; null daca SMTP nu e configurat (CORE_SMTP_NOT_CONFIGURED la trimitere).</summary>
    public static async Task<SmtpConnection?> ReadAsync(ISettingsStore store, ISecretProtector protector, CancellationToken ct)
    {
        var options = Deserialize((await store.GetInstallationAsync(OptionsKey, ct).ConfigureAwait(false)).Value);
        if (options is null)
        {
            return null;
        }

        var password = (await store.GetInstallationAsync(PasswordKey, ct).ConfigureAwait(false)).Value;
        return new SmtpConnection(options, string.IsNullOrEmpty(password) ? null : protector.Unprotect(password));
    }

    public static bool IsEmail(string? value) => !string.IsNullOrWhiteSpace(value) && MailAddress.TryCreate(value.Trim(), out var a) && a.Address == value.Trim();
}

/// <summary>Setarile SMTP din Configurare › Setari › Email (macheta 07f); parola nu se intoarce niciodata, doar daca exista (NET-17).</summary>
public sealed record SmtpSettingsDto(string? Host, int Port, SmtpSecurity Security, string? UserName, bool HasPassword, string? FromAddress, string? FromName,
    string? ReplyTo, byte[]? RowVersion);

[Module("Core")]
[RequiresPermission(CorePermissions.SettingsManage)]
public sealed record GetSmtpSettings : IQuery<SmtpSettingsDto>;

internal sealed class GetSmtpSettingsHandler(ISettingsStore store) : IQueryHandler<GetSmtpSettings, SmtpSettingsDto>
{
    public async Task<SmtpSettingsDto> Handle(GetSmtpSettings request, CancellationToken cancellationToken)
    {
        var stored = await store.GetInstallationAsync(SmtpSettings.OptionsKey, cancellationToken).ConfigureAwait(false);
        var password = await store.GetInstallationAsync(SmtpSettings.PasswordKey, cancellationToken).ConfigureAwait(false);
        var o = SmtpSettings.Deserialize(stored.Value);
        return new SmtpSettingsDto(o?.Host, o?.Port ?? 587, o?.Security ?? SmtpSecurity.StartTls, o?.UserName, !string.IsNullOrEmpty(password.Value), o?.FromAddress,
            o?.FromName, o?.ReplyTo, stored.RowVersion);
    }
}

/// <summary>
/// Salveaza setarile SMTP ale instalarii (Core.Settings.Manage, auditat fara parola): Password null pastreaza parola, "" o sterge, altfel o
/// cripteaza cu Data Protection (NET-17).
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.SettingsManage)]
[Audited("Core.Settings.Smtp")]
public sealed record SaveSmtpSettings(string Host, int Port, SmtpSecurity Security, string? UserName, string? Password, string FromAddress, string? FromName,
    string? ReplyTo, byte[]? RowVersion) : ICommand<SmtpSettingsDto>;

internal sealed class SaveSmtpSettingsValidator : AbstractValidator<SaveSmtpSettings>
{
    public SaveSmtpSettingsValidator()
    {
        RuleFor(x => x.Host).NotEmpty().WithMessage("Serverul SMTP este obligatoriu.").MaximumLength(200)
            .Must(h => Uri.CheckHostName(h?.Trim()) != UriHostNameType.Unknown).WithMessage("Serverul SMTP nu este un nume de calculator valid.");
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.Security).IsInEnum();
        RuleFor(x => x.UserName).MaximumLength(200);
        RuleFor(x => x.Password).MaximumLength(500);
        RuleFor(x => x.FromAddress).Must(SmtpSettings.IsEmail).WithMessage("Adresa expeditorului nu este validă.");
        RuleFor(x => x.FromName).MaximumLength(200);
        RuleFor(x => x.ReplyTo).Must(r => string.IsNullOrWhiteSpace(r) || SmtpSettings.IsEmail(r)).WithMessage("Adresa de răspuns nu este validă.");
    }
}

internal sealed class SaveSmtpSettingsHandler(ISettingsStore store, ISecretProtector protector, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveSmtpSettings, SmtpSettingsDto>
{
    public async Task<SmtpSettingsDto> Handle(SaveSmtpSettings request, CancellationToken cancellationToken)
    {
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var options = new SmtpOptions(request.Host.Trim(), request.Port, request.Security, Clean(request.UserName), request.FromAddress.Trim(), Clean(request.FromName),
            Clean(request.ReplyTo));
        var version = await store.SaveInstallationAsync(SmtpSettings.OptionsKey, SmtpSettings.Serialize(options), request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        var current = await store.GetInstallationAsync(SmtpSettings.PasswordKey, cancellationToken).ConfigureAwait(false);
        if (request.Password is not null)
        {
            await store.SaveInstallationAsync(SmtpSettings.PasswordKey, request.Password.Length == 0 ? null : protector.Protect(request.Password), current.RowVersion,
                context.UserId, cancellationToken).ConfigureAwait(false);
        }

        var hasPassword = request.Password is null ? !string.IsNullOrEmpty(current.Value) : request.Password.Length > 0;
        audit.Describe("Core.Setting", null, new { Key = SmtpSettings.OptionsKey, options.Host, options.Port, options.Security, options.UserName, options.FromAddress,
            PasswordChanged = request.Password is not null });
        return new SmtpSettingsDto(options.Host, options.Port, options.Security, options.UserName, hasPassword, options.FromAddress, options.FromName, options.ReplyTo,
            version);
    }
}

/// <summary>„Trimite email de test” (macheta 07f): trimite imediat, cu setarile salvate, si intoarce durata; eroarea serverului apare ca mesaj.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.SettingsManage)]
[Audited("Core.Settings.SmtpTest")]
public sealed record SendTestEmail(string To) : ICommand<SmtpTestResultDto>;

public sealed record SmtpTestResultDto(string To, long ElapsedMilliseconds);

internal sealed class SendTestEmailValidator : AbstractValidator<SendTestEmail>
{
    public SendTestEmailValidator() => RuleFor(x => x.To).Must(SmtpSettings.IsEmail).WithMessage("Adresa de test nu este validă.");
}

internal sealed class SendTestEmailHandler(ISettingsStore store, ISecretProtector protector, IEmailTransport transport, TimeProvider time, IAuditScope audit)
    : ICommandHandler<SendTestEmail, SmtpTestResultDto>
{
    public async Task<SmtpTestResultDto> Handle(SendTestEmail request, CancellationToken cancellationToken)
    {
        var connection = await SmtpSettings.ReadAsync(store, protector, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.Conflict("CORE_SMTP_NOT_CONFIGURED", "Serverul SMTP nu este configurat.");
        var started = time.GetTimestamp();
        try
        {
            await transport.SendAsync(connection, new OutgoingEmail(request.To.Trim(), null, "ERP · email de test",
                "Acesta este un mesaj de test trimis din ERP. Setările SMTP funcționează.", false, []), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException and not ErpException)
        {
            throw new ErpException(ErpErrorKind.Unavailable, "CORE_EMAIL_SEND_FAILED", $"Serverul SMTP a refuzat mesajul: {e.Message}", e);
        }

        var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
        audit.Describe("Core.Setting", null, new { Test = true, Elapsed = elapsed });
        return new SmtpTestResultDto(request.To.Trim(), elapsed);
    }
}
