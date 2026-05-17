---
name: hangfire-job
description: >-
  Generează background jobs pentru ERP cu Hangfire: recurring jobs (programate),
  fire-and-forget, continuations. Înregistrare în IModuleInstaller, SQL Server storage,
  idempotență, logging structurat, error handling.
---

# Hangfire Job

## Când se aplică
Când utilizatorul cere un job de fundal: trimitere email-uri programate,
generare rapoarte, sincronizări, cleanup, notificări, procesare asincronă.

## Tipuri de jobs

```
Recurring       — rulează după un schedule (Cron): rapoarte zilnice, cleanup, remindere
Fire-and-forget — declanșat din handler/controller: generare PDF, trimitere email
Continuation    — rulează după alt job: etape secvențiale
Scheduled       — rulează o singură dată la un moment viitor
```

---

## 1. Job Class

```csharp
// {Module}.Infrastructure/Jobs/InvoiceReminderJob.cs
public sealed class InvoiceReminderJob
{
    private readonly IInvoiceRepository _repo;
    private readonly IEmailService _emailService;
    private readonly ILogger<InvoiceReminderJob> _logger;

    public InvoiceReminderJob(
        IInvoiceRepository repo,
        IEmailService emailService,
        ILogger<InvoiceReminderJob> logger)
    {
        _repo         = repo;
        _emailService = emailService;
        _logger       = logger;
    }

    // Metoda job-ului — async Task, nu void
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task SendRemindersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting {JobName}", nameof(InvoiceReminderJob));

        // Idempotență — job-ul poate rula de mai multe ori fără efecte duplicate
        var overdueInvoices = await _repo.GetOverdueForReminderAsync(
            reminderDate: DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken);

        var processed = 0;
        foreach (var invoice in overdueInvoices)
        {
            try
            {
                await _emailService.SendInvoiceReminderAsync(invoice, cancellationToken);
                await _repo.MarkReminderSentAsync(invoice.Id, cancellationToken);
                processed++;
            }
            catch (Exception ex)
            {
                // Log și continuă — nu opri tot job-ul pentru o eroare individuală
                _logger.LogError(ex,
                    "Failed to send reminder for Invoice {InvoiceId}", invoice.Id);
            }
        }

        _logger.LogInformation(
            "{JobName} completed. Processed: {Count}", nameof(InvoiceReminderJob), processed);
    }
}
```

## 2. Fire-and-Forget Job (declanșat din handler)

```csharp
// {Module}.Infrastructure/Jobs/InvoicePdfGeneratorJob.cs
public sealed class InvoicePdfGeneratorJob
{
    private readonly IInvoiceRepository _repo;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<InvoicePdfGeneratorJob> _logger;

    public InvoicePdfGeneratorJob(
        IInvoiceRepository repo,
        IFileStorageService fileStorage,
        ILogger<InvoicePdfGeneratorJob> logger)
    {
        _repo        = repo;
        _fileStorage = fileStorage;
        _logger      = logger;
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task GenerateAsync(Guid invoiceId, Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating PDF for Invoice {InvoiceId}", invoiceId);

        var invoice = await _repo.GetWithLinesAsync(invoiceId, tenantId, cancellationToken);
        if (invoice is null)
        {
            _logger.LogWarning("Invoice {InvoiceId} not found — skipping PDF", invoiceId);
            return;   // nu arunca excepție — job-ul nu trebuie reîncercat
        }

        // Generare PDF și stocare
        var pdfBytes = PdfGenerator.Generate(invoice);
        await _fileStorage.UploadAsync(
            $"invoices/{tenantId}/{invoiceId}.pdf",
            pdfBytes,
            cancellationToken);

        _logger.LogInformation(
            "PDF generated for Invoice {InvoiceId}", invoiceId);
    }
}
```

## 3. Înregistrare în Command Handler (fire-and-forget)

