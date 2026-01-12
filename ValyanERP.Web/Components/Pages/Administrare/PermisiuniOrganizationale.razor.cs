// ========================================================================
// PermisiuniOrganizationale.razor.cs - Code-behind
// Page for managing user organizational access permissions
// ========================================================================

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Syncfusion.Blazor.Navigations;
using System.Security.Claims;
using ValyanERP.Web.Features.Infrastructure.Security.Models.DTOs;
using ValyanERP.Web.Features.Infrastructure.Security.Repositories;
using ValyanERP.Web.Features.Administrare.Utilizatori.Repositories;

namespace ValyanERP.Web.Components.Pages.Administrare;

public partial class PermisiuniOrganizationale : ComponentBase
{
    #region Injected Services
    
    [Inject] private IUserOrganizationalAccessRepository AccessRepository { get; set; } = default!;
    [Inject] private IUsersRepository UsersRepository { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private ILogger<PermisiuniOrganizationale> Logger { get; set; } = default!;
    
    #endregion

    #region State Variables

    // User selection
    private List<UserSelectItem> allUsers = new();
    private List<UserSelectItem> filteredUsers = new();
    private string userSearchTerm = string.Empty;
    private bool isLoadingUsers = true;
    
    // Selected user
    private Guid? selectedUserId;
    private string selectedUserName = string.Empty;
    private string selectedUserEmail = string.Empty;
    
    // Organization tree
    private SfTreeView<OrganizationTreeNode>? treeView;
    private List<OrganizationTreeNode> organizationTree = new();
    private bool isLoadingTree;
    
    // User access list
    private List<UserAccessListDto> userAccessList = new();
    private UserPerimeterSummaryDto? perimeterSummary;
    
    // Add/Edit Permission Dialog
    private bool showPermissionDialog;
    private bool isAddMode;
    private string permissionDialogTitle = "Adaugă Permisiune";
    private EditingAccessModel editingAccess = new();
    private Guid? editingAccessId;
    
    // Copy Dialog
    private bool showCopyDialog;
    private Guid? copyFromUserId;
    private List<UserSelectItem> usersForCopy = new();
    
    // Delete Confirmation
    private bool showDeleteConfirm;
    private UserAccessListDto? deletingAccess;
    
    // Dropdown options
    private List<EntityTypeItem> entityTypeOptions = new();
    private List<AccessLevelItem> accessLevelOptions = new();
    private List<EntitySelectItem> availableEntities = new();

    #endregion

    #region Lifecycle

    protected override async Task OnInitializedAsync()
    {
        InitializeDropdownOptions();
        await LoadUsersAsync();
    }

    private void InitializeDropdownOptions()
    {
        entityTypeOptions = EntityTypes.All
            .Select(x => new EntityTypeItem { Value = x.Value, Name = x.Name })
            .ToList();
        
        accessLevelOptions = AccessLevels.All
            .Select(x => new AccessLevelItem { Value = x.Value, Name = x.Name })
            .ToList();
    }

    #endregion

    #region Data Loading

    private async Task LoadUsersAsync()
    {
        isLoadingUsers = true;
        StateHasChanged();
        
        try
        {
            var users = await UsersRepository.GetAllAsync();
            allUsers = users.Select(u => new UserSelectItem
            {
                Id = u.Id,
                DisplayName = !string.IsNullOrWhiteSpace(u.NumeComplet) ? u.NumeComplet : 
                              (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName)) 
                                  ? $"{u.FirstName} {u.LastName}".Trim() 
                                  : u.Email,
                Email = u.Email,
                IsAdmin = false // Will check via roles if needed
            }).OrderBy(u => u.DisplayName).ToList();
            
            FilterUsers();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading users");
        }
        finally
        {
            isLoadingUsers = false;
            StateHasChanged();
        }
    }

    private void FilterUsers()
    {
        if (string.IsNullOrWhiteSpace(userSearchTerm))
        {
            filteredUsers = allUsers.ToList();
        }
        else
        {
            var term = userSearchTerm.ToLowerInvariant();
            filteredUsers = allUsers
                .Where(u => u.DisplayName.ToLowerInvariant().Contains(term) ||
                           u.Email.ToLowerInvariant().Contains(term))
                .ToList();
        }
    }

