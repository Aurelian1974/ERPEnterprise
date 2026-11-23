using System.Data;
using Dapper;
using Erp.Core.Application.Settings;
using Erp.Core.Contracts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using MimeKit;

namespace Erp.Core.Infrastructure.Data;

/// <summary>Secretele din setari criptate cu inelul de chei Data Protection al instalarii (NET-17); scopul e versionat.</summary>
public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Erp.Core.Setting.Secret.v1");

    public string Protect(string value) => protector.Protect(value);

    public string Unprotect(string value) => protector.Unprotect(value);
}

/// <summary>Mesajul in core.EmailMessage si evenimentul EmailCerut in outbox, in aceeasi tranzactie (02.01, 4.7).</summary>
public sealed class EmailOutbox(IDbSession session, IOutbox outbox, ICurrentContext context) : IEmailOutbox
{
    public async Task<int> EnqueueAsync(EmailRequest email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (!SmtpSettings.IsEmail(email.To) || (email.Cc is not null && !SmtpSettings.IsEmail(email.Cc)))
        {
            throw new ErpValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["Adresa de email nu este validă."] });
        }

        var id = await session.Connection.QuerySingleAsync<int>(new CommandDefinition("core.usp_EmailMessage_Create", new
        {
            CompanyId = context.CompanyId == 0 ? (int?)null : context.CompanyId, ToAddress = email.To.Trim(), CcAddress = email.Cc?.Trim(),
            Subject = email.Subject.Length <= 300 ? email.Subject : email.Subject[..300], email.Body, email.IsHtml, email.EntityType, email.EntityId,
            AttachmentIds = System.Text.Json.JsonSerializer.Serialize(email.AttachmentIds), context.UserId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        await outbox.EnqueueAsync(new EmailCerut(id), ct).ConfigureAwait(false);
        return id;
    }
}

/// <summary>Trimiterea prin SMTP cu MailKit: criptarea aleasa (STARTTLS, SSL), autentificarea daca exista utilizator, expeditorul si raspunsul.</summary>
public sealed class MailKitTransport : IEmailTransport
{
    public async Task SendAsync(SmtpConnection connection, OutgoingEmail email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(email);
        var o = connection.Options;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName ?? o.FromAddress, o.FromAddress));
        if (o.ReplyTo is not null)
        {
            message.ReplyTo.Add(MailboxAddress.Parse(o.ReplyTo));
        }

        message.To.Add(MailboxAddress.Parse(email.To));
        if (email.Cc is not null)
        {
            message.Cc.Add(MailboxAddress.Parse(email.Cc));
        }

        message.Subject = email.Subject;
        var body = new BodyBuilder();
        if (email.IsHtml)
        {
            body.HtmlBody = email.Body;
        }
        else
        {
            body.TextBody = email.Body;
        }

        foreach (var (fileName, contentType, content) in email.Files)
        {
            body.Attachments.Add(fileName, content, ContentType.Parse(contentType));
        }

        message.Body = body.ToMessageBody();
        using var client = new SmtpClient { Timeout = 30_000 };
        var security = o.Security switch
        {
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };
        await client.ConnectAsync(o.Host, o.Port, security, ct).ConfigureAwait(false);
        if (o.UserName is not null)
        {
            await client.AuthenticateAsync(o.UserName, connection.Password ?? "", ct).ConfigureAwait(false);
        }

        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Consumatorul EmailCerut din Worker (02.01, 4.7): trimite mesajul cu setarile SMTP ale instalarii si atasamentele lui; un mesaj deja trimis
/// nu se retrimite. O eroare se noteaza pe mesaj si se arunca, ca outbox-ul sa reincerce (1 min, 5 min, 30 min, 2 h).
/// </summary>
public sealed class SendEmail(IDbSession session, ISettingsStore settings, ISecretProtector protector, IAttachmentStore attachments, IEmailTransport transport,
    Messaging.OutboxConnection outboxConnection) : IIntegrationEventHandler<EmailCerut>
{
    public async Task HandleAsync(EmailCerut evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("core.usp_EmailMessage_Get", new { Id = evt.EmailMessageId },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var message = await grid.ReadSingleOrDefaultAsync<MessageRow>().ConfigureAwait(false);
        var attachmentIds = (await grid.ReadAsync<int>().ConfigureAwait(false)).ToList();
        if (message is null || message.Status != 1)
        {
            return;
        }

        try
        {
            var connection = await SmtpSettings.ReadAsync(settings, protector, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Serverul SMTP nu este configurat (Configurare › Setări › Email).");
            var files = new List<(string, string, byte[])>();
            foreach (var id in attachmentIds)
            {
                var info = await attachments.GetInfoAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"Atașamentul {id} lipsește.");
                await using var stream = await attachments.OpenAsync(id, ct).ConfigureAwait(false);
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, ct).ConfigureAwait(false);
                files.Add((info.FileName, info.ContentType, copy.ToArray()));
            }

            await transport.SendAsync(connection, new OutgoingEmail(message.ToAddress, message.CcAddress, message.Subject, message.Body, message.IsHtml, files), ct)
                .ConfigureAwait(false);
            await SetResultAsync(message.Id, true, null, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Tranzactia consumatorului se anuleaza la eroare; eroarea se noteaza pe mesaj separat, ca sa ramana vizibila.
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(outboxConnection.ConnectionString);
            await connection.ExecuteAsync(new CommandDefinition("core.usp_EmailMessage_SetResult",
                new { message.Id, Sent = false, Error = e.Message.Length <= 1000 ? e.Message : e.Message[..1000] }, commandType: CommandType.StoredProcedure,
                cancellationToken: ct)).ConfigureAwait(false);
            throw;
        }
    }

    private Task SetResultAsync(int id, bool sent, string? error, CancellationToken ct) =>
        session.Connection.ExecuteAsync(new CommandDefinition("core.usp_EmailMessage_SetResult", new { Id = id, Sent = sent, Error = error }, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private sealed record MessageRow(int Id, int? CompanyId, string ToAddress, string? CcAddress, string Subject, string Body, bool IsHtml, byte Status);
}