```csharp
// Finance.Application/Features/Invoices/Approve/ApproveInvoiceCommandHandler.cs
internal sealed class ApproveInvoiceCommandHandler
    : IRequestHandler<ApproveInvoiceCommand, Result>
{
    private readonly IInvoiceRepository _repo;
    private readonly IBackgroundJobClient _backgroundJobs;  // Hangfire

    public ApproveInvoiceCommandHandler(
        IInvoiceRepository repo,
        IBackgroundJobClient backgroundJobs)
    {
        _repo           = repo;
        _backgroundJobs = backgroundJobs;
    }

    public async Task<Result> Handle(
        ApproveInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var invoice = await _repo.GetByIdAsync(command.InvoiceId, cancellationToken);
        if (invoice is null)
            return Result.Failure(FinanceErrors.InvoiceNotFound(command.InvoiceId));

        invoice.Approve(command.ApprovedBy);
        await _repo.UpdateAsync(invoice, cancellationToken);

        // Fire-and-forget — după commit, nu blochează răspunsul
        _backgroundJobs.Enqueue<InvoicePdfGeneratorJob>(
            j => j.GenerateAsync(invoice.Id, invoice.TenantId, CancellationToken.None));

        return Result.Success();
    }
}
```

## 4. Înregistrare Recurring Jobs în IModuleInstaller

```csharp
// Finance.Infrastructure/FinanceModule.cs
public sealed class FinanceModule : IModuleInstaller
{
    public IServiceCollection Install(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Registrare job classes în DI
        services.AddScoped<InvoiceReminderJob>();
        services.AddScoped<InvoicePdfGeneratorJob>();
        services.AddScoped<MonthlyReportJob>();

        return services;
    }

    // Recurring jobs înregistrate după ce host-ul e construit
    public static void RegisterRecurringJobs()
    {
        RecurringJob.AddOrUpdate<InvoiceReminderJob>(
            recurringJobId: "finance.invoice-reminders",
            methodCall:     j => j.SendRemindersAsync(CancellationToken.None),
            cronExpression: Cron.Daily(hour: 8),          // 08:00 UTC zilnic
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        RecurringJob.AddOrUpdate<MonthlyReportJob>(
            recurringJobId: "finance.monthly-report",
            methodCall:     j => j.GenerateAsync(CancellationToken.None),
            cronExpression: Cron.Monthly(day: 1, hour: 6)); // 1 ale lunii, 06:00 UTC
    }
}

// Program.cs — după app.Build()
FinanceModule.RegisterRecurringJobs();
HRModule.RegisterRecurringJobs();
```

## 5. Configurare Hangfire în Program.cs

```csharp
// Program.cs
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(
        builder.Configuration.GetConnectionString("Default"),  // același DB, schema hangfire
        new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout       = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout   = TimeSpan.FromMinutes(5),
            QueuePollInterval            = TimeSpan.Zero,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks           = true,
            SchemaName                   = "hangfire"          // schema dedicată
        }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
    options.Queues      = ["critical", "default", "low"];
});

// Dashboard — numai în Development sau cu autentificare
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}
```

## 6. Cron expressions — referință rapidă

```csharp
Cron.Minutely()              // la fiecare minut
Cron.Hourly()                // la fiecare oră
Cron.HourInterval(4)         // la fiecare 4 ore
Cron.Daily()                 // zilnic la miezul nopții UTC
Cron.Daily(hour: 8)          // zilnic la 08:00 UTC
Cron.Weekly(DayOfWeek.Monday, hour: 9)   // luni la 09:00 UTC
Cron.Monthly(day: 1, hour: 6)            // 1 ale lunii la 06:00 UTC
"0 8 * * 1-5"               // luni-vineri la 08:00 (cron expression custom)
```

## Reguli obligatorii

```
Job classes     — scoped în DI, nu singleton
Metode job      — async Task, niciodată void sau async void
CancellationToken — parametru default = default, Hangfire îl gestionează
AutomaticRetry  — explicit pe metode care pot eșua tranzient (max 3 attempts)
Idempotență     — job-ul poate rula de N ori fără efecte duplicate
Erori individuale — log și continuă, nu oprire tot job-ul
Fire-and-forget  — CancellationToken.None la Enqueue (tranzacția e deja comisă)
RecurringJob ID  — format "{module}.{job-name}", unic pe toată aplicația
Schema Hangfire  — "hangfire" separat de schemele business
Dashboard       — protejat cu autentificare în producție
```
