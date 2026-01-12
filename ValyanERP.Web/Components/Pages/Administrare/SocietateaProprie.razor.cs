using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Navigations;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Services;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind pentru pagina Societatea Proprie.
/// </summary>
public partial class SocietateaProprie : ComponentBase
{
    // ==================== INJECTED SERVICES ====================
    
    [Inject]
    private IOrganizationService OrganizationService { get; set; } = default!;

    // ==================== TREE FIELDS ====================
    
    private SfTreeView<OrganizationTreeNode>? treeView;
    private List<OrganizationTreeNode> treeNodes = new();
    private List<OrganizationTreeNode> flatTreeNodes = new();
    private List<CompanyGroup> groups = new();
    private List<Company> companies = new();
    private List<WorkPlace> workPlaces = new();

    // ==================== SELECTION STATE ====================

    private OrganizationTreeNode? selectedNode;
    private CompanyGroup? selectedGroup;
    private Company? selectedCompany;
    private WorkPlace? selectedWorkPlace;
    private Location? selectedLocation;

    private string searchTerm = string.Empty;
    private bool isLoading = true;

    // ==================== DIALOG STATE - GROUP ====================

    private bool groupDialogVisible;
    private CompanyGroup? editingGroup;

    // ==================== DIALOG STATE - COMPANY ====================

    private bool companyDialogVisible;
    private Company? editingCompany;
    private Guid? preselectedGroupId;

    // ==================== DIALOG STATE - WORKPLACE ====================

    private bool workPlaceDialogVisible;
    private WorkPlace? editingWorkPlace;
    private Guid? preselectedCompanyId;

    // ==================== DIALOG STATE - LOCATION ====================

    private bool locationDialogVisible;
    private Location? editingLocation;
    private Guid? preselectedWorkPlaceId;

    // ==================== CONFIRM DIALOG STATE ====================

    private bool confirmDialogVisible;
    private string confirmDialogTitle = string.Empty;
    private string confirmDialogMessage = string.Empty;
    private string confirmDialogCssClass = string.Empty;
    private string confirmButtonClass = "e-danger";
    private Func<Task>? pendingConfirmAction;

    // ==================== LIFECYCLE ====================

    /// <summary>
    /// Inițializare componentă.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    // ==================== DATA LOADING ====================

