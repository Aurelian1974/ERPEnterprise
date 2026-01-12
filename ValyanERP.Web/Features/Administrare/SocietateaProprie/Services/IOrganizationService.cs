using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Services;

/// <summary>
/// Serviciu pentru operații pe structura organizațională.
/// </summary>
public interface IOrganizationService
{
    // ==================== GRUPURI ====================
    
    /// <summary>
    /// Obține toate grupurile de companii.
    /// </summary>
    Task<IEnumerable<CompanyGroup>> GetAllGroupsAsync(bool includeInactive = false);

    /// <summary>
    /// Obține un grup după ID.
    /// </summary>
    Task<CompanyGroup?> GetGroupByIdAsync(Guid id);

    /// <summary>
    /// Creează un grup nou.
    /// </summary>
    Task<Guid> CreateGroupAsync(CompanyGroup group, Guid? userId = null);

    /// <summary>
    /// Actualizează un grup.
    /// </summary>
    Task<bool> UpdateGroupAsync(CompanyGroup group, Guid? userId = null);

    /// <summary>
    /// Șterge un grup (soft delete).
    /// </summary>
    Task<bool> DeleteGroupAsync(Guid id, Guid? userId = null);

    // ==================== COMPANII ====================
    
    /// <summary>
    /// Obține toate companiile.
    /// </summary>
    Task<IEnumerable<Company>> GetAllCompaniesAsync(bool includeInactive = false, Guid? groupId = null);

    /// <summary>
    /// Obține o companie după ID.
    /// </summary>
    Task<Company?> GetCompanyByIdAsync(Guid id);

    /// <summary>
    /// Obține o companie după CUI.
    /// </summary>
    Task<Company?> GetCompanyByCUIAsync(string cui);

    /// <summary>
    /// Creează o companie nouă.
    /// </summary>
    Task<Guid> CreateCompanyAsync(Company company, Guid? userId = null);

    /// <summary>
    /// Actualizează o companie.
    /// </summary>
    Task<bool> UpdateCompanyAsync(Company company, Guid? userId = null);

    /// <summary>
    /// Șterge o companie (soft delete).
    /// </summary>
    Task<bool> DeleteCompanyAsync(Guid id, Guid? userId = null);

    /// <summary>
    /// Validează dacă CUI-ul este unic.
    /// </summary>
    Task<bool> IsCUIUniqueAsync(string cui, Guid? excludeId = null);

    // ==================== PUNCTE DE LUCRU ====================
    
    /// <summary>
    /// Obține toate punctele de lucru.
    /// </summary>
    Task<IEnumerable<WorkPlace>> GetAllWorkPlacesAsync(bool includeInactive = false, Guid? companyId = null);

    /// <summary>
    /// Obține un punct de lucru după ID.
    /// </summary>
    Task<WorkPlace?> GetWorkPlaceByIdAsync(Guid id);

    /// <summary>
    /// Creează un punct de lucru nou.
    /// </summary>
    Task<Guid> CreateWorkPlaceAsync(WorkPlace workPlace, Guid? userId = null);

    /// <summary>
    /// Actualizează un punct de lucru.
    /// </summary>
    Task<bool> UpdateWorkPlaceAsync(WorkPlace workPlace, Guid? userId = null);

    /// <summary>
    /// Șterge un punct de lucru (soft delete).
    /// </summary>
    Task<bool> DeleteWorkPlaceAsync(Guid id, Guid? userId = null);

    // ==================== LOCAȚII ====================
    
    /// <summary>
    /// Obține toate locațiile.
    /// </summary>
    Task<IEnumerable<Location>> GetAllLocationsAsync(bool includeInactive = false, Guid? companyId = null, Guid? workPlaceId = null);

    /// <summary>
    /// Obține o locație după ID.
    /// </summary>
    Task<Location?> GetLocationByIdAsync(Guid id);

    /// <summary>
    /// Creează o locație nouă.
    /// </summary>
    Task<Guid> CreateLocationAsync(Location location, Guid? userId = null);

    /// <summary>
    /// Actualizează o locație.
    /// </summary>
    Task<bool> UpdateLocationAsync(Location location, Guid? userId = null);

    /// <summary>
    /// Șterge o locație (soft delete).
    /// </summary>
    Task<bool> DeleteLocationAsync(Guid id, Guid? userId = null);

    /// <summary>
    /// Obține locațiile cu stoc pentru o companie.
    /// </summary>
    Task<IEnumerable<Location>> GetLocationsWithStockAsync(Guid companyId);

    /// <summary>
    /// Obține locațiile pentru vânzare pentru o companie.
    /// </summary>
    Task<IEnumerable<Location>> GetLocationsForSaleAsync(Guid companyId);

    // ==================== TREE VIEW ====================
    
    /// <summary>
    /// Obține structura ierarhică pentru TreeView.
    /// </summary>
    Task<List<OrganizationTreeNode>> GetOrganizationTreeAsync();

    /// <summary>
    /// Construiește ierarhia de noduri din lista plată.
    /// </summary>
    List<OrganizationTreeNode> BuildTreeHierarchy(IEnumerable<OrganizationTreeNode> flatNodes);
}