    private async Task LoadUserDataAsync()
    {
        if (selectedUserId == null) return;
        
        isLoadingTree = true;
        StateHasChanged();
        
        try
        {
            // Load in parallel
            var treeTask = AccessRepository.GetOrganizationTreeAsync(selectedUserId);
            var accessTask = AccessRepository.GetByUserIdAsync(selectedUserId.Value);
            var summaryTask = AccessRepository.GetUserPerimeterSummaryAsync(selectedUserId.Value);
            
            await Task.WhenAll(treeTask, accessTask, summaryTask);
            
            organizationTree = (await treeTask).ToList();
            userAccessList = (await accessTask).ToList();
            perimeterSummary = await summaryTask;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading user data for {UserId}", selectedUserId);
        }
        finally
        {
            isLoadingTree = false;
            StateHasChanged();
        }
    }

    private async Task RefreshDataAsync()
    {
        await LoadUsersAsync();
        if (selectedUserId != null)
        {
            await LoadUserDataAsync();
        }
    }

    #endregion

    #region User Selection

    private async Task SelectUserAsync(Guid userId)
    {
        var user = allUsers.FirstOrDefault(u => u.Id == userId);
        if (user == null) return;
        
        selectedUserId = userId;
        selectedUserName = user.DisplayName;
        selectedUserEmail = user.Email;
        
        await LoadUserDataAsync();
    }

    private void OnUserSearchChanged(Syncfusion.Blazor.Inputs.ChangedEventArgs args)
    {
        userSearchTerm = args.Value ?? string.Empty;
        FilterUsers();
    }

    #endregion

    #region Permission Dialog

    private void OpenAddPermissionDialog()
    {
        isAddMode = true;
        permissionDialogTitle = "Adaugă Permisiune";
        editingAccess = new EditingAccessModel
        {
            AccessLevel = 1,
            InheritToChildren = true
        };
        editingAccessId = null;
        availableEntities = new List<EntitySelectItem>();
        showPermissionDialog = true;
    }

    private void AddAccessToEntityAsync(OrganizationTreeNode node)
    {
        isAddMode = true;
        permissionDialogTitle = $"Acordă Acces la {node.Denumire}";
        editingAccess = new EditingAccessModel
        {
            EntityType = node.NodeType,
            EntityId = node.Id,
            AccessLevel = 1,
            InheritToChildren = true
        };
        editingAccessId = null;
        
        // Pre-populate entity selection
        availableEntities = new List<EntitySelectItem>
        {
            new() { Id = node.Id, Name = node.Denumire }
        };
        
        showPermissionDialog = true;
    }

    private async Task EditAccessAsync(UserAccessListDto access)
    {
        isAddMode = false;
        permissionDialogTitle = "Editează Permisiune";
        editingAccessId = access.Id;
        editingAccess = new EditingAccessModel
        {
            EntityType = access.EntityType,
            EntityId = access.EntityId,
            AccessLevel = access.AccessLevel,
            InheritToChildren = access.InheritToChildren,
            ValidFrom = access.ValidFrom,
            ValidTo = access.ValidTo,
            Notes = string.Empty // Would need to load from repository
        };
        
        showPermissionDialog = true;
        await Task.CompletedTask;
    }

    private void ClosePermissionDialog()
    {
        showPermissionDialog = false;
    }

    private async Task OnEntityTypeChanged(Syncfusion.Blazor.DropDowns.ChangeEventArgs<string, EntityTypeItem> args)
    {
        if (string.IsNullOrEmpty(args.Value)) return;
        
        // Load available entities of this type
        await LoadAvailableEntitiesAsync(args.Value);
    }

    private async Task LoadAvailableEntitiesAsync(string entityType)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(
                /* Would inject connection string */);
            
