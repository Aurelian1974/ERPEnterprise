# Task: Implement grouping / lazy-load / aggregates in StocAdaptor

Date: 2026-01-20
Plan ID: RaportStoc-Grouping-LazyLoad

## Goal
Add robust server-side grouping, lazy-load grouping, and aggregates support to `StocAdaptor.ReadAsync`, matching patterns used in `ArticoleAdaptor` and using `IDataGridOperationsService`.

## Steps
1. ? Review existing `StocAdaptor.ReadAsync` and `DataGridOperationsService` (DONE)
2. ? Add debug logging for group and aggregate descriptors (DONE)
3. ? Implement explicit handling for:
   - Export / FilterChoice (no paging, return full DataResult)
   - Lazy-load expand requests (Take=0, LazyLoad=true with filters)
   - Normal filtering requests (apply filtering and return DataResult)
   - Grouping requests: if LazyLoad then call `ApplyLazyLoadGrouping`, else `ApplyGrouping`
   - Default operations: call `ApplyOperations` (paging/sorting/search)
   (MOST STEPS ALREADY IMPLEMENTED; this task ensures non-lazy grouping handled)
4. ? Update code to return correct types for Syncfusion (DataResult vs IEnumerable)
5. ? Test end-to-end in browser: expand groups, verify aggregates, paging, export
6. ? If issues, collect logs and iterate

## Progress
- Steps 1-4 implemented in code changes.
- Steps 5-6 pending: need you to run the app, exercise grouping in UI and provide logs if issues occur.

## Files changed
- `Features/Rapoarte/Stoc/StocAdaptor.cs` — enhanced `ReadAsync` grouping handling and logging
- `Components/Pages/Rapoarte/Stoc.razor` — already switched to `AdaptorInstance` in earlier changes

## Notes
- For large datasets consider moving grouping/aggregation into DB (stored procedure) for performance.
- If you test and paste server logs when expanding groups, I'll adapt further.

