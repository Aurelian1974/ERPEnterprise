# Raport Stoc - Analiz? Ini?ial? (2024-06-13)

## 1. Structur? ?i Fi?iere Identificate

- **Component? UI:**
  - `Components/Pages/Rapoarte/Stoc.razor` (markup)
  - `Components/Pages/Rapoarte/Stoc.razor.cs` (logic)
- **Model:**
  - `Features/Rapoarte/Stoc/Models/StocReportItemDto.cs`
- **Repository:**
  - `Features/Rapoarte/Stoc/Repositories/IStocRepository.cs`
  - `Features/Rapoarte/Stoc/Repositories/StocRepository.cs`
- **Service:**
  - `Features/Rapoarte/Stoc/Services/IStocService.cs`
  - `Features/Rapoarte/Stoc/Services/StocService.cs`
- **API Controller:**
  - `ValyanERP.Web/Controllers/RapoarteController.cs` (expune endpoint `/api/rapoarte/stoc`)
- **Stored Procedure:**
  - `Database/Scripts/099_StoredProcedures_Rapoarte_Stoc.sql` (nume: `sp_Rapoarte_Stoc_Get`)

## 2. Componente Enterprise de Referin??

- **Template recomandat:**
  - `Components/Pages/Administrare/Utilizatori.razor` (+ .cs, .css)
  - `Components/Shared/DataGrid/GridStateManager.razor` (+ .cs)
- **Componente Syncfusion folosite la Utilizatori:**
  - `SfGrid`, `GridStateManager`, `SfDialog`, `SfButton`, `SfDropDownList`, export Excel/PDF

## 3. Dependen?e ?i Impact

- **Toate opera?iile de date** pentru raportul de stoc trec prin repository ?i stored procedure (f?r? SQL inline)
- **Nu exist? înc? adaptor Syncfusion** pentru server-side grid (va trebui creat)
- **Nu exist? înc? dialoguri sau toolbar avansat** (doar listare simpl?)
- **CSS**: Nu exist? fi?ier scoped pentru Stoc (va trebui creat)

## 4. Pa?i Urm?tori

- Creare adaptor Syncfusion pentru grid (server-side)
- Refactorizare markup pentru a folosi SfGrid ?i toolbar
- Ad?ugare GridStateManager ?i dialoguri detalii
- Implementare export ?i persistare stare grid

---

> Document actualizat la 2024-06-13. Va fi completat pe m?sur? ce avanseaz? task-ul.