            // For now, load from organization tree
            var allNodes = await AccessRepository.GetOrganizationTreeAsync(null);
            availableEntities = allNodes
                .Where(n => n.NodeType == entityType)
                .Select(n => new EntitySelectItem { Id = n.Id, Name = n.Denumire })
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading entities of type {EntityType}", entityType);
            availableEntities = new List<EntitySelectItem>();
        }
        
        StateHasChanged();
    }

    private async Task SavePermissionAsync()
    {
        if (selectedUserId == null) return;
        
        try
        {
            var currentUserId = await GetCurrentUserIdAsync();
            
            if (isAddMode)
            {
                var dto = new CreateUserAccessDto
                {
                    UserId = selectedUserId.Value,
                    EntityType = editingAccess.EntityType,
                    EntityId = editingAccess.EntityId,
                    AccessLevel = editingAccess.AccessLevel,
                    InheritToChildren = editingAccess.InheritToChildren,
                    ValidFrom = editingAccess.ValidFrom,
                    ValidTo = editingAccess.ValidTo,
                    Notes = editingAccess.Notes
                };
                
                await AccessRepository.CreateAsync(dto, currentUserId);
                Logger.LogInformation("Created access permission for user {UserId}", selectedUserId);
            }
            else if (editingAccessId.HasValue)
            {
                var dto = new UpdateUserAccessDto
                {
                    Id = editingAccessId.Value,
                    AccessLevel = editingAccess.AccessLevel,
                    InheritToChildren = editingAccess.InheritToChildren,
                    ValidFrom = editingAccess.ValidFrom,
                    ValidTo = editingAccess.ValidTo,
                    Notes = editingAccess.Notes
                };
                
                await AccessRepository.UpdateAsync(dto, currentUserId);
                Logger.LogInformation("Updated access permission {Id}", editingAccessId);
            }
            
            showPermissionDialog = false;
            await LoadUserDataAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving permission");
            // TODO: Show error notification
        }
    }

    #endregion

    #region Copy Permissions

    private void OpenCopyPermissionsDialog()
    {
        usersForCopy = allUsers
            .Where(u => u.Id != selectedUserId)
            .ToList();
        copyFromUserId = null;
        showCopyDialog = true;
    }

    private void CloseCopyDialog()
    {
        showCopyDialog = false;
    }

    private async Task CopyPermissionsAsync()
    {
        if (selectedUserId == null || copyFromUserId == null) return;
        
        try
        {
            var currentUserId = await GetCurrentUserIdAsync();
            
            var count = await AccessRepository.CopyFromUserAsync(
                copyFromUserId.Value, 
                selectedUserId.Value, 
                currentUserId);
            
            Logger.LogInformation(
                "Copied {Count} permissions from {SourceUserId} to {TargetUserId}",
                count, copyFromUserId, selectedUserId);
            
            showCopyDialog = false;
            await LoadUserDataAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error copying permissions");
        }
    }

    #endregion

    #region Delete Permission

    private async Task RemoveAccessAsync(OrganizationTreeNode node)
    {
        if (node.DirectAccessId == null) return;
        
        deletingAccess = new UserAccessListDto
        {
            Id = node.DirectAccessId.Value,
            EntityType = node.NodeType,
            EntityName = node.Denumire
        };
        showDeleteConfirm = true;
        await Task.CompletedTask;
    }

    private async Task DeleteAccessAsync(UserAccessListDto access)
    {
        deletingAccess = access;
        showDeleteConfirm = true;
        await Task.CompletedTask;
    }

    private void CloseDeleteConfirm()
    {
        showDeleteConfirm = false;
        deletingAccess = null;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (deletingAccess == null) return;
        
        try
        {
            var currentUserId = await GetCurrentUserIdAsync();
            
            await AccessRepository.DeleteAsync(deletingAccess.Id, currentUserId);
            Logger.LogInformation("Revoked access permission {Id}", deletingAccess.Id);
            
            showDeleteConfirm = false;
            deletingAccess = null;
            await LoadUserDataAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error revoking permission");
        }
    }

    #endregion

    #region Helpers

    private async Task<Guid> GetCurrentUserIdAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var userIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier);
        
        if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        
        return Guid.Empty;
    }

    #endregion

    #region Helper Models

    public class UserSelectItem
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
    }

    public class EntityTypeItem
    {
        public string Value { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class AccessLevelItem
    {
        public byte Value { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class EntitySelectItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class EditingAccessModel
    {
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public byte AccessLevel { get; set; }
        public bool InheritToChildren { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string? Notes { get; set; }
    }

    #endregion
}
