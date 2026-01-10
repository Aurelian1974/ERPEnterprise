using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Administrare.Persoane.Repositories;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Services;

namespace ValyanERP.Web.Features.Administrare.Persoane.Services;

/// <summary>
/// Service for Persoane business logic.
/// Handles validation, business rules, and orchestrates repository calls.
/// </summary>
public class PersoaneService : IPersoaneService
{
    private readonly IPersoaneRepository _repository;
    private readonly ILogger<PersoaneService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ISystemParametersService _parametersService;
    private const string CACHE_KEY_ALL_SIMPLE = "Persoane_AllSimple";

    public PersoaneService(
        IPersoaneRepository repository, 
        ILogger<PersoaneService> logger, 
        IMemoryCache cache,
        ISystemParametersService parametersService)
    {
        _repository = repository;
        _logger = logger;
        _cache = cache;
        _parametersService = parametersService;
    }

    public async Task<Persoana> CreateAsync(Persoana persoana)
    {
        _logger.LogInformation("CreateAsync called for {Nume} {Prenume}", persoana.Nume, persoana.Prenume);
        
        // Business validation
        ValidatePersoana(persoana);

        // Additional business rules
        if (!string.IsNullOrWhiteSpace(persoana.Email))
        {
            var existing = await _repository.GetByEmailAsync(persoana.Email);
            if (existing != null)
            {
                _logger.LogWarning("Email {Email} already exists for another person", persoana.Email);
                throw new InvalidOperationException($"O persoană cu email-ul {persoana.Email} există deja.");
            }
        }

        // Validate CNP if provided
        if (!string.IsNullOrWhiteSpace(persoana.CNP))
        {
            if (!ValidateCNP(persoana.CNP))
            {
                _logger.LogWarning("Invalid CNP provided: {CNP}", persoana.CNP);
                throw new InvalidOperationException("CNP-ul introdus nu este valid.");
            }
        }

        // Set defaults
        if (persoana.Id == Guid.Empty)
        {
            persoana.Id = Guid.NewGuid();
        }

        if (string.IsNullOrWhiteSpace(persoana.Tara))
        {
            persoana.Tara = await _parametersService.GetStringAsync("Business.DefaultCountry", "Romania");
        }

        persoana.CreatedAt = ValyanERP.Web.Infrastructure.Time.TimeUtils.NowRomania();

        // Create via repository
        await _repository.CreateAsync(persoana);
        
        // Invalidate cache since data changed
        _cache.Remove(CACHE_KEY_ALL_SIMPLE);
        _logger.LogDebug("Cache invalidated after Create");
        
        _logger.LogInformation("Persoana created successfully with Id={Id}", persoana.Id);

        return persoana;
    }

    public async Task<bool> UpdateAsync(Persoana persoana)
    {
        _logger.LogInformation("UpdateAsync called for Id={Id}", persoana.Id);
        
        // Business validation
        ValidatePersoana(persoana);

        // Check if exists
        var existing = await _repository.GetByIdAsync(persoana.Id);
        if (existing == null)
        {
            _logger.LogWarning("Update failed: Persoana Id={Id} not found", persoana.Id);
            throw new InvalidOperationException($"Persoana cu ID {persoana.Id} nu există.");
        }

        // Validate email uniqueness (excluding current record)
        if (!string.IsNullOrWhiteSpace(persoana.Email))
        {
            var emailExists = await _repository.GetByEmailAsync(persoana.Email);
            if (emailExists != null && emailExists.Id != persoana.Id)
            {
                _logger.LogWarning("Email {Email} already used by another person (Id={ExistingId})", persoana.Email, emailExists.Id);
                throw new InvalidOperationException($"O altă persoană cu email-ul {persoana.Email} există deja.");
            }
        }

        // Validate CNP if provided
        if (!string.IsNullOrWhiteSpace(persoana.CNP) && !ValidateCNP(persoana.CNP))
        {
            _logger.LogWarning("Invalid CNP provided in update: {CNP}", persoana.CNP);
            throw new InvalidOperationException("CNP-ul introdus nu este valid.");
        }

        // Update via repository
        await _repository.UpdateAsync(persoana);
        
        // Invalidate cache since data changed
        _cache.Remove(CACHE_KEY_ALL_SIMPLE);
        _logger.LogDebug("Cache invalidated after Update");
        
        _logger.LogInformation("Persoana Id={Id} updated successfully", persoana.Id);

        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        _logger.LogInformation("DeleteAsync called for Id={Id}", id);
        
        // Check if exists
        var existing = await _repository.GetByIdAsync(id);
        if (existing == null)
        {
            _logger.LogWarning("Delete failed: Persoana Id={Id} not found", id);
            throw new InvalidOperationException($"Persoana cu ID {id} nu există.");
        }

        // Soft delete via repository
        await _repository.DeleteAsync(id);
        
        // Invalidate cache since data changed
        _cache.Remove(CACHE_KEY_ALL_SIMPLE);
        _logger.LogDebug("Cache invalidated after Delete");
        
        _logger.LogInformation("Persoana Id={Id} deleted successfully", id);

        return true;
    }

