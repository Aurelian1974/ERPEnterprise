using Erp.Core.Application;
using Erp.Core.Infrastructure;
using Erp.Finance.Infrastructure;
using Erp.Inventory.Infrastructure;
using Erp.Core.Infrastructure.Messaging;
using Erp.Hosting;
using Erp.Worker.Jobs;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;

// Erp.Worker (8.12): serviciu Windows, procesarea Hangfire pornita doar cu baza compatibila si fara mentenanta (10.4, 10.5).
// In mediul Development, fara --instance-dir, configurarea vine din appsettings.Development.json / user-secrets.
var builder = Host.CreateApplicationBuilder(args);
var development = builder.Environment.IsDevelopment();
var instanceDirectory = builder.Configuration["instance-dir"];
if (instanceDirectory is not null)
{
    builder.Configuration.AddErpInstallation(instanceDirectory);
}
else if (!development)
{
    throw new InvalidOperationException("Argumentul --instance-dir lipsește (8.12).");
}

var instance = builder.Configuration["Erp:Instance"] ?? throw new InvalidOperationException("Erp:Instance lipsește din configurare.");

builder.Services.AddWindowsService(options => options.ServiceName = $"ErpWorker-{instance}");

var build = ErpBuildInfo.From(typeof(Program).Assembly);
var connectionString = InstallationConfiguration.ConnectionString(builder.Configuration, "Worker");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(build);
builder.Services.AddSingleton<ISchemaVersionSource>(new SchemaVersionReader(() => new SqlConnection(connectionString)));
builder.Services.AddSingleton(sp => new SchemaVersionGuard(sp.GetRequiredService<ISchemaVersionSource>(), build,
    sp.GetRequiredService<ILogger<SchemaVersionGuard>>(), TimeProvider.System, allowUnversionedBuild: development));
builder.Services.AddHostedService(sp => sp.GetRequiredService<SchemaVersionGuard>());
builder.Services.AddSingleton(new MaintenanceFlag(builder.Configuration["Erp:MaintenanceFlagPath"], TimeProvider.System));
// Documentele PDF (QuestPDF): licenta din configurarea instalarii, implicit Community.
Erp.Pdf.PdfSetup.Configure(builder.Configuration[Erp.Pdf.PdfSetup.ConfigurationKey]);

// Acelasi inel de chei Data Protection ca Erp.Host (08, 5.12): Worker-ul citeste secretele criptate (de exemplu parola SMTP).
builder.Services.AddErpDataProtection(builder.Configuration, () => new SqlConnection(connectionString), development);

// Tabelele Hangfire vin din migrarile hangfire/ (8.5), deci PrepareSchemaIfNecessary = false.
GlobalConfiguration.Configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
var storage = new SqlServerStorage(() => new SqlConnection(connectionString), new SqlServerStorageOptions
{
    SchemaName = "hangfire",
    PrepareSchemaIfNecessary = false,
});
builder.Services.AddSingleton<JobStorage>(storage);
builder.Services.AddSingleton<Func<SqlConnection>>(() => new SqlConnection(connectionString));
builder.Services.AddTransient<UserAccessExpirationJob>();
// Pipeline-ul comenzilor, ca in Erp.Host, pentru joburile care trimit comenzi in numele utilizatorului de sistem (de exemplu importul
// extraselor din folder): licenta, validare, tranzactie, audit, outbox. Contextul e cel de sistem (AddOutboxDispatcher, mai jos).
builder.Services.AddErpMediator(Erp.Finance.Application.FinanceApplication.Assembly, Erp.Inventory.Application.InventoryApplication.Assembly);
builder.Services.AddCoreInfrastructure(connectionString, new Erp.Core.Infrastructure.Licensing.LicenseOptions(development));
// Finante (02.02): inchiderea automata a zilelor de casa, avansurile depasite, importul extraselor din folder.
builder.Services.AddFinanceInfrastructure(connectionString);
builder.Services.AddInventoryInfrastructure();
builder.Services.AddTransient<CompanyVersionJob>();
// Cursurile BNR (02.01, 4.3): aceleasi adrese ca Erp.Host (Bnr:DailyUrl), clientul HTTP partajat.
builder.Services.AddSingleton(new Erp.Bnr.BnrOptions(
    new Uri(builder.Configuration["Bnr:DailyUrl"] ?? Erp.Bnr.BnrOptions.Default.DailyUrl.ToString()),
    new Uri(builder.Configuration["Bnr:TenDaysUrl"] ?? Erp.Bnr.BnrOptions.Default.TenDaysUrl.ToString())));
