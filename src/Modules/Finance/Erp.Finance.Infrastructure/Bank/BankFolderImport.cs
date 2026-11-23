using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain.Bank;
using Erp.Finance.Infrastructure.Treasury;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Finance.Infrastructure.Bank;

/// <summary>Rezultatul unei treceri: fisierele importate, cele mutate in „erori” si cele lasate pentru trecerea urmatoare.</summary>
public sealed record BankFolderImportResult(int Imported, int Failed, int Postponed);

/// <summary>
/// Importul automat al extraselor din folder (02.02, F2 4.1 pasul 1): la 15 minute, pentru fiecare cont cu folder de import, fisierele noi
/// (cele scrise de cel putin un minut, ca sa nu fie luate pe jumatate copiate) trec prin aceeasi comanda ca importul din ecran
/// (ImportBankStatement: verificari, tranzactie, audit, outbox), in numele utilizatorului de sistem si in firma contului, apoi prin potrivirea
/// automata. Fisierul importat se muta in subfolderul „importate”; cel refuzat (format, sold, dublura...) in „erori”, cu motivul in
/// „&lt;fisier&gt;.eroare.txt”. O eroare tehnica (baza indisponibila) lasa fisierul pe loc pentru trecerea urmatoare.
/// </summary>
public sealed class BankFolderImport(IServiceScopeFactory scopes, FinanceConnection connection, TimeProvider clock, ILogger<BankFolderImport> log)
{
    public const string JobId = "fin-bank-folder-import";

    /// <summary>La fiecare 15 minute.</summary>
    public const string Schedule = "*/15 * * * *";

    public const string ImportedFolder = "importate";
    public const string ErrorFolder = "erori";

    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(1);

    public async Task<BankFolderImportResult> RunAsync(CancellationToken ct)
    {
        List<Account> accounts;
        await using (var sql = new SqlConnection(connection.ConnectionString))
        {
            accounts = (await sql.QueryAsync<Account>(new CommandDefinition("fin.usp_BankFolderImport_Accounts", commandType: CommandType.StoredProcedure,
                cancellationToken: ct)).ConfigureAwait(false)).ToList();
        }

        int imported = 0, failed = 0, postponed = 0;
        foreach (var account in accounts)
        {
            if (!Directory.Exists(account.ImportFolder))
            {
                log.LogWarning("Folderul de import al contului {Account} nu există: {Folder}", account.Code, account.ImportFolder);
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(account.ImportFolder).Order(StringComparer.OrdinalIgnoreCase).ToList())
            {
                var file = new FileInfo(path);
                if (file.Name.StartsWith('.') || file.Name.StartsWith("~$", StringComparison.Ordinal) || file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    || file.Name.EndsWith(".eroare.txt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (clock.GetUtcNow().UtcDateTime - file.LastWriteTimeUtc < Settle)
                {
                    postponed++;
                    continue;
                }

                switch (await ImportAsync(account, file, ct).ConfigureAwait(false))
                {
                    case Outcome.Imported:
                        imported++;
                        break;
                    case Outcome.Failed:
                        failed++;
                        break;
                    default:
                        postponed++;
                        break;
                }
            }
        }

        return new BankFolderImportResult(imported, failed, postponed);
    }

    private async Task<Outcome> ImportAsync(Account account, FileInfo file, CancellationToken ct)
    {
        if (file.Length > BankStatementRules.MaxFileBytes)
        {
            Move(file, ErrorFolder, "Fișierul depășește 20 MB.");
            return Outcome.Failed;
        }

        BankImportResultDto result;
        try
        {
            var content = await File.ReadAllBytesAsync(file.FullName, ct).ConfigureAwait(false);
            result = await SendAsync(account.CompanyId, new ImportBankStatement(account.Id, file.Name, Convert.ToBase64String(content)), ct).ConfigureAwait(false);
        }
        catch (ErpException ex)
        {
            Move(file, ErrorFolder, $"{ex.Code}: {ex.Message}");
            log.LogWarning("Extrasul {File} al contului {Account} a fost refuzat: {Code} {Message}", file.Name, account.Code, ex.Code, ex.Message);
            return Outcome.Failed;
        }
        catch (Exception ex) when (ex is IOException or SqlException or TimeoutException)
        {
            log.LogError(ex, "Extrasul {File} al contului {Account} rămâne pentru trecerea următoare.", file.Name, account.Code);
            return Outcome.Postponed;
        }

        Move(file, ImportedFolder, null);
        foreach (var statement in result.Statements)
        {
            try
            {
                await SendAsync(account.CompanyId, new RunAutoMatching(statement.BankStatementId), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Extrasul e importat; potrivirea se poate rula din ecranul Extras de cont.
                log.LogWarning(ex, "Potrivirea automată a extrasului {Statement} a eșuat.", statement.BankStatementId);
            }
        }

        log.LogInformation("Extrasul {File} a fost importat în contul {Account}: {Count} extrase.", file.Name, account.Code, result.Statements.Count);
        return Outcome.Imported;
    }

    private async Task<T> SendAsync<T>(int companyId, IRequest<T> request, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISystemContextScope>().Enter(companyId);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, ct).ConfigureAwait(false);
    }

    /// <summary>Muta fisierul in subfolder, cu un nume nou daca exista deja; motivul unui refuz se scrie langa el.</summary>
    private void Move(FileInfo file, string folder, string? reason)
    {
        var target = Path.Combine(file.DirectoryName!, folder);
        Directory.CreateDirectory(target);
        var destination = Path.Combine(target, file.Name);
        if (File.Exists(destination))
        {
            destination = Path.Combine(target,
                $"{Path.GetFileNameWithoutExtension(file.Name)}.{clock.GetUtcNow():yyyyMMddHHmmss}{Path.GetExtension(file.Name)}");
        }

        file.MoveTo(destination);
        if (reason is not null)
        {
            File.WriteAllText(destination + ".eroare.txt", reason);
        }
    }

    private enum Outcome
    {
        Imported,
        Failed,
        Postponed,
    }

    private sealed record Account(int Id, int CompanyId, string Code, string ImportFolder);
}
