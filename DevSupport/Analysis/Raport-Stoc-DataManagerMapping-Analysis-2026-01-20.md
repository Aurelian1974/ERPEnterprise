# Analysis: Raport Stoc - DataManagerRequest mapping

Date: 2026-01-20

Goal: Ensure `POST /api/rapoarte/stoc` correctly binds the payload sent by Syncfusion `SfDataManager` (UrlAdaptor) and implement server-side handling. Provide logging to inspect exact payload shape from client to adapt DTO mapping.

Files involved
- `Controllers/RapoarteController.cs` (POST /api/rapoarte/stoc)
- `Features/Rapoarte/Stoc/StocAdaptor.cs` (ReadAsync implemented)
- `Components/Pages/Rapoarte/Stoc.razor` (SfDataManager configuration)

Proposed steps
1. ? Add analysis doc (this file) — evidence of Step 0 (DONE).
2. ? Add detailed debug logging in `PostStoc` to capture raw payload / bound DTO (will help adapt mapping) — implement now.
3. ? Make DTO more resilient: support nested `where` predicates and multiple shapes (simple filters or complex predicate tree). Implement helper to flatten predicates.
4. ? Use flattened filters to extract `PunctLucruId`, `TipArticolId`, `MinStoc` (existing logic but using robust extraction).
5. ? Run app and reproduce request; collect payload from logs / network — requires user to run and share logs or confirm behaviour.
6. ? Adjust DTO mapping or controller logic based on actual payload (if needed). Mark done after validation.

Notes
- Syncfusion sometimes sends `where` as a complex object with `predicates` arrays; binding to a shallow DTO fails to capture nested filters. Logging payload is the fastest way to adapt.
- For production, prefer implementing server-side paging/filtering at repository level rather than fetching all rows and filtering in-memory.

Next action
- I will modify `RapoarteController.PostStoc` to log the incoming payload (serialize bound DTO) and to flatten nested where predicates. Then you should run the app, reproduce the request and paste the logged payload (or allow me to inspect logs) so I can finalize DTO adjustments.

Plan ID: RaportStoc-DataManager-Mapping

Progress
1. ? Analysis document created.
2. ? Implement controller logging and robust mapping.
3. ? Run and verify payload shape.
4. ? Finalize mapping and mark complete.
