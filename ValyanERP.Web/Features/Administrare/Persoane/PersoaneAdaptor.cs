using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Persoane.Repositories;

namespace ValyanERP.Web.Features.Administrare.Persoane;

public class PersoaneAdaptor : DataAdaptor
{
    private readonly IPersoaneRepository _repository;

    public PersoaneAdaptor(IPersoaneRepository repository)
    {
        _repository = repository;
    }

    public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
    {
        return await _repository.GetPagedAsync(dm);
    }
}
