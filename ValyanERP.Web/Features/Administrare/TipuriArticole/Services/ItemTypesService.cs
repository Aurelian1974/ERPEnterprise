using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Repositories;
using ValyanERP.Web.Features.Infrastructure.Security.Services;
using Microsoft.Extensions.Logging;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Services
{
    public class ItemTypesService : IItemTypesService
    {
        private readonly IItemTypesRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<ItemTypesService> _logger;

        public ItemTypesService(IItemTypesRepository repository, ICurrentUserService currentUserService, ILogger<ItemTypesService> logger)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        private async Task EnsureAdminAsync()
        {
            if (!await _currentUserService.IsInRoleAsync("Admin"))
            {
                _logger.LogWarning("Unauthorized attempt to modify ItemTypes by {UserId}", _currentUserService.UserId);
                throw new UnauthorizedAccessException("Admin role required");
            }
        }

        public async Task<IEnumerable<ItemType>> GetAllAsync()
        {
            var items = (await _repository.GetAllAsync()).ToList();
            _logger.LogDebug("ItemTypesService.GetAllAsync returned {Count} items", items.Count);
            return items;
        }

        public async Task<ItemType?> GetByIdAsync(Guid id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<Guid> CreateAsync(string code, string name)
        {
            await EnsureAdminAsync();
            var createdBy = _currentUserService.UserId;
            var id = await _repository.CreateAsync(code, name, createdBy);
            _logger.LogInformation("ItemType created {Id} by {UserId}", id, createdBy);
            return id;
        }

        public async Task<int> UpdateAsync(Guid id, string code, string name)
        {
            await EnsureAdminAsync();
            var updatedBy = _currentUserService.UserId;
            var rows = await _repository.UpdateAsync(id, code, name, updatedBy);
            _logger.LogInformation("ItemType {Id} updated by {UserId}", id, updatedBy);
            return rows;
        }

        public async Task<int> DeleteAsync(Guid id)
        {
            await EnsureAdminAsync();
            var updatedBy = _currentUserService.UserId;
            var rows = await _repository.DeleteAsync(id, updatedBy);
            _logger.LogInformation("ItemType {Id} soft-deleted by {UserId}", id, updatedBy);
            return rows;
        }
    }
}