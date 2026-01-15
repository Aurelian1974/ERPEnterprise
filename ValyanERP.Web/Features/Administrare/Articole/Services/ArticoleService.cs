using FluentValidation;
using ValyanERP.Web.Features.Administrare.Articole.Models;
using ValyanERP.Web.Features.Administrare.Articole.Repositories;

namespace ValyanERP.Web.Features.Administrare.Articole.Services;

public class ArticoleService : IArticoleService
{
    private readonly IArticoleRepository _articoleRepo;
    private readonly ILogger<ArticoleService> _logger;

    public ArticoleService(IArticoleRepository articoleRepo, ILogger<ArticoleService> logger)
    {
        _articoleRepo = articoleRepo;
        _logger = logger;
    }

    public async Task<IEnumerable<Articol>> GetAllArticoleAsync()
    {
        try
        {
            var articole = await _articoleRepo.GetAllAsync();
            _logger.LogInformation("Retrieved {Count} articole", articole.Count());
            return articole;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all articole");
            throw;
        }
    }

    public async Task<Articol?> GetArticolByIdAsync(Guid id)
    {
        try
        {
            var articol = await _articoleRepo.GetByIdAsync(id);
            if (articol == null)
            {
                _logger.LogWarning("Articol with ID {Id} not found", id);
            }
            return articol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving articol by ID {Id}", id);
            throw;
        }
    }

    public async Task<Articol?> GetArticolByCodeAsync(string code)
    {
        try
        {
            var articol = await _articoleRepo.GetByCodeAsync(code);
            if (articol == null)
            {
                _logger.LogWarning("Articol with code {Code} not found", code);
            }
            return articol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving articol by code {Code}", code);
            throw;
        }
    }

    public async Task<bool> CreateArticolAsync(ArticolCreateDto articolDto)
    {
        // Validate input
        ValidateArticolCreateDto(articolDto);

        // Check if articol code already exists
        if (await _articoleRepo.ExistsAsync(articolDto.ArticolCode))
        {
            _logger.LogWarning("CreateArticol failed: ArticolCode {Code} already exists", articolDto.ArticolCode);
            throw new InvalidOperationException($"Codul {articolDto.ArticolCode} este deja folosit.");
        }

        // Check if TipArticol exists (mandatory association)
        // Note: This would require injecting ITipuriArticoleService or checking via repository
        // For now, we'll assume the validation is done at the UI level

        try
        {
            var result = await _articoleRepo.CreateAsync(articolDto);
            var success = result != Guid.Empty;

            if (success)
            {
                _logger.LogInformation("Articol created successfully: {Code}", articolDto.ArticolCode);
            }
            else
            {
                _logger.LogWarning("CreateArticol returned empty Guid for {Code}", articolDto.ArticolCode);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating articol {Code}", articolDto.ArticolCode);
            throw;
        }
    }

    public async Task<bool> UpdateArticolAsync(ArticolUpdateDto articolDto)
    {
        // Validate input
        ValidateArticolUpdateDto(articolDto);

        // Check if articol exists
        var existing = await _articoleRepo.GetByIdAsync(articolDto.Id);
        if (existing == null)
        {
            _logger.LogWarning("UpdateArticol failed: Articol Id={Id} not found", articolDto.Id);
            throw new InvalidOperationException($"Articolul cu ID {articolDto.Id} nu există.");
        }

        // Check if articol code conflicts with another articol
        if (await _articoleRepo.ExistsAsync(articolDto.ArticolCode, articolDto.Id))
        {
            _logger.LogWarning("UpdateArticol failed: ArticolCode {Code} already exists", articolDto.ArticolCode);
            throw new InvalidOperationException($"Codul {articolDto.ArticolCode} este deja folosit.");
        }

        // Check if TipArticol exists (mandatory association)
        // Note: This would require injecting ITipuriArticoleService or checking via repository

        try
        {
            var success = await _articoleRepo.UpdateAsync(articolDto);

            if (success)
            {
                _logger.LogInformation("Articol updated successfully: {Code}", articolDto.ArticolCode);
            }
            else
            {
                _logger.LogWarning("UpdateArticol failed for {Id}", articolDto.Id);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating articol {Id}", articolDto.Id);
            throw;
        }
    }

    public async Task<bool> DeleteArticolAsync(Guid id)
    {
        try
        {
            var success = await _articoleRepo.DeleteAsync(id);

            if (success)
            {
                _logger.LogInformation("Articol deleted successfully: {Id}", id);
            }
            else
            {
                _logger.LogWarning("DeleteArticol failed for {Id}", id);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting articol {Id}", id);
            throw;
        }
    }

    public async Task<bool> ArticolExistsAsync(string code, Guid? excludeId = null)
    {
        try
        {
            return await _articoleRepo.ExistsAsync(code, excludeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if articol exists {Code}", code);
            throw;
        }
    }

    private void ValidateArticolCreateDto(ArticolCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ArticolCode))
            throw new ArgumentException("Codul articolului este obligatoriu.", nameof(dto.ArticolCode));

        if (dto.ArticolCode.Length > 20)
            throw new ArgumentException("Codul articolului nu poate depăși 20 de caractere.", nameof(dto.ArticolCode));

        if (string.IsNullOrWhiteSpace(dto.ArticolName))
            throw new ArgumentException("Numele articolului este obligatoriu.", nameof(dto.ArticolName));

        if (dto.ArticolName.Length > 100)
            throw new ArgumentException("Numele articolului nu poate depăși 100 de caractere.", nameof(dto.ArticolName));

        if (dto.TipArticolId == Guid.Empty)
            throw new ArgumentException("Tipul de articol este obligatoriu.", nameof(dto.TipArticolId));

        if (dto.Description?.Length > 500)
            throw new ArgumentException("Descrierea nu poate depăși 500 de caractere.", nameof(dto.Description));
    }

    private void ValidateArticolUpdateDto(ArticolUpdateDto dto)
    {
        if (dto.Id == Guid.Empty)
            throw new ArgumentException("ID-ul articolului este obligatoriu.", nameof(dto.Id));

        if (string.IsNullOrWhiteSpace(dto.ArticolCode))
            throw new ArgumentException("Codul articolului este obligatoriu.", nameof(dto.ArticolCode));

        if (dto.ArticolCode.Length > 20)
            throw new ArgumentException("Codul articolului nu poate depăși 20 de caractere.", nameof(dto.ArticolCode));

        if (string.IsNullOrWhiteSpace(dto.ArticolName))
            throw new ArgumentException("Numele articolului este obligatoriu.", nameof(dto.ArticolName));

        if (dto.ArticolName.Length > 100)
            throw new ArgumentException("Numele articolului nu poate depăși 100 de caractere.", nameof(dto.ArticolName));

        if (dto.TipArticolId == Guid.Empty)
            throw new ArgumentException("Tipul de articol este obligatoriu.", nameof(dto.TipArticolId));

        if (dto.Description?.Length > 500)
            throw new ArgumentException("Descrierea nu poate depăși 500 de caractere.", nameof(dto.Description));
    }

    public async Task<int> GetTotalArticoleCountAsync()
    {
        try
        {
            var count = await _articoleRepo.GetTotalCountAsync();
            _logger.LogInformation("Retrieved total articole count: {Count}", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving total articole count");
            throw;
        }
    }
}