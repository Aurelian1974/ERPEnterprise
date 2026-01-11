using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Syncfusion.Blazor.DropDowns;
using System.Text.Json;

namespace ValyanERP.Web.Components.Shared.DataGrid;

/// <summary>
/// Code-behind for GridStateManager component.
/// Manages SfGrid state persistence with save/load/reset functionality.
/// ⚠️ ALL LOGIC here (code-behind), .razor only contains markup.
/// </summary>
public partial class GridStateManager : ComponentBase
{
    #region Injected Services

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    #endregion

    #region Parameters

    /// <summary>
    /// The unique identifier for this grid (used as storage key).
    /// </summary>
    [Parameter, EditorRequired]
    public string GridId { get; set; } = string.Empty;

    /// <summary>
    /// The SfGrid reference to manage state for.
    /// </summary>
    [Parameter, EditorRequired]
    public object? GridReference { get; set; }

    /// <summary>
    /// Whether to show the configuration selector dropdown.
    /// </summary>
    [Parameter]
    public bool ShowConfigurationSelector { get; set; } = true;

    /// <summary>
    /// Whether to show button labels or just icons.
    /// </summary>
    [Parameter]
    public bool ShowButtonLabels { get; set; } = false;

    /// <summary>
    /// Whether to show the export button.
    /// </summary>
    [Parameter]
    public bool ShowExportButton { get; set; } = false;

    #endregion

    #region Event Callbacks

    /// <summary>
    /// Callback when configuration is saved.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnStateSaved { get; set; }

    /// <summary>
    /// Callback when configuration is loaded.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnStateLoaded { get; set; }

    /// <summary>
    /// Callback when configuration is reset.
    /// </summary>
    [Parameter]
    public EventCallback OnStateReset { get; set; }

    #endregion

    #region Private Fields

    private List<GridConfiguration> configurations = new();
    private string selectedConfigName = string.Empty;
    private bool showSaveDialog = false;
    private string newConfigName = string.Empty;
    private bool setAsDefault = false;

    #endregion

    #region Lifecycle Methods

    /// <summary>
    /// Initializes the component and loads saved configurations.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadConfigurations();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads saved configurations from localStorage.
    /// In production, this should load from database via service.
    /// </summary>
    private async Task LoadConfigurations()
    {
        try
        {
            // Load from localStorage - in production, load from DB via service
            var storedConfigs = await JS.InvokeAsync<string>("localStorage.getItem", $"grid_configs_{GridId}");
            if (!string.IsNullOrEmpty(storedConfigs))
            {
                configurations = JsonSerializer.Deserialize<List<GridConfiguration>>(storedConfigs)
                                 ?? new List<GridConfiguration>();
            }

            // Add default configuration if empty
            if (!configurations.Any())
            {
                configurations.Add(new GridConfiguration { Name = "Implicit", IsDefault = true });
            }

            // Select default configuration
            var defaultConfig = configurations.FirstOrDefault(c => c.IsDefault) ?? configurations.First();
            selectedConfigName = defaultConfig.Name;
        }
        catch
        {
            configurations = new List<GridConfiguration>
            {
                new GridConfiguration { Name = "Implicit", IsDefault = true }
            };
            selectedConfigName = "Implicit";
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles configuration selection from dropdown.
    /// </summary>
    public async Task OnConfigurationSelected(ChangeEventArgs<string, GridConfiguration> args)
    {
        if (args.ItemData != null && GridReference != null)
        {
            var config = args.ItemData;
            if (!string.IsNullOrEmpty(config.GridState))
            {
                // Use dynamic to access SetPersistDataAsync
                dynamic grid = GridReference;
                await grid.SetPersistDataAsync(config.GridState);
                await OnStateLoaded.InvokeAsync(config.Name);
            }
        }
    }

    /// <summary>
    /// Opens the save configuration dialog.
    /// </summary>
    public void OnSaveConfiguration()
    {
        newConfigName = string.Empty;
        setAsDefault = false;
        showSaveDialog = true;
    }

    /// <summary>
    /// Saves the current grid state as a configuration.
    /// </summary>
    public async Task SaveConfiguration()
    {
        if (string.IsNullOrWhiteSpace(newConfigName) || GridReference == null)
        {
            showSaveDialog = false;
            return;
        }

        try
        {
            // Get current grid state
            dynamic grid = GridReference;
            string gridState = await grid.GetPersistDataAsync();

            // Update or add configuration
            var existingConfig = configurations.FirstOrDefault(c => c.Name == newConfigName);
            if (existingConfig != null)
            {
                existingConfig.GridState = gridState;
                existingConfig.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                configurations.Add(new GridConfiguration
                {
                    Name = newConfigName,
                    GridState = gridState,
                    IsDefault = setAsDefault,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            // Set as default if requested
            if (setAsDefault)
            {
                foreach (var config in configurations)
                {
                    config.IsDefault = config.Name == newConfigName;
                }
            }

            // Save to localStorage - in production, save to DB via service
            var json = JsonSerializer.Serialize(configurations);
            await JS.InvokeVoidAsync("localStorage.setItem", $"grid_configs_{GridId}", json);

            selectedConfigName = newConfigName;
            await OnStateSaved.InvokeAsync(newConfigName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving configuration: {ex.Message}");
        }

        showSaveDialog = false;
    }

    /// <summary>
    /// Resets the grid to default configuration.
    /// </summary>
    public async Task OnResetConfiguration()
    {
        if (GridReference != null)
        {
            try
            {
                dynamic grid = GridReference;
                await grid.ResetPersistDataAsync();
                await OnStateReset.InvokeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resetting configuration: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Exports the current grid configuration as a JSON file.
    /// </summary>
    public async Task OnExportConfiguration()
    {
        if (GridReference != null)
        {
            try
            {
                dynamic grid = GridReference;
                string gridState = await grid.GetPersistDataAsync();

                // Download as JSON file
                await JS.InvokeVoidAsync("eval", $@"
                    var blob = new Blob(['{gridState.Replace("'", "\\'")}'], {{type: 'application/json'}});
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = '{GridId}_config.json';
                    a.click();
                    URL.revokeObjectURL(url);
                ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting configuration: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Closes the save dialog.
    /// </summary>
    public void CloseSaveDialog()
    {
        showSaveDialog = false;
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Grid configuration model for persistence.
    /// </summary>
    public class GridConfiguration
    {
        public string Name { get; set; } = string.Empty;
        public string? GridState { get; set; }
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion
}
