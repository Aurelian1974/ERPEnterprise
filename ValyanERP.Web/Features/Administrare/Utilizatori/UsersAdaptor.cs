using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Utilizatori.Repositories;

namespace ValyanERP.Web.Features.Administrare.Utilizatori;

public class UsersAdaptor : DataAdaptor
{
    private readonly IUsersRepository _repository;

    public UsersAdaptor(IUsersRepository repository)
    {
        _repository = repository;
    }

    public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
    {
        return await _repository.GetPagedAsync(dm);
    }
}
