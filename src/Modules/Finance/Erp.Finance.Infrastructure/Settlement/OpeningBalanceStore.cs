using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Importul soldurilor initiale prin fin.usp_OpeningBalance_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class OpeningBalanceStore(IDbSession session) : IOpeningBalanceStore
{
    public async Task<(IReadOnlyDictionary<int, (int? PartnerId, bool Exists)> Rows, IReadOnlySet<string> Currencies)> LookupAsync(int companyId,
        IReadOnlyList<(int Row, string Partner, PartnerRole Role, string LineKey)> keys, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(keys.Select(k => new { row = k.Row, partner = k.Partner, role = (byte)k.Role, key = k.LineKey }));
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("fin.usp_OpeningBalance_Lookup", new { CompanyId = companyId, Keys = json },
            session.Transaction, commandTimeout: 300, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<(int RowNumber, int? PartnerId, bool AlreadyImported)>().ConfigureAwait(false))
            .ToDictionary(r => r.RowNumber, r => (r.PartnerId, r.AlreadyImported));
        var currencies = (await grid.ReadAsync<string>().ConfigureAwait(false)).Select(c => c.Trim()).ToHashSet(StringComparer.Ordinal);
        return (rows, currencies);
    }

    public async Task<int> ImportAsync(int companyId, IReadOnlyList<OpeningItemRow> rows, int userId, CancellationToken ct)
    {
        using var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("RowNumber", typeof(int)), ("PartnerId", typeof(int)), ("Role", typeof(byte)), ("Kind", typeof(byte)), ("LineKey", typeof(string)),
            ("DocumentNo", typeof(string)), ("DocumentDate", typeof(DateTime)), ("DueDate", typeof(DateTime)), ("CurrencyCode", typeof(string)),
            ("ExchangeRate", typeof(decimal)), ("Amount", typeof(decimal)), ("AmountRon", typeof(decimal)), ("VatAmountRon", typeof(decimal)),
            ("IsVatOnCollection", typeof(bool)), ("GlAccountCode", typeof(string)),
        })
        {
            table.Columns.Add(name, type);
        }

        foreach (var r in rows)
        {
            var gl = r.Kind == OpenItemKind.OpeningBalance ? SettlementRules.InvoiceGlAccount(r.Role == PartnerRole.Customer) : null;
            table.Rows.Add(r.RowNumber, r.PartnerId, (byte)r.Role, (byte)r.Kind, r.LineKey, r.DocumentNo, r.DocumentDate.ToDateTime(TimeOnly.MinValue),
                r.DueDate.ToDateTime(TimeOnly.MinValue), r.CurrencyCode, r.ExchangeRate, r.Amount, r.AmountRon, r.VatAmountRon, r.IsVatOnCollection,
                (object?)gl ?? DBNull.Value);
        }

        return await session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("fin.usp_OpeningBalance_Import",
            new { CompanyId = companyId, Rows = table.AsTableValuedParameter("fin.OpeningItemList"), UserId = userId }, session.Transaction, commandTimeout: 300,
            commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
    }
}
