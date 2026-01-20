# Analiză: Raport Stoc

Data: 2026-01-20
Autor: Copilot

Scop
---
Adăugarea primului raport în meniul `Rapoarte`: raport privind stocul (inventar). Raportul trebuie să permită filtrare minimală (locație/punct de lucru, tip articol, stoc minim) și să returneze o listă cu cantități, valoare și status.

Componente afectate
---
- Frontend:
  - `Components/Layout/Sidebar.razor` — adăugare intrare meniu și /rapoarte/stoc
  - Pagină Razor pentru raport: `Components/Pages/Rapoarte/Stoc.razor` + `Stoc.razor.cs` + `Stoc.razor.css`
- Backend:
  - Feature: `Features/Rapoarte/Stoc/Models`, `Repositories`, `Services`
  - Controller/API: `Controllers/RapoarteController.cs` (sau extensie existentă)
  - Program.cs: înregistrare DI pentru repository/service
- Database:
  - Script SQL nou în `Database/Scripts/` pentru stored procedure: `0XX_StoredProcedures_Reports_Stoc.sql`

Design propus (high-level)
---
1. Stored procedure `sp_Rapoarte_Stoc_Get` cu parametri opționali `@PunctLucruId`, `@TipArticolId`, `@MinStoc`.
2. Repository `IStocRepository` + `StocRepository` care apelează SP folosind Dapper (CommandType.StoredProcedure).
3. Service `IStocService` care validează parametri și formează DTO-uri.
4. Controller `RapoarteController` cu endpoint `GET /api/rapoarte/stoc` ce returnează `StocReportItemDto[]`.
5. Pagină Razor `Stoc.razor` care oferă filtre și afișează tabel (Syncfusion sau HTML table simplă inițial).

Validări și securitate
---
- Endpoint accesibil doar utilizatorilor autorizați (atribut `[Authorize]`).
- Input sanitizat și valori implicite folosite pentru parametri opționali.

Următorii pași (execuție)
---
1. Creez scaffold pentru feature (modele, repo, service, controller, pagină).
2. Adaug script SQL pentru stored procedure (schelet). 
3. Implementez repository + service.
4. Leg pagină + meniu.
5. Test rapid (manual) endpoint.

Notă
---
Voi implementa doar scaffolding și endpoint minimal; logica de agregare/valoare poate fi extinsă după confirmare.