builder.Services.AddSingleton(_ => new Erp.Bnr.BnrRatesClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));
builder.Services.AddTransient<BnrRatesJob>();
// Procesatorul outbox-ului (02.01, 4.5): consumatorii modulelor se inregistreaza cu AddIntegrationEventHandler.
builder.Services.AddOutboxDispatcher(connectionString);
// Finante (02.02, F1 5.2; F3 5.2): atribuirile la case ale utilizatorului dezactivat se inchid; facturile din Vanzari si Achizitii devin partide.
// Emailul din aplicatie (02.01, 4.7): mesajele puse in outbox pleaca prin serverul SMTP al instalarii.
builder.Services.AddIntegrationEventHandler<Erp.Core.Contracts.EmailCerut, Erp.Core.Infrastructure.Data.SendEmail>();
builder.Services.AddIntegrationEventHandler<Erp.Core.Contracts.UtilizatorDezactivat, Erp.Finance.Infrastructure.Events.CloseCashierAssignments>();
builder.Services.AddIntegrationEventHandler<Erp.Core.Contracts.FirmaModificata, Erp.Inventory.Infrastructure.Events.InitializeCompanyStock>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Settlement.FacturaEmisa, Erp.Finance.Infrastructure.Events.RegisterInvoices>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Settlement.FacturaFurnizorValidata, Erp.Finance.Infrastructure.Events.RegisterInvoices>();
// Contarea automata (02.02, C2 4.1): evenimentele cu model de contare devin note validate sau intra in coada „De contat”.
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Settlement.FacturaEmisa, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Settlement.FacturaFurnizorValidata, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Treasury.IncasareInregistrata, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Treasury.PlataInregistrata, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Settlement.TvaLaIncasareExigibil, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Settlement.DiferentaCursInregistrata, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Settlement.CompensareValidata, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Treasury.DecontCheltuieliValidat, Erp.Finance.Infrastructure.Events.PostEvents>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Periods.PerioadaContabilaRedeschisa, Erp.Finance.Infrastructure.Accounting.RetryQueueOnReopen>();
// Declaratiile (02.02, C4 4.1): redeschiderea lunii marcheaza declaratiile depuse „De verificat”; reinchiderea fara schimbari sterge marcajul.
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Periods.PerioadaContabilaRedeschisa, Erp.Finance.Infrastructure.Declarations.MarkTaxReturnsOnReopen>();
builder.Services.AddIntegrationEventHandler<Erp.Finance.Contracts.Periods.PerioadaContabilaInchisa, Erp.Finance.Infrastructure.Declarations.RecheckTaxReturnsOnClose>();
builder.Services.AddSingleton(sp => new OutboxProcessorLoop(sp.GetRequiredService<OutboxDispatcher>(),
    TimeSpan.FromSeconds(int.TryParse(builder.Configuration["Outbox:IntervalSeconds"], out var seconds) && seconds > 0 ? seconds : 2),
    sp.GetRequiredService<ILogger<OutboxProcessorLoop>>()));
builder.Services.AddSingleton<IBackgroundProcessingFactory>(sp => new HangfireProcessingFactory(sp.GetRequiredService<JobStorage>(), instance,
    sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<OutboxProcessorLoop>()));
builder.Services.AddHostedService(sp => new BackgroundProcessingLifecycle(
    sp.GetRequiredService<IBackgroundProcessingFactory>(),
    sp.GetRequiredService<SchemaVersionGuard>(),
    sp.GetRequiredService<MaintenanceFlag>(),
    sp.GetRequiredService<ILogger<BackgroundProcessingLifecycle>>(),
    TimeProvider.System));

builder.Build().Run();

