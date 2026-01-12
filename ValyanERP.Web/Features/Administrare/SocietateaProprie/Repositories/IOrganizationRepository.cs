using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Repository pentru operații pe structura organizațională (tree).
/// </summary>
public interface IOrganizationRepository
{
    /// <summary>
    /// Obține structura ierarhică completă pentru TreeView.
    /// </summary>
    /// <returns>Lista de noduri pentru arborele organizațional.</returns>
    Task<IEnumerable<OrganizationTreeNode>> GetTreeAsync();
}
