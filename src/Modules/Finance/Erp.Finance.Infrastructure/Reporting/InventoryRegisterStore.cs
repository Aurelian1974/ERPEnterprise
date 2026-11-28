using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Reporting;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Reporting;

/// <summary>Registrul inventar prin fin.usp_InventoryRegister_Save, _Get, _Names.</summary>
public sealed class InventoryRegisterStore(IDbSession session) : IInventoryRegisterStore
{
    public async Task<IReadOnlyDictionary<int, string>> PartnerNamesAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int Id, string Name)>(Command("fin.usp_InventoryRegister_Names", new { Partners = JsonSerializer.Serialize(ids) }, ct))
            .ConfigureAwait(false)).ToDictionary(x => x.Id, x => x.Name);

    public Task<int> SaveAsync(int companyId, int year, IReadOnlyList<InventoryLine> lines, byte[] pdf, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_InventoryRegister_Save", new
        {
            CompanyId = companyId, Year = (short)year, Pdf = pdf, UserId = userId,
            Lines = JsonSerializer.Serialize(lines.Select(l => new
            {
                accountCode = l.AccountCode, accountName = l.AccountName, partnerId = l.PartnerId, partnerName = l.PartnerName, side = l.Side, bookValue = l.BookValue,
                inventoryValue = l.InventoryValue,
            })),
        }, ct));

    public async Task<(InventoryRegisterDto Register, byte[]? Pdf)?> GetAsync(int companyId, int year, bool withPdf, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_InventoryRegister_Get", new { CompanyId = companyId, Year = (short)year, WithPdf = withPdf },
            ct)).ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<Header>().ConfigureAwait(false);
        var lines = (await grid.ReadAsync<(string AccountCode, string AccountName, int? PartnerId, string? PartnerName, byte Side, decimal BookValue, decimal InventoryValue,
            decimal Difference)>().ConfigureAwait(false)).Select(l => new InventoryLineDto(l.AccountCode, l.AccountName, l.PartnerId, l.PartnerName, l.Side,
            Money.Format(l.BookValue), Money.Format(l.InventoryValue), Money.Format(l.Difference))).ToList();
        return h is null ? null : (new InventoryRegisterDto(h.Id, h.Year, h.IsFinal, h.GeneratedAt, lines), h.Pdf);
    }

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record Header(int Id, short Year, bool IsFinal, DateTime GeneratedAt, byte[]? Pdf);
}