internal sealed class HangfireProcessingFactory(JobStorage storage, string instance, IServiceScopeFactory scopes, OutboxProcessorLoop outbox)
    : IBackgroundProcessingFactory
{
    // Joburile recurente se inregistreaza la pornirea procesarii, deci numai cu baza compatibila si fara mentenanta (10.4, 10.5).
    public IAsyncDisposable Start()
    {
        var jobs = new RecurringJobManager(storage);
        // 02.01, 3.4: zilnic, imediat dupa miezul noptii UTC (ziua folosita si de autentificare, core.v_UserLoginStatus).
        jobs.AddOrUpdate<UserAccessExpirationJob>(UserAccessExpirationJob.Id, job => job.RunAsync(CancellationToken.None), "5 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        jobs.AddOrUpdate<CompanyVersionJob>(CompanyVersionJob.Id, job => job.RunAsync(CancellationToken.None), "2 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        // 02.01, 4.3: cursurile BNR, in zilele lucratoare dupa ora publicarii (ora Romaniei).
        jobs.AddOrUpdate<BnrRatesJob>(BnrRatesJob.Id, job => job.RunAsync(CancellationToken.None), BnrRatesJob.Schedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        // 02.02, F1 4.2: inchiderea automata a zilelor de casa, la 00:05 ora Romaniei.
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Treasury.CashDayAutoClose>(Erp.Finance.Infrastructure.Treasury.CashDayAutoClose.JobId,
            job => job.RunAsync(CancellationToken.None), Erp.Finance.Infrastructure.Treasury.CashDayAutoClose.Schedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        // 02.02, F2 4.1: importul automat al extraselor din folderele conturilor, la 15 minute.
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Bank.BankFolderImport>(Erp.Finance.Infrastructure.Bank.BankFolderImport.JobId,
            job => job.RunAsync(CancellationToken.None), Erp.Finance.Infrastructure.Bank.BankFolderImport.Schedule,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        // 02.02, F1 4.4: avansurile spre decontare depasite, la 00:15 ora Romaniei.
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Treasury.CashAdvanceOverdue>(Erp.Finance.Infrastructure.Treasury.CashAdvanceOverdue.JobId,
            job => job.RunAsync(CancellationToken.None), Erp.Finance.Infrastructure.Treasury.CashAdvanceOverdue.Schedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        // 02.02, F5 3.4: istoricul de plata al clientilor pe intervale de intarziere, lunar pe 1 la 01:30 ora Romaniei.
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Settlement.PaymentBehaviorRecalculation>(Erp.Finance.Infrastructure.Settlement.PaymentBehaviorRecalculation.JobId,
            job => job.RunAsync(CancellationToken.None), Erp.Finance.Infrastructure.Settlement.PaymentBehaviorRecalculation.Schedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        // 02.02, F5 3.6, 3.7: instantaneul automat al prognozei lunea la 06:40 si alertele de cash-flow zilnic la 07:10, ora Romaniei.
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Settlement.CashFlowJobs>(Erp.Finance.Infrastructure.Settlement.CashFlowJobs.SnapshotJobId,
            job => job.SnapshotsAsync(CancellationToken.None), Erp.Finance.Infrastructure.Settlement.CashFlowJobs.SnapshotSchedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Settlement.CashFlowJobs>(Erp.Finance.Infrastructure.Settlement.CashFlowJobs.AlertJobId,
            job => job.AlertsAsync(CancellationToken.None), Erp.Finance.Infrastructure.Settlement.CashFlowJobs.AlertSchedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        // 02.02, C3 3.1: anii financiari urmatori, cu 60 de zile inainte, zilnic la 00:20 ora Romaniei.
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Periods.FiscalYearCreation>(Erp.Finance.Infrastructure.Periods.FiscalYearCreation.JobId,
            job => job.RunAsync(CancellationToken.None), Erp.Finance.Infrastructure.Periods.FiscalYearCreation.Schedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        // 02.02, C2 4.4: coada „De contat” se reincearca la 15 minute (configurarea schimbata intre timp poate rezolva motivul).
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Accounting.PostingQueueRetry>(Erp.Finance.Infrastructure.Accounting.PostingQueueRetry.JobId,
            job => job.RunAsync(CancellationToken.None), Erp.Finance.Infrastructure.Accounting.PostingQueueRetry.Schedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        // 02.02, C2 3.8: ciornele notelor recurente scadente, zilnic la 00:40 ora Romaniei.
        jobs.AddOrUpdate<Erp.Finance.Infrastructure.Accounting.RecurringEntryGeneration>(Erp.Finance.Infrastructure.Accounting.RecurringEntryGeneration.JobId,
            job => job.RunAsync(CancellationToken.None), Erp.Finance.Infrastructure.Accounting.RecurringEntryGeneration.Schedule,
            new RecurringJobOptions { TimeZone = BnrRatesJob.Romania });
        return new Running(new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                ServerName = $"ErpWorker-{instance}:{Environment.MachineName}",
                Activator = new ServiceProviderJobActivator(scopes),
            }, storage), outbox.Start());
    }

    private sealed class Running(BackgroundJobServer server, IAsyncDisposable outbox) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await outbox.DisposeAsync().ConfigureAwait(false);
            server.SendStop();
            server.Dispose();
        }
    }
}
