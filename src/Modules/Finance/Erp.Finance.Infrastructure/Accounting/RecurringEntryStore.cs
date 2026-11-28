using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Infrastructure.Treasury;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Finance.Infrastructure.Accounting;

/// <summary>Sabloanele de note recurente prin fin.usp_RecurringEntryTemplate_* (NET-06).</summary>
public sealed class RecurringEntryStore(IDbSession session) : IRecurringEntryStore
{
    public async Task<(int Id, byte[] RowVersion)> SaveAsync(int? id, int companyId, string name, int journalId, byte periodicity, DateOnly nextDate,
        DateOnly? endDate, string? description, string lines, byte[]? rowVersion, int userId, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<(int, byte[])>(Command("fin.usp_RecurringEntryTemplate_Save", new
        {
            Id = id, CompanyId = companyId, Name = name, JournalId = journalId, Periodicity = periodicity, NextDate = Day(nextDate),
            EndDate = endDate is { } e ? Day(e) : (DateTime?)null, Description = description, Lines = lines, RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);

    public async Task<IReadOnlyList<RecurringTemplateDto>> ListAsync(int companyId, DateOnly? dueOn, CancellationToken ct) =>
        (await session.Connection.QueryAsync<Row>(Command("fin.usp_RecurringEntryTemplate_List", new
        {
            CompanyId = companyId, DueOn = dueOn is { } d ? Day(d) : (DateTime?)null,
        }, ct)).ConfigureAwait(false))
        .Select(r => new RecurringTemplateDto(r.Id, r.Name, r.JournalId, r.JournalCode, r.Periodicity, DateOnly.FromDateTime(r.NextDate),
            r.EndDate is { } e ? DateOnly.FromDateTime(e) : null, r.Description, RecurringLines.Parse(r.Lines), r.IsActive, r.LastEntryId, r.RowVersion))
        .ToList();

    public Task SetActiveAsync(int companyId, int id, bool isActive, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_RecurringEntryTemplate_SetActive", new
        {
            CompanyId = companyId, Id = id, IsActive = isActive, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public async Task<bool> AdvanceAsync(int companyId, int id, DateOnly generatedFor, long entryId, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<int>(Command("fin.usp_RecurringEntryTemplate_Advance", new
        {
            CompanyId = companyId, Id = id, GeneratedFor = Day(generatedFor), EntryId = entryId,
        }, ct)).ConfigureAwait(false) == 1;

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record Row(int Id, string Name, int JournalId, string JournalCode, byte Periodicity, DateTime NextDate, DateTime? EndDate,
        string? Description, string Lines, bool IsActive, long? LastEntryId, byte[] RowVersion);
}

/// <summary>Generarea zilnica a ciornelor recurente scadente (C2 3.8), pe fiecare firma cu sabloane active, prin GenerateRecurringEntries.</summary>
public sealed class RecurringEntryGeneration(FinanceConnection connection, IServiceScopeFactory scopes, ILogger<RecurringEntryGeneration> log)
{
    public const string JobId = "fin-recurring-entries";

    public const string Schedule = "40 0 * * *";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var sql = new SqlConnection(connection.ConnectionString);
        var companies = (await sql.QueryAsync<int>(new CommandDefinition("fin.usp_RecurringEntryTemplate_Companies", commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false)).ToList();
        var total = 0;
        foreach (var company in companies)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ISystemContextScope>().Enter(company);
            total += await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GenerateRecurringEntries(), ct).ConfigureAwait(false);
        }

        if (total > 0)
        {
            log.LogInformation("Note recurente: {Count} ciorne generate.", total);
        }

        return total;
    }
}
