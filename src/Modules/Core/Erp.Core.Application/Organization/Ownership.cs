using Erp.Core.Contracts;

namespace Erp.Core.Application.Organization;

/// <summary>
/// Proprietarul unui articol sau partener (02.01, 3.2-3.4; deciziile lui Aurelian din 21.11.2026): 0 comun grupului, 1 firma curenta,
/// 2 un punct de lucru al ei, 3 o locatie a ei (NodeId = punctul de lucru sau locatia).
/// </summary>
public static class OwnerLevels
{
    public const byte Common = 0;
    public const byte Company = 1;
    public const byte Branch = 2;
    public const byte Location = 3;
}

/// <summary>Proprietarul in forma tabelelor: toate null = comun; firma, cu punctul de lucru sau locatia ei.</summary>
public sealed record OwnerRef(int? CompanyId, int? BranchId, int? LocationId)
{
    public static readonly OwnerRef Common = new(null, null, null);

    /// <summary>Nivelul (OwnerLevels) si nodul din coloanele proprietarului.</summary>
    public static (byte Level, int? NodeId) LevelOf(int? companyId, int? branchId, int? locationId) =>
        locationId is not null ? (OwnerLevels.Location, locationId)
        : branchId is not null ? (OwnerLevels.Branch, branchId)
        : companyId is not null ? (OwnerLevels.Company, null)
        : (OwnerLevels.Common, null);

    public static OwnerRef From(byte level, int? nodeId, int companyId) => level switch
    {
        OwnerLevels.Company => new OwnerRef(companyId, null, null),
        OwnerLevels.Branch when nodeId is not null => new OwnerRef(companyId, nodeId, null),
        OwnerLevels.Location when nodeId is not null => new OwnerRef(companyId, null, nodeId),
        OwnerLevels.Common => Common,
        _ => throw new ErpValidationException(new Dictionary<string, string[]>
        {
            ["ownerNodeId"] = ["Alegeți punctul de lucru sau locația care deține înregistrarea."],
        }),
    };
}

/// <summary>Un nod al firmei curente in alegerea proprietarului: punct de lucru (BranchId) sau locatie (LocationId, cu ParentBranchId).</summary>
public sealed record OwnerNodeDto(int? BranchId, int? LocationId, int? ParentBranchId, string Code, string Name, bool IsActive, bool CanView, bool CanManage);

/// <summary>
/// Proprietarii posibili in firma curenta: dreptul pe toata firma (vizualizare / modificare) si nodurile ei, cu CanView (filtrul listei)
/// si CanManage (alegerea proprietarului la adaugare sau modificare).
/// </summary>
public sealed record OwnerOptionsDto(int CompanyId, string CompanyName, bool ViewWholeCompany, bool ManageWholeCompany, IReadOnlyList<OwnerNodeDto> Nodes);

/// <summary>Verificarea accesului pe proprietar, in SQL (core.usp_OwnerAccess_Check), pentru utilizatorul si firma cererii.</summary>
public interface IOwnerAccess
{
    /// <summary>Inregistrarea exista si e vizibila; altfel 404, ca si cum n-ar exista.</summary>
    Task EnsureVisibleAsync(string entityType, int id, string viewPermission, CancellationToken ct);

    /// <summary>Inregistrarea existenta (daca e data) si proprietarul nou (daca e dat) sunt in nodurile pe care utilizatorul are dreptul.</summary>
    Task EnsureManageAsync(string entityType, int? id, OwnerRef? newOwner, string managePermission, CancellationToken ct);

    Task<OwnerOptionsDto?> GetOptionsAsync(string viewPermission, string managePermission, CancellationToken ct);
}

public static class OwnedEntities
{
    public const string Item = "Item";
    public const string Partner = "Partner";
}

/// <summary>
/// Proprietarii posibili pentru articole sau parteneri (Entity = Item / Partner), in firma curenta. Permisiunea depinde de entitate,
/// deci o verifica handler-ul (Core.Item.View / Core.Partner.View).
/// </summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetOwnerOptions(string Entity) : IQuery<OwnerOptionsDto?>;

internal sealed class GetOwnerOptionsHandler(IOwnerAccess owners, ICurrentContext context) : IQueryHandler<GetOwnerOptions, OwnerOptionsDto?>
{
    public Task<OwnerOptionsDto?> Handle(GetOwnerOptions request, CancellationToken cancellationToken)
    {
        var (view, manage) = request.Entity switch
        {
            OwnedEntities.Item => (CorePermissions.ItemView, CorePermissions.ItemManage),
            OwnedEntities.Partner => (CorePermissions.PartnerView, CorePermissions.PartnerManage),
            _ => throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Tip de entitate necunoscut."),
        };
        if (!context.HasPermission(view))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Nu aveți dreptul necesar.");
        }

        return owners.GetOptionsAsync(view, manage, cancellationToken);
    }
}
