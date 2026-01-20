using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;

namespace ValyanERP.Web.Components.Pages.Rapoarte
{
    public partial class Stoc : ComponentBase
    {
        private IEnumerable<StocReportItemDto>? items;
        private string? errorMessage;
        // JS runtime injected via @inject in the .razor file

        protected async Task Load()
        {
            errorMessage = null;
            try
            {
                if (JS == null) throw new InvalidOperationException("JSRuntime not available");
                var json = await JS.InvokeAsync<string>("fetchWithCredentials", "/api/rapoarte/stoc");
                items = JsonSerializer.Deserialize<IEnumerable<StocReportItemDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                errorMessage = "Eroare la încărcarea raportului: " + ex.Message;
                items = null;
            }

            await InvokeAsync(StateHasChanged);
        }
    }
}
