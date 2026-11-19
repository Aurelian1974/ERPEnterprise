using System.Globalization;
using Erp.Core.Contracts;
using Erp.Licensing;

namespace Erp.Core.Application.Licensing;

/// <summary>Licenta activa si data de referinta (08, 4.4), din cache-ul aplicatiei.</summary>
public interface ILicenseSnapshot
{
    License? Current { get; }

    byte KeyId { get; }

    /// <summary>Cea mai mare dintre data curenta si cea mai mare data vazuta de aplicatie (08, 4.4).</summary>
    DateOnly ReferenceDate { get; }
}

/// <summary>Cererea de reincarcare a licentei dupa o modificare (08, 4.3 pasul 2).</summary>
public interface ILicenseRefresh
{
    void RequestRefresh();
}

/// <summary>Un rand din istoricul licentelor (08, 4.1).</summary>
public sealed record LicenseHistoryItem(string LicenseId, string KeyId, string CustomerName, int Companies, DateOnly IssuedAt,
    DateOnly? ValidUntil, DateOnly? UpdatesUntil, bool IsCurrent, DateTime LoadedAt);

public interface ILicenseStore
{
    /// <summary>Salveaza licenta ca activa, in tranzactia cererii (core.usp_InstallationLicense_Save); intoarce Id-ul randului.</summary>
    Task<int> SaveAsync(byte[] file, License license, byte keyId, int userId, CancellationToken ct);

    Task<IReadOnlyList<LicenseHistoryItem>> ListAsync(CancellationToken ct);
}

/// <param name="FiscalCodes">CUI-urile, cu prefixul RO doar la afisare (08, 4.1).</param>
/// <param name="State">Valid, ExpiringSoon, Grace sau ReadOnly (02.01, 4.4).</param>
/// <param name="ClockBehind">Ceasul serverului e in urma celei mai mari date vazute (08, 4.4).</param>
public sealed record LicenseDto(bool HasLicense, string? LicenseId, string? KeyId, string? CustomerName, IReadOnlyList<string> FiscalCodes,
    IReadOnlyList<string> Modules, int Users, int PayrollEmployees, int PosTerminals, DateOnly? IssuedAt, DateOnly? ValidUntil,
    DateOnly? UpdatesUntil, string State, bool ClockBehind, IReadOnlyList<LicenseHistoryItem> History);

/// <summary>Administrare › Configurare › Licenta (08, 4.1): licenta activa si istoricul.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.LicenseManage)]
public sealed record GetLicense : IQuery<LicenseDto>;

/// <summary>
/// Incarcarea unei licente noi (08, 4.1): fisierul .lic in Base64. Aceleasi verificari ca la instalare (08, 4.2, 1–5), plus
/// verificarea 6 (o licenta mai veche decat cea activa e refuzata). Permisa si in modul doar-citire: e iesirea din el.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.LicenseManage)]
[Audited("Core.License.Upload")]
[AllowedWhenLicenseReadOnly]
public sealed record UploadLicense(string FileBase64) : ICommand<LicenseDto>;

internal static class LicenseViews
{
    /// <summary>Un fisier .lic are cateva sute de octeti; limita protejeaza serverul de incarcari mari.</summary>
    public const int MaxFileBytes = 128 * 1024;

    public static LicenseDto From(License? license, byte keyId, ILicenseInfo info, IReadOnlyList<LicenseHistoryItem> history) => license is null
        ? new LicenseDto(false, null, null, null, [], [], 0, 0, 0, null, null, null, info.State.ToString(), info.ClockBehind, history)
        : new LicenseDto(true, license.LicenseId, keyId.ToString(CultureInfo.InvariantCulture), license.CustomerName,
            [.. license.FiscalCodeList.Select(c => "RO" + c)], [.. license.Modules], license.Limits.Users, license.Limits.PayrollEmployees,
            license.Limits.PosTerminals, license.IssuedAt, license.ValidUntil, license.UpdatesUntil, info.State.ToString(), info.ClockBehind, history);
}

internal sealed class GetLicenseHandler(ILicenseSnapshot snapshot, ILicenseInfo info, ILicenseStore store) : IQueryHandler<GetLicense, LicenseDto>
{
    public async Task<LicenseDto> Handle(GetLicense request, CancellationToken cancellationToken) =>
        LicenseViews.From(snapshot.Current, snapshot.KeyId, info, await store.ListAsync(cancellationToken).ConfigureAwait(false));
}

internal sealed class UploadLicenseHandler(ILicenseReader reader, ILicenseSnapshot snapshot, ILicenseInfo info, ILicenseStore store,
    ILicenseRefresh refresh, ICurrentContext context, IAuditScope audit) : ICommandHandler<UploadLicense, LicenseDto>
{
    public async Task<LicenseDto> Handle(UploadLicense request, CancellationToken cancellationToken)
    {
        byte[] file;
        try
        {
            file = Convert.FromBase64String(request.FileBase64);
        }
        catch (FormatException)
        {
            throw Invalid("Fișierul nu este o licență ERP.");
        }

        if (file.Length > LicenseViews.MaxFileBytes)
        {
            throw Invalid("Fișierul nu este o licență ERP.");
        }

        var result = reader.Read(file, snapshot.ReferenceDate);
        if (!result.IsValid)
        {
            throw Invalid(result.Message);
        }

        var license = result.License!;
        if (LicensePolicy.CheckReplacement(snapshot.Current, license) is { } older)
        {
            throw ErpException.Conflict(CoreErrorCodes.LicenseOlder, older);
        }

        var id = await store.SaveAsync(file, license, result.KeyId, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.InstallationLicense", id, new
        {
            license.LicenseId, license.CustomerName, license.Companies, license.Modules, license.ValidUntil, license.UpdatesUntil, result.KeyId,
        });
        refresh.RequestRefresh();
        return LicenseViews.From(license, result.KeyId, info, await store.ListAsync(cancellationToken).ConfigureAwait(false)) with
        {
            State = LicenseState.Valid.ToString(),
        };
    }

    private static ErpValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { [FileField] = [message] });

    /// <summary>Numele campului ca in JSON-ul API (camelCase), ca la erorile validatorilor.</summary>
    public const string FileField = "fileBase64";
}
