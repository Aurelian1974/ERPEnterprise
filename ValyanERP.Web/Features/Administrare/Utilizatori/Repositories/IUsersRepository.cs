using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Utilizatori.Models;

namespace ValyanERP.Web.Features.Administrare.Utilizatori.Repositories;

public interface IUsersRepository
{
    Task<DataResult> GetPagedAsync(DataManagerRequest dm);
    Task<IEnumerable<User>> GetAllAsync();
    Task<User?> GetByIdAsync(Guid id);
    Task CreateAsync(UserCreateDto user);
    Task UpdateAsync(User user);
    Task DeleteAsync(Guid id);
}
