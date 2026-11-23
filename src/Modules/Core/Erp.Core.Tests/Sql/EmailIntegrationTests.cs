using System.Collections.Concurrent;
using Dapper;
using Erp.Core.Application;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Settings;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure;
using Erp.Core.Infrastructure.Data;
using Erp.Core.Infrastructure.Licensing;
using Erp.Core.Infrastructure.Messaging;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Tests.Sql;

/// <summary>Transportul de test: retine mesajele trimise; esueaza cat timp Failing e setat.</summary>
public sealed class FakeTransport : IEmailTransport
{
    public ConcurrentQueue<(SmtpConnection Connection, OutgoingEmail Email)> Sent { get; } = new();

    public string? Failing { get; set; }

    public Task SendAsync(SmtpConnection connection, OutgoingEmail email, CancellationToken ct)
    {
        if (Failing is not null)
        {
            throw new InvalidOperationException(Failing);
        }

        Sent.Enqueue((connection, email));
        return Task.CompletedTask;
    }
}

/// <summary>Baza ERP_IT_EMAIL: un administrator si o firma.</summary>
public sealed class EmailDatabase(SqlServerFixture sql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public int AdminId { get; private set; }

    public int CompanyId { get; private set; }

    public IDataProtectionProvider DataProtection { get; } = new EphemeralDataProtectionProvider();

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await sql.CreateErpDatabaseAsync("ERP_IT_EMAIL", CancellationToken.None);
        await using var connection = new SqlConnection(ConnectionString);
        AdminId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.[User] (SubjectId, UserName, DisplayName, IsActive, IsSystem, CreatedBy)
            VALUES (NEWID(), N'admin.email', N'Admin Email', 1, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
        CompanyId = await connection.ExecuteScalarAsync<int>("""
            INSERT INTO core.Company (Code, Name, FiscalCode, VatPayer, VatOnCollection, TradeRegisterNo, Address, City, CountyCode, CountryCode,
                DefaultPrecisionPolicyId, IncludeInConsolidation, IsActive, CreatedBy)
            SELECT N'EFIRMA', N'Firma email', N'18547290', 1, 0, N'J40/1234/2015', N'Str. 1', N'București', N'B', 'RO', Id, 1, 1, 1
            FROM core.PrecisionPolicy WHERE Code = N'STANDARD';
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Emailul din aplicatie (02.01, 4.7): setarile SMTP cu parola criptata (NET-17, nu se intoarce niciodata), mesajul pus in outbox in tranzactia
/// operatiei si trimis de consumatorul EmailCerut cu atasamentele lui, o singura data; o eroare SMTP ramane pe mesaj si se reincearca.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EmailIntegrationTests(EmailDatabase database) : IClassFixture<EmailDatabase>
{
    private readonly FakeTransport transport = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpMediator(typeof(EmailIntegrationTests).Assembly);
        services.AddCoreInfrastructure(database.ConnectionString, new LicenseOptions(Development: true));
        services.AddSingleton(database.DataProtection);
        services.AddSingleton<IEmailTransport>(transport);
        var context = new FakeContext { UserId = database.AdminId, CompanyId = database.CompanyId };
        context.Permissions.Add(CorePermissions.SettingsManage);
        services.AddSingleton<ICurrentContext>(context);
        return services.BuildServiceProvider();
    }

    private async Task<T> Send<T>(IRequest<T> request)
    {
        await using var provider = Services();
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    private OutboxDispatcher Dispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreInfrastructure(database.ConnectionString, new LicenseOptions(Development: true));
        services.AddOutboxDispatcher(database.ConnectionString);
        services.AddSingleton(database.DataProtection);
        services.AddSingleton<IEmailTransport>(transport);
        services.AddIntegrationEventHandler<EmailCerut, SendEmail>();
        return services.BuildServiceProvider().GetRequiredService<OutboxDispatcher>();
    }

    private async Task<T> Query<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    [Fact]
    [Trait("Criteriu", "02.01-4.7")]
    public async Task Smtp_settings_keep_the_password_encrypted_and_an_email_from_the_outbox_is_sent_once_with_its_attachment()
    {
        var current = await Send(new GetSmtpSettings());
        var saved = await Send(new SaveSmtpSettings("mail.exemplu.ro", 587, SmtpSecurity.StartTls, "erp@exemplu.ro", "Parola-Secreta1", "erp@exemplu.ro",
            "Exemplu Distribuție", "facturare@exemplu.ro", current.RowVersion));
        Assert.True(saved.HasPassword);
        var stored = await Query<string>("SELECT Value FROM core.Setting WHERE [Key] = 'Smtp.Password'");
        Assert.DoesNotContain("Parola-Secreta1", stored, StringComparison.Ordinal);
        var invalid = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SaveSmtpSettings("mail.exemplu.ro", 587, SmtpSecurity.StartTls, null, null,
            "nu-e-adresa", null, null, saved.RowVersion)));
        Assert.Contains("fromAddress", invalid.Errors.Keys);

        // Parola nu se schimba cand nu se trimite; emailul de test o foloseste decriptata.
        var kept = await Send(new SaveSmtpSettings("mail.exemplu.ro", 465, SmtpSecurity.SslOnConnect, "erp@exemplu.ro", null, "erp@exemplu.ro", null, null,
            saved.RowVersion));
        Assert.True(kept.HasPassword);
        var test = await Send(new SendTestEmail("andrei.matei@exemplu.ro"));
        Assert.Equal("andrei.matei@exemplu.ro", test.To);
        Assert.True(transport.Sent.TryDequeue(out var probe));
        Assert.Equal(("Parola-Secreta1", 465, SmtpSecurity.SslOnConnect), (probe.Connection.Password, probe.Connection.Options.Port, probe.Connection.Options.Security));

        // Mesajul cu atasament, in tranzactia operatiei; consumatorul il trimite o singura data.
        int messageId;
        await using (var provider = Services())
        {
            await using var scope = provider.CreateAsyncScope();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unit.BeginAsync(Ct);
            var attachment = await scope.ServiceProvider.GetRequiredService<IAttachmentStore>().SaveAsync("Finance.BalanceConfirmation", 7, "confirmare.pdf",
                "application/pdf", new MemoryStream("%PDF-1.4 confirmare"u8.ToArray()), Ct);
            messageId = await scope.ServiceProvider.GetRequiredService<IEmailOutbox>().EnqueueAsync(new EmailRequest("client@partener.ro", null,
                "Confirmare de sold", "Vă rugăm să confirmați soldul.", false, [attachment], "Finance.BalanceConfirmation", 7), Ct);
            await unit.CommitAsync(Ct);
        }

        var dispatcher = Dispatcher();
        await dispatcher.RunOnceAsync(Ct);
        await dispatcher.RunOnceAsync(Ct);
        var sent = Assert.Single(transport.Sent);
        Assert.Equal(("client@partener.ro", "confirmare.pdf"), (sent.Email.To, Assert.Single(sent.Email.Files).FileName));
        Assert.Equal((byte)2, await Query<byte>("SELECT Status FROM core.EmailMessage WHERE Id = @Id", new { Id = messageId }));

        // O eroare SMTP ramane pe mesaj, care ramane de trimis; outbox-ul il reia.
        transport.Failing = "5.7.1 Relay access denied";
        int failingId;
        await using (var provider = Services())
        {
            await using var scope = provider.CreateAsyncScope();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unit.BeginAsync(Ct);
            failingId = await scope.ServiceProvider.GetRequiredService<IEmailOutbox>().EnqueueAsync(new EmailRequest("altul@partener.ro", null, "Test", "Text", false, []),
                Ct);
            await unit.CommitAsync(Ct);
        }

        await dispatcher.RunOnceAsync(Ct);
        Assert.Equal(((byte)1, 1, "5.7.1 Relay access denied"), await Query<(byte, int, string)>(
            "SELECT Status, Attempts, LastError FROM core.EmailMessage WHERE Id = @Id", new { Id = failingId }));
        transport.Failing = null;
    }
}
