using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Repositories;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Services;

/// <summary>
/// Service interface for ItemTypes business logic.
/// </summary>
public interface IItemTypesService
{
    /// <summary>
    /// Gets all item types for export purposes.
    /// </summary>
    Task<IEnumerable<ItemType>> GetAllItemTypesAsync();

    /// <summary>
    /// Creates a new item type.
    /// </summary>
    Task<bool> CreateItemTypeAsync(ItemTypeCreateDto itemTypeDto);

    /// <summary>
    /// Updates an existing item type.
    /// </summary>
    Task<bool> UpdateItemTypeAsync(ItemTypeUpdateDto itemTypeDto);

    /// <summary>
    /// Soft deletes an item type.
    /// </summary>
    Task<bool> DeleteItemTypeAsync(Guid id);

    /// <summary>
    /// Gets an item type by ID.
    /// </summary>
    Task<ItemType?> GetByIdAsync(Guid id);
}

/// <summary>
/// Service for ItemTypes business logic.
/// Handles validation and orchestrates repository calls.
/// </summary>
public class ItemTypesService : IItemTypesService
{
    private readonly IItemTypesRepository _itemTypesRepo;
    private readonly ILogger<ItemTypesService> _logger;

    public ItemTypesService(
        IItemTypesRepository itemTypesRepo,
        ILogger<ItemTypesService> logger)
    {
        _itemTypesRepo = itemTypesRepo;
        _logger = logger;
    }

    public async Task<IEnumerable<ItemType>> GetAllItemTypesAsync()
    {
        return await _itemTypesRepo.GetAllAsync();
    }

    public async Task<bool> CreateItemTypeAsync(ItemTypeCreateDto itemTypeDto)
    {
        // Validate input
        ValidateItemTypeDto(itemTypeDto);

        // Check if ItemTypeCode already exists
        var existing = await _itemTypesRepo.GetAllAsync();
        if (existing.Any(x => x.ItemTypeCode.Equals(itemTypeDto.ItemTypeCode, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("CreateItemType failed: ItemTypeCode {Code} already exists", itemTypeDto.ItemTypeCode);
            throw new InvalidOperationException($"Codul {itemTypeDto.ItemTypeCode} este deja folosit.");
        }

        try
        {
            await _itemTypesRepo.CreateAsync(itemTypeDto);
            _logger.LogInformation("ItemType created: {Code} - {Name}", itemTypeDto.ItemTypeCode, itemTypeDto.ItemTypeName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ItemType: {Code}", itemTypeDto.ItemTypeCode);
            throw;
        }
    }

    public async Task<bool> UpdateItemTypeAsync(ItemTypeUpdateDto itemTypeDto)
    {
        // Validate input
        ValidateItemTypeUpdateDto(itemTypeDto);

        // Check if ItemType exists
        var existing = await _itemTypesRepo.GetByIdAsync(itemTypeDto.Id);
        if (existing == null)
        {
            _logger.LogWarning("UpdateItemType failed: ItemType Id={Id} not found", itemTypeDto.Id);
            throw new InvalidOperationException($"Tipul de articol cu ID {itemTypeDto.Id} nu există.");
        }

        // Check if ItemTypeCode conflicts with another item
        var allItems = await _itemTypesRepo.GetAllAsync();
        if (allItems.Any(x => x.Id != itemTypeDto.Id &&
                             x.ItemTypeCode.Equals(itemTypeDto.ItemTypeCode, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("UpdateItemType failed: ItemTypeCode {Code} already exists", itemTypeDto.ItemTypeCode);
            throw new InvalidOperationException($"Codul {itemTypeDto.ItemTypeCode} este deja folosit.");
        }

        try
        {
            await _itemTypesRepo.UpdateAsync(itemTypeDto);
            _logger.LogInformation("ItemType updated: {Id} - {Code}", itemTypeDto.Id, itemTypeDto.ItemTypeCode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating ItemType: {Id}", itemTypeDto.Id);
            throw;
        }
    }

    public async Task<bool> DeleteItemTypeAsync(Guid id)
    {
        // Check if ItemType exists
        var existing = await _itemTypesRepo.GetByIdAsync(id);
        if (existing == null)
        {
            _logger.LogWarning("DeleteItemType failed: ItemType Id={Id} not found", id);
            throw new InvalidOperationException($"Tipul de articol cu ID {id} nu există.");
        }

        try
        {
            await _itemTypesRepo.DeleteAsync(id);
            _logger.LogInformation("ItemType deleted: {Id} - {Code}", id, existing.ItemTypeCode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting ItemType: {Id}", id);
            throw;
        }
    }

    public async Task<ItemType?> GetByIdAsync(Guid id)
    {
        return await _itemTypesRepo.GetByIdAsync(id);
    }

    private void ValidateItemTypeDto(ItemTypeCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ItemTypeCode))
            throw new ArgumentException("Codul tipului de articol este obligatoriu.");

        if (dto.ItemTypeCode.Length > 10)
            throw new ArgumentException("Codul tipului de articol nu poate depăși 10 caractere.");

        if (string.IsNullOrWhiteSpace(dto.ItemTypeName))
            throw new ArgumentException("Denumirea tipului de articol este obligatorie.");

        if (dto.ItemTypeName.Length > 100)
            throw new ArgumentException("Denumirea tipului de articol nu poate depăși 100 caractere.");
    }

    private void ValidateItemTypeUpdateDto(ItemTypeUpdateDto dto)
    {
        if (dto.Id == Guid.Empty)
            throw new ArgumentException("ID-ul tipului de articol este invalid.");

        if (string.IsNullOrWhiteSpace(dto.ItemTypeCode))
            throw new ArgumentException("Codul tipului de articol este obligatoriu.");

        if (dto.ItemTypeCode.Length > 10)
            throw new ArgumentException("Codul tipului de articol nu poate depăși 10 caractere.");

        if (string.IsNullOrWhiteSpace(dto.ItemTypeName))
            throw new ArgumentException("Denumirea tipului de articol este obligatorie.");

        if (dto.ItemTypeName.Length > 100)
            throw new ArgumentException("Denumirea tipului de articol nu poate depăși 100 caractere.");
    }
}