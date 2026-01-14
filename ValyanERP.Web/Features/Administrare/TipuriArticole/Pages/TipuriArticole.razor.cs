using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Syncfusion.Blazor.Grids;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ItemTypeModel = ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Adaptors;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Services;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Pages;

public partial class TipuriArticole : ComponentBase
{
    [Inject]
    public ItemTypesAdaptor Adaptor { get; set; } = null!;

    [Inject]
    public IServiceProvider ServiceProvider { get; set; } = null!;

    [Inject]
    public IItemTypesService ItemTypesService { get; set; } = null!;

    private SfGrid<ItemTypeModel>? grid;

    private bool dialogOpen = false;
    private ItemTypeModel dialogModel = new ItemTypeModel();

    private bool deleteDialogOpen = false;
    private Guid? deleteItemId = null;

    private string? successMessage;
    private string? errorMessage;
    private string? debugMessage;

    private List<ItemTypeModel> data = new();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            data = (await ItemTypesService.GetAllAsync()).ToList();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
    }

    private void ShowCreateDialog()
    {
        dialogModel = new ItemTypeModel();
        dialogOpen = true;
    }

    private void OnEdit(object? context)
    {
        if (context is ItemTypeModel it)
        {
            // clone to avoid editing grid instance until saved
            dialogModel = new ItemTypeModel
            {
                Id = it.Id,
                ItemTypeCode = it.ItemTypeCode,
                ItemTypeName = it.ItemTypeName,
                IsActive = it.IsActive
            };
            dialogOpen = true;
        }
    }

    private void OnDelete(object? context)
    {
        if (context is ItemTypeModel it)
        {
            deleteItemId = it.Id;
            deleteDialogOpen = true;
        }
    }

    private async Task OnDialogSubmit(ItemTypeModel model)
    {
        try
        {
            if (model.Id == Guid.Empty)
            {
                var id = await ItemTypesService.CreateAsync(model.ItemTypeCode, model.ItemTypeName);
                successMessage = "Tipul de articol a fost creat.";
            }
            else
            {
                await ItemTypesService.UpdateAsync(model.Id, model.ItemTypeCode, model.ItemTypeName);
                successMessage = "Tipul de articol a fost actualizat.";
            }

            errorMessage = null;
            dialogOpen = false;

            // Reload data
            data = (await ItemTypesService.GetAllAsync()).ToList();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            successMessage = null;
        }
    }

    private async Task OnDeleteConfirmed(bool confirmed)
    {
        if (!confirmed || deleteItemId == null)
        {
            deleteDialogOpen = false;
            deleteItemId = null;
            return;
        }

        try
        {
            await ItemTypesService.DeleteAsync(deleteItemId.Value);
            deleteItemId = null;
            deleteDialogOpen = false;
            successMessage = "Tipul de articol a fost șters.";
            errorMessage = null;

            // Reload data
            data = (await ItemTypesService.GetAllAsync()).ToList();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            successMessage = null;
            deleteDialogOpen = false;
        }
    }

    private async Task CheckDb()
    {
        try
        {
            var all = (await ItemTypesService.GetAllAsync()).ToList();
            debugMessage = $"Înregistrări găsite în service: {all.Count} (ex: " + (all.FirstOrDefault()?.ItemTypeCode ?? "-") + ")";
        }
        catch (Exception ex)
        {
            debugMessage = "Eroare la interogare: " + ex.Message;
        }
        StateHasChanged();
    }
}