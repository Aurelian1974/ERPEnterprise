# Plan: Mutare paging/filtering în Repository / Stored Procedure (Rapoarte Stoc)

Data: 2026-01-20
Plan ID: RaportStoc-Repo-Optimization

Scop
- Mutarea logicii de paging, filtering ?i sorting din adaptor/controller în repository/stored procedure pentru performan?? la volume mari.
- Rezultatul: apeluri eficiente la baza de date, timp de r?spuns sc?zut ?i consum redus de memorie pe server.

Riscuri
- Necesit? modificare SQL (stored procedure) ?i testare pe date reale
- Trebuie tratat? compatibilitatea cu actualele filtre/agrregate Syncfusion

Fi?iere afectate (propuse)
- Database/Scripts/XXX_Rapoarte_Stoc_Paging.sql (nou)
- Features/Rapoarte/Stoc/Repositories/StocRepository.cs (modificare)
- Features/Rapoarte/Stoc/Repositories/IStocRepository.cs (ad?ugare metod? paginare)
- Features/Rapoarte/Stoc/Services/StocService.cs (posibil adapt?ri)
- Features/Rapoarte/Stoc/StocAdaptor.cs (folose?te noua metod? GetPagedAsync)
- Controllers/RapoarteController.cs (dac? folose?te repository paginat pentru UrlAdaptor POST)

Pa?i (detalia?i)
1. ? Documentare / analiz? (CURRENT) — create aceast? pagin? (DONE)
2. ? Definire SP: creare stored procedure `sp_Rapoarte_Stoc_GetPaged` cu parametri:
   - @PunctLucruId UNIQUEIDENTIFIER = NULL
   - @TipArticolId UNIQUEIDENTIFIER = NULL
   - @MinStoc DECIMAL(18,2) = NULL
   - @Skip INT = 0
   - @Take INT = 20
   - @Sort NVARCHAR(MAX) = NULL -- ex: "Cod ASC, Denumire DESC"
   - @Filter NVARCHAR(MAX) = NULL -- optional, JSON sau expresie simpl?
   - RETURN result set paginat ?i total count (output param sau second resultset)

   Output: Query optimizat, index usage, optional OFFSET-FETCH sau ROW_NUMBER

3. ? Adaug script SQL în `Database/Scripts/` ?i rulez local (SSMS) pe DB de test. Verific plan de execu?ie ?i indexuri.
4. ? Extind `IStocRepository` cu metod?:
   ```csharp
   Task<(IEnumerable<StocReportItemDto> Items, int TotalCount)> GetPagedAsync(Guid? punctLucruId, Guid? tipArticolId, decimal? minStoc, int skip, int take, string? sort, string? filterJson = null);
   ```
5. ? Implement `StocRepository.GetPagedAsync` care apeleaz? `sp_Rapoarte_Stoc_GetPaged` folosind Dapper, returnând items ?i totalCount (output param sau second resultset).
6. ? Modific `StocService` pentru a expune aceea?i metod? (sau o adaptare) ?i tratament valid?ri.
7. ? Modific `StocAdaptor.ReadAsync` s? foloseasc? `GetPagedAsync(dm)` când nu este grouping/lazy expand:
   - Construie?te `sort` string din `dm.Sorted` (ex: "Cod ASC, Denumire DESC")
   - Construie?te `filterJson` din `dm.Where/dm.Search` dac? e nevoie
   - Apeleaz? `GetPagedAsync(..., dm.Skip, dm.Take, sort, filterJson)`
   - Returneaz? `DataResult { Result = items, Count = totalCount }`
8. ? Test local end-to-end cu date reale: paging, sorting, filtering, grouping expand (lazy grouping r?mâne handled la nivel adaptor/service dup? necesitate)
9. ? Performance testing: compar? execu?ie SP vs in-memory (profilare timp ?i memorie)
10. ? Cod review ?i commit (conventional commit `feat(stoc): server-side paging/filtering in stored procedure`)
11. ? Deploy pe staging ?i testare final? cu trafic real
12. ? Finalizare documenta?ie în `DevSupport/Completed/RaportStoc-Repo-Optimization-Final-2026-01-20.md` (create summary, SQL script, files changed, test results)

Estimare timp
- Creare SP + testare local: 1-2 ore
- Implementare repository + adaptor changes: 1-2 ore
- End-to-end testing + perf measurement: 1-2 ore
Total estimat: 3-6 ore (în func?ie de complexitatea SP ?i date)

Ce pot implementa imediat (dup? confirmare)
- Creez template SQL `sp_Rapoarte_Stoc_GetPaged` în `Database/Scripts/` (neexecutat)
- Adaug metoda `GetPagedAsync` în interfa?? ?i implementare repository (calls SP)
- Actualizez `StocAdaptor.ReadAsync` s? foloseasc? `GetPagedAsync`

Te rog confirm? ce vrei s? fac acum:
- 1) `create-sql-and-repo` — creez SP template + repository method + adaptor changes (implementare local, build)
- 2) `create-plan-only` — p?streaz? doar planul; eu nu modific codul înc?

R?spunde cu `1` sau `2`.
