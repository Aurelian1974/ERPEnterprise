using Erp.Core.Contracts;
using FluentValidation;

namespace Erp.Core.Application.Settings;

/// <summary>Cheile setarilor instalarii (core.Setting, Scope = Installation) administrate din Configurare › Setari.</summary>
public static class SettingKeys
{
    /// <summary>Serviciul web ANAF de verificare a contribuabililor (registrele TVA, TVA la incasare, inactivi, plata defalcata, RO e-Factura).</summary>
    public const string AnafVatRegistryUrl = "Anaf.VatRegistryUrl";

    /// <summary>Versiunea sincrona v9 (instructiunile ANAF publicate pe 04.02.2025); aceeasi valoare ca in seed-ul 04_Settings.</summary>
    public const string DefaultAnafVatRegistryUrl = "https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva";
}

/// <summary>Valoarea unei setari si versiunea ei; RowVersion null = setarea nu exista inca.</summary>
public sealed record SettingValue(string? Value, byte[]? RowVersion);

/// <summary>Setarile instalarii din Configurare › Setari (macheta 07f); acum adresa serviciului ANAF.</summary>
public sealed record InstallationSettingsDto(string AnafVatRegistryUrl, string DefaultAnafVatRegistryUrl, byte[]? RowVersion);

/// <summary>Setarile instalarii prin core.usp_Setting_* (NET-06).</summary>
public interface ISettingsStore
{
    Task<SettingValue> GetInstallationAsync(string key, CancellationToken ct);

    /// <summary>Creeaza sau modifica setarea; RowVersion null doar daca setarea nu exista (altfel 50006).</summary>
    Task<byte[]> SaveInstallationAsync(string key, string? value, byte[]? rowVersion, int userId, CancellationToken ct);
}

[Module("Core")]
[RequiresPermission(CorePermissions.SettingsManage)]
public sealed record GetInstallationSettings : IQuery<InstallationSettingsDto>;

/// <summary>Adresa serviciului ANAF se schimba din pagina de setari cand ANAF publica o versiune noua, fara kit nou.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.SettingsManage)]
[Audited("Core.Settings.Save")]
public sealed record SaveInstallationSettings(string AnafVatRegistryUrl, byte[]? RowVersion) : ICommand<InstallationSettingsDto>;

internal sealed class GetInstallationSettingsHandler(ISettingsStore store) : IQueryHandler<GetInstallationSettings, InstallationSettingsDto>
{
    public async Task<InstallationSettingsDto> Handle(GetInstallationSettings request, CancellationToken cancellationToken)
    {
        var anaf = await store.GetInstallationAsync(SettingKeys.AnafVatRegistryUrl, cancellationToken).ConfigureAwait(false);
        return new InstallationSettingsDto(string.IsNullOrWhiteSpace(anaf.Value) ? SettingKeys.DefaultAnafVatRegistryUrl : anaf.Value,
            SettingKeys.DefaultAnafVatRegistryUrl, anaf.RowVersion);
    }
}

internal sealed class SaveInstallationSettingsValidator : AbstractValidator<SaveInstallationSettings>
{
    public SaveInstallationSettingsValidator()
    {
        RuleFor(x => x.AnafVatRegistryUrl).NotEmpty().MaximumLength(500)
            .Must(url => Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
            .WithMessage("Adresa serviciului ANAF este o adresă https completă, de exemplu " + SettingKeys.DefaultAnafVatRegistryUrl + ".");
    }
}

internal sealed class SaveInstallationSettingsHandler(ISettingsStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveInstallationSettings, InstallationSettingsDto>
{
    public async Task<InstallationSettingsDto> Handle(SaveInstallationSettings request, CancellationToken cancellationToken)
    {
        var url = request.AnafVatRegistryUrl.Trim();
        var rowVersion = await store.SaveInstallationAsync(SettingKeys.AnafVatRegistryUrl, url, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Core.Setting", null, new { Key = SettingKeys.AnafVatRegistryUrl, Value = url });
        return new InstallationSettingsDto(url, SettingKeys.DefaultAnafVatRegistryUrl, rowVersion);
    }
}
