using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Administrare.Utilizatori.Models;
using ValyanERP.Web.Features.Administrare.Utilizatori.Repositories;
using ValyanERP.Web.Infrastructure.Identity;

namespace ValyanERP.Web.Features.Administrare.Utilizatori.Services;

/// <summary>
/// Service interface for Users business logic.
/// </summary>
public interface IUsersService
{
    /// <summary>
    /// Gets all users for export purposes.
    /// </summary>
    Task<IEnumerable<User>> GetAllUsersAsync();

    /// <summary>
    /// Gets available persons for user assignment (dropdown).
    /// </summary>
    Task<IEnumerable<Persoana>> GetAvailablePersonsAsync();

    /// <summary>
    /// Creates a new user with password hashing.
    /// </summary>
    Task<bool> CreateUserAsync(UserCreateDto userDto);

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    Task<bool> UpdateUserAsync(User user);

    /// <summary>
    /// Soft deletes a user.
    /// </summary>
    Task<bool> DeleteUserAsync(Guid id);

    /// <summary>
    /// Gets a user by ID.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id);

    /// <summary>
    /// Resets a user's password.
    /// </summary>
    Task<bool> ResetPasswordAsync(Guid userId, string newPassword);
}

/// <summary>
/// Service for Users business logic.
/// Handles password hashing, validation, and orchestrates repository/UserManager calls.
/// </summary>
public class UsersService : IUsersService
{
    private readonly ValyanERP.Web.Features.Administrare.Persoane.Repositories.IPersoaneRepository _persoaneRepo;
    private readonly IUsersRepository _usersRepo;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly ILogger<UsersService> _logger;
    
    public UsersService(
        ValyanERP.Web.Features.Administrare.Persoane.Repositories.IPersoaneRepository persoaneRepo,
        IUsersRepository usersRepo,
        IPasswordHasher<ApplicationUser> passwordHasher,
        ILogger<UsersService> logger)
    {
        _persoaneRepo = persoaneRepo;
        _usersRepo = usersRepo;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<IEnumerable<Persoana>> GetAvailablePersonsAsync()
    {
        return await _persoaneRepo.GetAllSimpleAsync();
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        return await _usersRepo.GetAllAsync();
    }

    public async Task<bool> CreateUserAsync(UserCreateDto userDto)
    {
        // Validate input
        ValidateUserDto(userDto);

        // Check if Persoana exists
        var persoana = await _persoaneRepo.GetByIdAsync(userDto.PersoanaId);
        if (persoana == null)
        {
            _logger.LogWarning("CreateUser failed: Persoana Id={PersoanaId} not found", userDto.PersoanaId);
            throw new InvalidOperationException($"Persoana cu ID {userDto.PersoanaId} nu există.");
        }

        // Hash password using Argon2id (via custom IPasswordHasher)
        var dummyUser = new ApplicationUser(); // Dummy for hashing interface
        userDto.PasswordHash = _passwordHasher.HashPassword(dummyUser, userDto.Password);

        // Create via repository
        await _usersRepo.CreateAsync(userDto);

        return true;
    }

    public async Task<bool> UpdateUserAsync(User user)
    {
        // Validate input
        if (user.Id == Guid.Empty)
        {
            _logger.LogWarning("UpdateUser failed: Invalid user Id");
            throw new ArgumentException("ID-ul utilizatorului este invalid.", nameof(user.Id));
        }

        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            _logger.LogWarning("UpdateUser failed: UserName is required");
            throw new ArgumentException("Username-ul este obligatoriu.", nameof(user.UserName));
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning("UpdateUser failed: Email is required");
            throw new ArgumentException("Email-ul este obligatoriu.", nameof(user.Email));
        }

        // Check if exists
        var existing = await _usersRepo.GetByIdAsync(user.Id);
        if (existing == null)
        {
            _logger.LogWarning("UpdateUser failed: User Id={Id} not found", user.Id);
            throw new InvalidOperationException($"Utilizatorul cu ID {user.Id} nu există.");
        }

        // Check if Persoana exists
        var persoana = await _persoaneRepo.GetByIdAsync(user.PersoanaId);
        if (persoana == null)
        {
            _logger.LogWarning("UpdateUser failed: Persoana Id={PersoanaId} not found", user.PersoanaId);
            throw new InvalidOperationException($"Persoana cu ID {user.PersoanaId} nu există.");
        }

        // Update via repository
        await _usersRepo.UpdateAsync(user);

        return true;
    }

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        // Check if exists
        var existing = await _usersRepo.GetByIdAsync(id);
        if (existing == null)
        {
            _logger.LogWarning("DeleteUser failed: User Id={Id} not found", id);
            throw new InvalidOperationException($"Utilizatorul cu ID {id} nu există.");
        }

