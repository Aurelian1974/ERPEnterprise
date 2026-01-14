# Analiză: Adăugare Societăți / Puncte de Lucru / Locații în Structura Arbore

**Data:** 2026-01-14
**Autor:** Automat (task inițial)

## Obiectiv
Permite adăugarea de Societăți, Puncte de Lucru și Locații în pagina "Societatea Proprie" prin dialog (modal), cu validări și acces restricționat la rolul `Admin`. Inserțiile trebuie realizate prin Stored Procedures (convenție proiect).

## Ce am găsit
- UI: Există componente pentru dialoguri: `CompanyDialog.razor`, `WorkPlaceDialog.razor`, `LocationDialog.razor` (forme și validări implementate).
- Page: `SocietateaProprie.razor` + code-behind gestionează afișarea arborelui și evenimentele de Add/Edit/Delete.
- Services/Repos: `OrganizationService` / `CompanyRepository` / `WorkPlaceRepository` / `LocationRepository` folosesc stored procedures (`sp_Companies_Create`, `sp_WorkPlaces_Create`, `sp_Locations_Create` etc.) - deja prezente în database scripts (`017_*.sql`, `018_StoredProcedures_SocietateaProprie.sql`).
- Acces: Pagina are `[Authorize]` (orice utilizator autentificat); nu exista restricții pe operațiuni de creare/ștergere la nivel server.

## Decizii și modificări efectuate
1. UI: Am învelit butoanele de "Add / Edit / Delete" în `AuthorizeView Roles="Admin"` pentru:
   - Header (buton Grup Nou / Companie Nouă)
   - Empty state ("Adaugă prima companie")
   - Panouri detalii: `CompanyDetailPanel`, `GroupDetailPanel`, `WorkPlaceDetailPanel`, `LocationDetailPanel`
   Rezultat: butoanele sunt vizibile doar către utilizatorii cu rol `Admin`.

2. Server: Am introdus verificări de rol în `OrganizationService`:
   - Am injectat `ICurrentUserService` și adăugat `EnsureAdminAsync()` care aruncă `UnauthorizedAccessException` dacă utilizatorul nu este `Admin`.
   - Am apelat `EnsureAdminAsync()` în metode create/update/delete pentru grupuri, companii, puncte de lucru și locații.
   Rezultat: protecție server-side împotriva operațiunilor CRUD de către utilizatori fără rol `Admin`.

3. Stored Procedures: nu a fost nevoie de modificări (există deja în `018_StoredProcedures_SocietateaProprie.sql`).

## Următorii pași (propus)
- [ ] Adăuga teste unitare pentru `OrganizationService` (verifică că operațiile CRUD aruncă `UnauthorizedAccessException` când userul nu e Admin și reușesc când e Admin).
- [ ] Adăuga teste componentă (bUnit) pentru a verifica vizibilitatea butoanelor în funcție de rol (Authorized vs NotAuthorized).
- [ ] Actualizează documentația în `DevSupport/Completed` când testele sunt implementate.
- [ ] PR și code review.

---

Dacă ești de acord cu pașii, trec la implementarea testelor (unit + component) și la actualizarea documentației.