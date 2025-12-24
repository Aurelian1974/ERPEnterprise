using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Administrare.Persoane.Repositories;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for Persoane page - manages CRUD operations for persons.
/// </summary>
public partial class Persoane : ComponentBase
{
    [Inject]
    private IPersoaneRepository PersoaneRepository { get; set; } = default!;

    // State
    private IEnumerable<Persoana>? persoane;
    private bool isLoading = true;
    private bool showModal = false;
    private bool showDeleteModal = false;
    private bool isEditing = false;
    private bool isSaving = false;
    private string searchTerm = string.Empty;
    private string alertMessage = string.Empty;
    private string alertClass = "alert-success";
    private Persoana? selectedPersoana;
    private UpdatePersoanaDto editModel = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadPersoane();
    }

    private async Task LoadPersoane()
    {
        isLoading = true;
        try
        {
            persoane = await PersoaneRepository.GetAllAsync();
        }
        catch (Exception ex)
        {
            ShowAlert($"Eroare la încărcarea datelor: {ex.Message}", false);
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task SearchPersoane()
    {
        isLoading = true;
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                persoane = await PersoaneRepository.GetAllAsync();
            }
            else
            {
                persoane = await PersoaneRepository.SearchAsync(searchTerm);
            }
        }
        catch (Exception ex)
        {
            ShowAlert($"Eroare la căutare: {ex.Message}", false);
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task HandleSearchKeyPress(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SearchPersoane();
        }
    }

    private void ShowAddModal()
    {
        editModel = new UpdatePersoanaDto { Tara = "Romania", IsActive = true };
        isEditing = false;
        showModal = true;
    }

    private void ShowEditModal(Persoana persoana)
    {
        editModel = new UpdatePersoanaDto
        {
            Id = persoana.Id,
            Nume = persoana.Nume,
            Prenume = persoana.Prenume,
            CNP = persoana.CNP,
            DataNasterii = persoana.DataNasterii,
            Email = persoana.Email,
            Telefon = persoana.Telefon,
            Adresa = persoana.Adresa,
            Oras = persoana.Oras,
            Judet = persoana.Judet,
            CodPostal = persoana.CodPostal,
            Tara = persoana.Tara,
            IsActive = persoana.IsActive
        };
        isEditing = true;
        showModal = true;
    }

    private void CloseModal()
    {
        showModal = false;
        editModel = new();
    }

    private async Task SavePersoana()
    {
        isSaving = true;
        try
        {
            if (isEditing)
            {
                await PersoaneRepository.UpdateAsync(editModel);
                ShowAlert("Persoana a fost actualizată cu succes.", true);
            }
            else
            {
                await PersoaneRepository.CreateAsync(new CreatePersoanaDto
                {
                    Nume = editModel.Nume,
                    Prenume = editModel.Prenume,
                    CNP = editModel.CNP,
                    DataNasterii = editModel.DataNasterii,
                    Email = editModel.Email,
                    Telefon = editModel.Telefon,
                    Adresa = editModel.Adresa,
                    Oras = editModel.Oras,
                    Judet = editModel.Judet,
                    CodPostal = editModel.CodPostal,
                    Tara = editModel.Tara
                });
                ShowAlert("Persoana a fost adăugată cu succes.", true);
            }
            
            CloseModal();
            await LoadPersoane();
        }
        catch (Exception ex)
        {
            ShowAlert($"Eroare la salvare: {ex.Message}", false);
        }
        finally
        {
            isSaving = false;
        }
    }

    private void ShowDeleteConfirmation(Persoana persoana)
    {
        selectedPersoana = persoana;
        showDeleteModal = true;
    }

    private async Task DeletePersoana()
    {
        if (selectedPersoana == null) return;

        isSaving = true;
        try
        {
            await PersoaneRepository.DeleteAsync(selectedPersoana.Id);
            ShowAlert($"Persoana {selectedPersoana.NumeComplet} a fost ștearsă.", true);
            showDeleteModal = false;
            await LoadPersoane();
        }
        catch (Exception ex)
        {
            ShowAlert($"Eroare la ștergere: {ex.Message}", false);
        }
        finally
        {
            isSaving = false;
        }
    }

    private void ShowAlert(string message, bool isSuccess)
    {
        alertMessage = message;
        alertClass = isSuccess ? "alert-success" : "alert-danger";
    }

    private void CloseAlert()
    {
        alertMessage = string.Empty;
    }
}