        // Soft delete via repository
        await _usersRepo.DeleteAsync(id);

        return true;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _usersRepo.GetByIdAsync(id);
    }

    public async Task<bool> ResetPasswordAsync(Guid userId, string newPassword)
    {
        // Validate input
        if (userId == Guid.Empty)
        {
            _logger.LogWarning("ResetPassword failed: Invalid user Id");
            throw new ArgumentException("ID-ul utilizatorului este invalid.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            _logger.LogWarning("ResetPassword failed: Password is required");
            throw new ArgumentException("Parola nouă este obligatorie.", nameof(newPassword));
        }

        if (newPassword.Length < 8)
        {
            _logger.LogWarning("ResetPassword failed: Password too short ({Length} chars)", newPassword.Length);
            throw new ArgumentException("Parola trebuie să aibă cel puțin 8 caractere.", nameof(newPassword));
        }

        // Check if user exists
        var existing = await _usersRepo.GetByIdAsync(userId);
        if (existing == null)
        {
            _logger.LogWarning("ResetPassword failed: User Id={Id} not found", userId);
            throw new InvalidOperationException($"Utilizatorul cu ID {userId} nu există.");
        }

        // Hash password using Argon2id (via custom IPasswordHasher)
        var dummyUser = new ApplicationUser(); // Dummy for hashing interface
        var passwordHash = _passwordHasher.HashPassword(dummyUser, newPassword);

        // Reset password via repository
        await _usersRepo.ResetPasswordAsync(userId, passwordHash);

        _logger.LogInformation("Password reset successfully for User Id={Id}", userId);
        return true;
    }

    /// <summary>
    /// Validates UserCreateDto business rules.
    /// </summary>
    private void ValidateUserDto(UserCreateDto userDto)
    {
        if (userDto.PersoanaId == Guid.Empty)
        {
            _logger.LogWarning("Validation failed: PersoanaId is required");
            throw new ArgumentException("PersoanaId este obligatoriu.", nameof(userDto.PersoanaId));
        }

        if (string.IsNullOrWhiteSpace(userDto.UserName))
        {
            _logger.LogWarning("Validation failed: UserName is required");
            throw new ArgumentException("Username-ul este obligatoriu.", nameof(userDto.UserName));
        }

        if (string.IsNullOrWhiteSpace(userDto.Email))
        {
            _logger.LogWarning("Validation failed: Email is required");
            throw new ArgumentException("Email-ul este obligatoriu.", nameof(userDto.Email));
        }

        if (string.IsNullOrWhiteSpace(userDto.Password))
        {
            _logger.LogWarning("Validation failed: Password is required");
            throw new ArgumentException("Parola este obligatorie.", nameof(userDto.Password));
        }

        if (userDto.Password.Length < 8)
        {
            _logger.LogWarning("Validation failed: Password too short ({Length} chars)", userDto.Password.Length);
            throw new ArgumentException("Parola trebuie să aibă cel puțin 8 caractere.", nameof(userDto.Password));
        }

        if (userDto.UserName.Length > 256)
        {
            _logger.LogWarning("Validation failed: UserName too long ({Length} chars)", userDto.UserName.Length);
            throw new ArgumentException("Username-ul nu poate depăși 256 caractere.", nameof(userDto.UserName));
        }

        if (userDto.Email.Length > 256)
        {
            _logger.LogWarning("Validation failed: Email too long ({Length} chars)", userDto.Email.Length);
            throw new ArgumentException("Email-ul nu poate depăși 256 caractere.", nameof(userDto.Email));
        }
    }
}