    /// <summary>
    /// Încarcă toate datele necesare.
    /// </summary>
    private async Task LoadDataAsync()
    {
        isLoading = true;
        try
        {
            // Încarcă datele în paralel
            var treeTask = OrganizationService.GetOrganizationTreeAsync();
            var groupsTask = OrganizationService.GetAllGroupsAsync();
            var companiesTask = OrganizationService.GetAllCompaniesAsync();
            var workPlacesTask = OrganizationService.GetAllWorkPlacesAsync();

            await Task.WhenAll(treeTask, groupsTask, companiesTask, workPlacesTask);

            treeNodes = await treeTask;
            groups = (await groupsTask).ToList();
            companies = (await companiesTask).ToList();
            workPlaces = (await workPlacesTask).ToList();

            // Flatten pentru TreeView
            flatTreeNodes = FlattenTree(treeNodes);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Eroare la încărcarea datelor: {ex.Message}");
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// Reîncarcă structura arborelui.
    /// </summary>
    private async Task RefreshTree()
    {
        selectedNode = null;
        selectedGroup = null;
        selectedCompany = null;
        selectedWorkPlace = null;
        selectedLocation = null;
        await LoadDataAsync();
    }

    /// <summary>
    /// Flatten tree pentru Syncfusion TreeView.
    /// </summary>
    private List<OrganizationTreeNode> FlattenTree(List<OrganizationTreeNode> nodes)
    {
        var result = new List<OrganizationTreeNode>();
        foreach (var node in nodes)
        {
            result.Add(node);
            if (node.Children.Any())
            {
                result.AddRange(FlattenTree(node.Children));
            }
        }
        return result;
    }

    // ==================== TREE OPERATIONS ====================

    /// <summary>
    /// Handler pentru schimbarea căutării.
    /// </summary>
    private void OnSearchChanged(Syncfusion.Blazor.Inputs.ChangedEventArgs args)
    {
        searchTerm = args.Value ?? string.Empty;
        // TODO: Implementare filtrare tree
    }

    /// <summary>
    /// Handler pentru selecția unui nod din TreeView.
    /// </summary>
    private async Task OnNodeSelected(NodeSelectEventArgs args)
    {
        // Get node ID from TreeView selection
        var nodeId = args.NodeData?.Id;
        if (string.IsNullOrEmpty(nodeId)) return;

        // Find node in our flat list
        var node = flatTreeNodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node != null)
        {
            await SelectNode(node);
        }
    }

    /// <summary>
    /// Selectează un nod și încarcă detaliile.
    /// </summary>
    private async Task SelectNode(OrganizationTreeNode? node)
    {
        if (node == null) return;

        selectedNode = node;
        
        // Reset all selections
        selectedGroup = null;
        selectedCompany = null;
        selectedWorkPlace = null;
        selectedLocation = null;

        // Load details based on node type
        if (Guid.TryParse(node.NodeId, out var id))
        {
            try
            {
                switch (node.NodeType)
                {
                    case "GROUP":
                        selectedGroup = await OrganizationService.GetGroupByIdAsync(id);
                        break;
                    case "COMPANY":
                        selectedCompany = await OrganizationService.GetCompanyByIdAsync(id);
                        break;
                    case "WORKPLACE":
                        selectedWorkPlace = await OrganizationService.GetWorkPlaceByIdAsync(id);
                        break;
                    case "LOCATION":
                        selectedLocation = await OrganizationService.GetLocationByIdAsync(id);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Eroare la încărcarea detaliilor: {ex.Message}");
            }
        }

        StateHasChanged();
    }

    // ==================== GROUP OPERATIONS ====================

    /// <summary>
    /// Afișează dialogul pentru adăugare grup.
    /// </summary>
    private void ShowAddGroupDialog()
    {
        editingGroup = new CompanyGroup();
        groupDialogVisible = true;
    }

    /// <summary>
    /// Editează un grup.
    /// </summary>
    private void EditGroup()
    {
        if (selectedGroup != null)
        {
            editingGroup = selectedGroup;
            groupDialogVisible = true;
        }
    }

    /// <summary>
    /// Șterge un grup.
    /// </summary>
    private void DeleteGroup()
    {
        if (selectedGroup == null) return;

        confirmDialogTitle = "Ștergere Grup";
        confirmDialogMessage = $"Sunteți sigur că doriți să ștergeți grupul '{selectedGroup.Denumire}'?";
        confirmDialogCssClass = "delete-confirm";
        confirmButtonClass = "e-danger";
        pendingConfirmAction = async () =>
        {
            await OrganizationService.DeleteGroupAsync(selectedGroup.Id);
            await RefreshTree();
        };
        confirmDialogVisible = true;
    }

    /// <summary>
    /// Handler pentru salvare grup.
    /// </summary>
    private async Task OnGroupSaved(CompanyGroup group)
    {
        try
        {
            if (group.Id == Guid.Empty)
            {
                await OrganizationService.CreateGroupAsync(group);
            }
            else
            {
                await OrganizationService.UpdateGroupAsync(group);
            }
            await RefreshTree();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Eroare la salvarea grupului: {ex.Message}");
        }
    }

    // ==================== COMPANY OPERATIONS ====================

    /// <summary>
    /// Afișează dialogul pentru adăugare companie.
    /// </summary>
    private void ShowAddCompanyDialog(Guid? groupId = null)
    {
        editingCompany = new Company();
        preselectedGroupId = groupId;
        companyDialogVisible = true;
    }

    /// <summary>
    /// Editează o companie.
    /// </summary>
    private void EditCompany()
    {
        if (selectedCompany != null)
        {
            editingCompany = selectedCompany;
            preselectedGroupId = selectedCompany.GroupId;
            companyDialogVisible = true;
        }
    }

    /// <summary>
    /// Șterge o companie.
    /// </summary>
    private void DeleteCompany()
    {
        if (selectedCompany == null) return;

        confirmDialogTitle = "Ștergere Companie";
        confirmDialogMessage = $"Sunteți sigur că doriți să ștergeți compania '{selectedCompany.Denumire}'?";
        confirmDialogCssClass = "delete-confirm";
        confirmButtonClass = "e-danger";
        pendingConfirmAction = async () =>
        {
            await OrganizationService.DeleteCompanyAsync(selectedCompany.Id);
            await RefreshTree();
        };
        confirmDialogVisible = true;
    }

    /// <summary>
    /// Handler pentru salvare companie.
    /// </summary>
    private async Task OnCompanySaved(Company company)
    {
        try
        {
            if (company.Id == Guid.Empty)
            {
                await OrganizationService.CreateCompanyAsync(company);
            }
            else
            {
                await OrganizationService.UpdateCompanyAsync(company);
            }
            await RefreshTree();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Eroare la salvarea companiei: {ex.Message}");
        }
    }

    // ==================== WORKPLACE OPERATIONS ====================

    /// <summary>
    /// Afișează dialogul pentru adăugare punct de lucru.
    /// </summary>
    private void ShowAddWorkPlaceDialog(Guid? companyId)
    {
        if (!companyId.HasValue) return;
        
        editingWorkPlace = new WorkPlace { CompanyId = companyId.Value };
        preselectedCompanyId = companyId;
        workPlaceDialogVisible = true;
    }

    /// <summary>
    /// Editează un punct de lucru.
    /// </summary>
    private void EditWorkPlace()
    {
        if (selectedWorkPlace != null)
        {
            editingWorkPlace = selectedWorkPlace;
            preselectedCompanyId = selectedWorkPlace.CompanyId;
            workPlaceDialogVisible = true;
        }
    }

    /// <summary>
    /// Șterge un punct de lucru.
    /// </summary>
    private void DeleteWorkPlace()
    {
        if (selectedWorkPlace == null) return;

        confirmDialogTitle = "Ștergere Punct de Lucru";
        confirmDialogMessage = $"Sunteți sigur că doriți să ștergeți punctul de lucru '{selectedWorkPlace.Denumire}'?";
        confirmDialogCssClass = "delete-confirm";
        confirmButtonClass = "e-danger";
        pendingConfirmAction = async () =>
        {
            await OrganizationService.DeleteWorkPlaceAsync(selectedWorkPlace.Id);
            await RefreshTree();
        };
        confirmDialogVisible = true;
    }

    /// <summary>
    /// Handler pentru salvare punct de lucru.
    /// </summary>
    private async Task OnWorkPlaceSaved(WorkPlace workPlace)
    {
        try
        {
            if (workPlace.Id == Guid.Empty)
            {
                await OrganizationService.CreateWorkPlaceAsync(workPlace);
            }
            else
            {
                await OrganizationService.UpdateWorkPlaceAsync(workPlace);
            }
            await RefreshTree();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Eroare la salvarea punctului de lucru: {ex.Message}");
        }
    }

    // ==================== LOCATION OPERATIONS ====================

    /// <summary>
    /// Afișează dialogul pentru adăugare locație.
    /// </summary>
    private void ShowAddLocationDialog(Guid? companyId, Guid? workPlaceId)
    {
        if (!companyId.HasValue) return;
        
        editingLocation = new Location 
        { 
            CompanyId = companyId.Value,
            WorkPlaceId = workPlaceId
        };
        preselectedCompanyId = companyId;
        preselectedWorkPlaceId = workPlaceId;
        locationDialogVisible = true;
    }

    /// <summary>
    /// Editează o locație.
    /// </summary>
    private void EditLocation()
    {
        if (selectedLocation != null)
        {
            editingLocation = selectedLocation;
            preselectedCompanyId = selectedLocation.CompanyId;
            preselectedWorkPlaceId = selectedLocation.WorkPlaceId;
            locationDialogVisible = true;
        }
    }

    /// <summary>
    /// Șterge o locație.
    /// </summary>
    private void DeleteLocation()
    {
        if (selectedLocation == null) return;

        confirmDialogTitle = "Ștergere Locație";
        confirmDialogMessage = $"Sunteți sigur că doriți să ștergeți locația '{selectedLocation.Denumire}'?";
        confirmDialogCssClass = "delete-confirm";
        confirmButtonClass = "e-danger";
        pendingConfirmAction = async () =>
        {
            await OrganizationService.DeleteLocationAsync(selectedLocation.Id);
            await RefreshTree();
        };
        confirmDialogVisible = true;
    }

    /// <summary>
    /// Handler pentru salvare locație.
    /// </summary>
    private async Task OnLocationSaved(Location location)
    {
        try
        {
            if (location.Id == Guid.Empty)
            {
                await OrganizationService.CreateLocationAsync(location);
            }
            else
            {
                await OrganizationService.UpdateLocationAsync(location);
            }
            await RefreshTree();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Eroare la salvarea locației: {ex.Message}");
        }
    }

    // ==================== CONFIRM DIALOG ====================

    /// <summary>
    /// Handler pentru confirmarea acțiunii în dialog.
    /// </summary>
    private async Task OnConfirmDialogConfirm()
    {
        if (pendingConfirmAction != null)
        {
            await pendingConfirmAction();
            pendingConfirmAction = null;
        }
    }
}
