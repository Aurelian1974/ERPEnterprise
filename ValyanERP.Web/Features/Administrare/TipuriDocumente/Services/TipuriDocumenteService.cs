using ValyanERP.Web.Features.Administrare.TipuriDocumente.Models;
using ValyanERP.Web.Features.Administrare.TipuriDocumente.Repositories;
using ValyanERP.Web.Features.Administrare.TipuriDocumente.Services;

namespace ValyanERP.Web.Features.Administrare.TipuriDocumente.Services;

public class TipuriDocumenteService : ITipuriDocumenteService
{
    private readonly ITipuriDocumenteRepository _repository;

    public TipuriDocumenteService(ITipuriDocumenteRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<TipDocument>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<TipDocument?> GetByIdAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<int> CreateAsync(CreateTipDocumentDto dto)
    {
        // Validate uniqueness
        if (await _repository.ExistsByCodeAsync(dto.TipDocumentCode))
        {
            throw new InvalidOperationException($"Codul '{dto.TipDocumentCode}' este deja utilizat.");
        }

        if (await _repository.ExistsByNameAsync(dto.TipDocumentName))
        {
            throw new InvalidOperationException($"Numele '{dto.TipDocumentName}' este deja utilizat.");
        }

        return await _repository.CreateAsync(dto);
    }

    public async Task<bool> UpdateAsync(UpdateTipDocumentDto dto)
    {
        // Validate uniqueness
        if (await _repository.ExistsByCodeAsync(dto.TipDocumentCode, dto.Id))
        {
            throw new InvalidOperationException($"Codul '{dto.TipDocumentCode}' este deja utilizat.");
        }

        if (await _repository.ExistsByNameAsync(dto.TipDocumentName, dto.Id))
        {
            throw new InvalidOperationException($"Numele '{dto.TipDocumentName}' este deja utilizat.");
        }

        return await _repository.UpdateAsync(dto);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        if (!await CanDeleteAsync(id))
        {
            throw new InvalidOperationException("Tipul de document nu poate fi șters deoarece este utilizat în alte înregistrări.");
        }

        return await _repository.DeleteAsync(id);
    }

    public async Task<bool> CanDeleteAsync(Guid id)
    {
        // For now, allow deletion. In the future, check if the document type is used in other tables
        // For example: check if there are documents of this type
        return true;
    }
}