    public async Task<Persoana?> GetByIdAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<Persoana?> GetByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email-ul nu poate fi gol.", nameof(email));
        }

        return await _repository.GetByEmailAsync(email);
    }

    public async Task<IEnumerable<Persoana>> GetAllSimpleAsync()
    {
        // Try to get from cache first
        if (_cache.TryGetValue(CACHE_KEY_ALL_SIMPLE, out IEnumerable<Persoana>? cachedData) && cachedData != null)
        {
            _logger.LogDebug("GetAllSimpleAsync: Cache HIT");
            return cachedData;
        }

        _logger.LogDebug("GetAllSimpleAsync: Cache MISS - fetching from database");
        var data = await _repository.GetAllSimpleAsync();

        // Get cache duration from system parameters (default: 5 minutes)
        var cacheDurationMinutes = await _parametersService.GetIntAsync("Cache.Persoane.DurationMinutes", 5);
        var cacheDuration = TimeSpan.FromMinutes(cacheDurationMinutes);
        
        // Cache with configured duration
        _cache.Set(CACHE_KEY_ALL_SIMPLE, data, cacheDuration);
        _logger.LogInformation("Cached {Count} persons for dropdown (expires in {Duration} minutes)", 
            data.Count(), cacheDuration.TotalMinutes);

        return data;
    }

    public bool ValidateCNP(string? cnp)
    {
        if (string.IsNullOrWhiteSpace(cnp))
        {
            _logger.LogDebug("CNP validation: empty or null CNP");
            return false;
        }

        // CNP must be exactly 13 digits
        if (cnp.Length != 13 || !cnp.All(char.IsDigit))
        {
            _logger.LogDebug("CNP validation failed: invalid length or non-digit characters for CNP={CNP}", cnp);
            return false;
        }

        // Basic validation: first digit must be 1-8 (sex + century)
        var firstDigit = int.Parse(cnp[0].ToString());
        if (firstDigit < 1 || firstDigit > 8)
        {
            _logger.LogDebug("CNP validation failed: invalid first digit {FirstDigit}", firstDigit);
            return false;
        }

        // Validate checksum (last digit)
        var controlDigits = new[] { 2, 7, 9, 1, 4, 6, 3, 5, 8, 2, 7, 9 };
        var sum = 0;

        for (int i = 0; i < 12; i++)
        {
            sum += int.Parse(cnp[i].ToString()) * controlDigits[i];
        }

        var remainder = sum % 11;
        var checkDigit = remainder == 10 ? 1 : remainder;
        
        var isValid = checkDigit == int.Parse(cnp[12].ToString());
        if (!isValid)
        {
            _logger.LogDebug("CNP validation failed: checksum mismatch");
        }

        return isValid;
    }

    /// <summary>
    /// Validates basic Persoana business rules.
    /// </summary>
    private void ValidatePersoana(Persoana persoana)
    {
        _logger.LogDebug("Validating Persoana: {Nume} {Prenume}", persoana.Nume, persoana.Prenume);
        
        if (string.IsNullOrWhiteSpace(persoana.Nume))
        {
            _logger.LogWarning("Validation failed: Nume is required");
            throw new ArgumentException("Numele este obligatoriu.", nameof(persoana.Nume));
        }

        if (string.IsNullOrWhiteSpace(persoana.Prenume))
        {
            _logger.LogWarning("Validation failed: Prenume is required");
            throw new ArgumentException("Prenumele este obligatoriu.", nameof(persoana.Prenume));
        }

        if (persoana.Nume.Length > 100)
        {
            _logger.LogWarning("Validation failed: Nume too long ({Length} chars)", persoana.Nume.Length);
            throw new ArgumentException("Numele nu poate depăși 100 caractere.", nameof(persoana.Nume));
        }

        if (persoana.Prenume.Length > 100)
        {
            _logger.LogWarning("Validation failed: Prenume too long ({Length} chars)", persoana.Prenume.Length);
            throw new ArgumentException("Prenumele nu poate depăși 100 caractere.", nameof(persoana.Prenume));
        }

        if (!string.IsNullOrWhiteSpace(persoana.Email) && persoana.Email.Length > 256)
        {
            _logger.LogWarning("Validation failed: Email too long ({Length} chars)", persoana.Email.Length);
            throw new ArgumentException("Email-ul nu poate depăși 256 caractere.", nameof(persoana.Email));
        }
    }
}
