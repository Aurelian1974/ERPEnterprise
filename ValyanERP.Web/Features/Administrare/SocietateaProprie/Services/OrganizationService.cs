using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Services;

/// <summary>
/// Implementare serviciu pentru structura organizațională.
/// </summary>
public class OrganizationService : IOrganizationService
{
    private readonly ICompanyGroupRepository _groupRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly IWorkPlaceRepository _workPlaceRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<OrganizationService> _logger;

    /// <summary>
    /// Constructor cu injectare dependențe.
    /// </summary>
    public OrganizationService(
        ICompanyGroupRepository groupRepository,
        ICompanyRepository companyRepository,
        IWorkPlaceRepository workPlaceRepository,
        ILocationRepository locationRepository,
        IOrganizationRepository organizationRepository,
        ILogger<OrganizationService> logger)
    {
        _groupRepository = groupRepository;
        _companyRepository = companyRepository;
        _workPlaceRepository = workPlaceRepository;
        _locationRepository = locationRepository;
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    // ==================== GRUPURI ====================

    /// <inheritdoc />
    public async Task<IEnumerable<CompanyGroup>> GetAllGroupsAsync(bool includeInactive = false)
    {
        try
        {
            return await _groupRepository.GetAllAsync(includeInactive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea grupurilor de companii");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<CompanyGroup?> GetGroupByIdAsync(Guid id)
    {
        try
        {
            return await _groupRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea grupului {GroupId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateGroupAsync(CompanyGroup group, Guid? userId = null)
    {
        try
        {
            var id = await _groupRepository.CreateAsync(group, userId);
            _logger.LogInformation("Grup creat: {GroupId} - {Denumire}", id, group.Denumire);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la crearea grupului {Denumire}", group.Denumire);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateGroupAsync(CompanyGroup group, Guid? userId = null)
    {
        try
        {
            var result = await _groupRepository.UpdateAsync(group, userId);
            if (result)
            {
                _logger.LogInformation("Grup actualizat: {GroupId}", group.Id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea grupului {GroupId}", group.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteGroupAsync(Guid id, Guid? userId = null)
    {
        try
        {
            var result = await _groupRepository.DeleteAsync(id, userId);
            if (result)
            {
                _logger.LogInformation("Grup șters: {GroupId}", id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea grupului {GroupId}", id);
            throw;
        }
    }

    // ==================== COMPANII ====================

    /// <inheritdoc />
    public async Task<IEnumerable<Company>> GetAllCompaniesAsync(bool includeInactive = false, Guid? groupId = null)
    {
        try
        {
            return await _companyRepository.GetAllAsync(includeInactive, groupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea companiilor");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Company?> GetCompanyByIdAsync(Guid id)
    {
        try
        {
            return await _companyRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea companiei {CompanyId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Company?> GetCompanyByCUIAsync(string cui)
    {
        try
        {
            return await _companyRepository.GetByCUIAsync(cui);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea companiei cu CUI {CUI}", cui);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateCompanyAsync(Company company, Guid? userId = null)
    {
        try
        {
            // Validare CUI unic
            if (await _companyRepository.ExistsByCUIAsync(company.CUI))
            {
                throw new InvalidOperationException($"CUI-ul {company.CUI} există deja în sistem.");
            }

            var id = await _companyRepository.CreateAsync(company, userId);
            _logger.LogInformation("Companie creată: {CompanyId} - {CUI} - {Denumire}", id, company.CUI, company.Denumire);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la crearea companiei {CUI}", company.CUI);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateCompanyAsync(Company company, Guid? userId = null)
    {
        try
        {
            // Validare CUI unic (exclude compania curentă)
            if (await _companyRepository.ExistsByCUIAsync(company.CUI, company.Id))
            {
                throw new InvalidOperationException($"CUI-ul {company.CUI} există deja în sistem.");
            }

            var result = await _companyRepository.UpdateAsync(company, userId);
            if (result)
            {
                _logger.LogInformation("Companie actualizată: {CompanyId}", company.Id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea companiei {CompanyId}", company.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteCompanyAsync(Guid id, Guid? userId = null)
    {
        try
        {
            var result = await _companyRepository.DeleteAsync(id, userId);
            if (result)
            {
                _logger.LogInformation("Companie ștearsă: {CompanyId}", id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea companiei {CompanyId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsCUIUniqueAsync(string cui, Guid? excludeId = null)
    {
        return !await _companyRepository.ExistsByCUIAsync(cui, excludeId);
    }

    // ==================== PUNCTE DE LUCRU ====================

    /// <inheritdoc />
    public async Task<IEnumerable<WorkPlace>> GetAllWorkPlacesAsync(bool includeInactive = false, Guid? companyId = null)
    {
        try
        {
            return await _workPlaceRepository.GetAllAsync(includeInactive, companyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea punctelor de lucru");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<WorkPlace?> GetWorkPlaceByIdAsync(Guid id)
    {
        try
        {
            return await _workPlaceRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea punctului de lucru {WorkPlaceId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateWorkPlaceAsync(WorkPlace workPlace, Guid? userId = null)
    {
        try
        {
            var id = await _workPlaceRepository.CreateAsync(workPlace, userId);
            _logger.LogInformation("Punct de lucru creat: {WorkPlaceId} - {Denumire}", id, workPlace.Denumire);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la crearea punctului de lucru {Denumire}", workPlace.Denumire);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateWorkPlaceAsync(WorkPlace workPlace, Guid? userId = null)
    {
        try
        {
            var result = await _workPlaceRepository.UpdateAsync(workPlace, userId);
            if (result)
            {
                _logger.LogInformation("Punct de lucru actualizat: {WorkPlaceId}", workPlace.Id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea punctului de lucru {WorkPlaceId}", workPlace.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteWorkPlaceAsync(Guid id, Guid? userId = null)
    {
        try
        {
            var result = await _workPlaceRepository.DeleteAsync(id, userId);
            if (result)
            {
                _logger.LogInformation("Punct de lucru șters: {WorkPlaceId}", id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea punctului de lucru {WorkPlaceId}", id);
            throw;
        }
    }

    // ==================== LOCAȚII ====================

    /// <inheritdoc />
    public async Task<IEnumerable<Location>> GetAllLocationsAsync(bool includeInactive = false, Guid? companyId = null, Guid? workPlaceId = null)
    {
        try
        {
            return await _locationRepository.GetAllAsync(includeInactive, companyId, workPlaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea locațiilor");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Location?> GetLocationByIdAsync(Guid id)
    {
        try
        {
            return await _locationRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea locației {LocationId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateLocationAsync(Location location, Guid? userId = null)
    {
        try
        {
            // Validare cod intern unic
            if (await _locationRepository.ExistsByCodInternAsync(location.CompanyId, location.CodIntern))
            {
                throw new InvalidOperationException($"Codul intern {location.CodIntern} există deja pentru această companie.");
            }

            var id = await _locationRepository.CreateAsync(location, userId);
            _logger.LogInformation("Locație creată: {LocationId} - {Denumire}", id, location.Denumire);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la crearea locației {Denumire}", location.Denumire);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateLocationAsync(Location location, Guid? userId = null)
    {
        try
        {
            // Validare cod intern unic (exclude locația curentă)
            if (await _locationRepository.ExistsByCodInternAsync(location.CompanyId, location.CodIntern, location.Id))
            {
                throw new InvalidOperationException($"Codul intern {location.CodIntern} există deja pentru această companie.");
            }

            var result = await _locationRepository.UpdateAsync(location, userId);
            if (result)
            {
                _logger.LogInformation("Locație actualizată: {LocationId}", location.Id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea locației {LocationId}", location.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteLocationAsync(Guid id, Guid? userId = null)
    {
        try
        {
            var result = await _locationRepository.DeleteAsync(id, userId);
            if (result)
            {
                _logger.LogInformation("Locație ștearsă: {LocationId}", id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea locației {LocationId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Location>> GetLocationsWithStockAsync(Guid companyId)
    {
        return await _locationRepository.GetWithStockAsync(companyId);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Location>> GetLocationsForSaleAsync(Guid companyId)
    {
        return await _locationRepository.GetForSaleAsync(companyId);
    }

    // ==================== TREE VIEW ====================

    /// <inheritdoc />
    public async Task<List<OrganizationTreeNode>> GetOrganizationTreeAsync()
    {
        try
        {
            var flatNodes = await _organizationRepository.GetTreeAsync();
            return BuildTreeHierarchy(flatNodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la obținerea arborelui organizațional");
            throw;
        }
    }

    /// <inheritdoc />
    public List<OrganizationTreeNode> BuildTreeHierarchy(IEnumerable<OrganizationTreeNode> flatNodes)
    {
        var nodeList = flatNodes.ToList();
        var nodeDict = nodeList.ToDictionary(n => n.NodeId);
        var rootNodes = new List<OrganizationTreeNode>();

        foreach (var node in nodeList)
        {
            if (string.IsNullOrEmpty(node.ParentId) || !nodeDict.ContainsKey(node.ParentId))
            {
                // Nod rădăcină
                rootNodes.Add(node);
            }
            else
            {
                // Adaugă la părinte
                var parent = nodeDict[node.ParentId];
                parent.Children.Add(node);
                parent.HasChildren = true;
            }
        }

        // Sortează copiii
        foreach (var node in nodeList)
        {
            node.Children = node.Children.OrderBy(c => c.SortOrder).ThenBy(c => c.Denumire).ToList();
        }

        return rootNodes.OrderBy(n => n.SortOrder).ThenBy(n => n.Denumire).ToList();
    }
}
