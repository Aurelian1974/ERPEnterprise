---
mode: agent
description: Generează un feature frontend complet — api.ts, schemas.ts, List component, Form component.
---

Creează un feature frontend complet respectând skill-ul `frontend-feature`.

**Informații necesare:**
- Modul: [finance / hr / inventory / ...]
- Entitate: [ex: Invoice, Employee, Product]
- Operații necesare: [List / Create / Edit / Delete / Approve / ...]
- Câmpuri formular principal
- Permisiuni necesare: [ex: finance.invoices.create, finance.invoices.approve]

**Generează în ordine:**

1. **`features/{module}/{entity}/api.ts`**
   - `{entity}Keys` obiect cu query keys
   - `use{Entity}s(filters)` — TanStack Query list hook
   - `use{Entity}(id)` — TanStack Query detail hook
   - `useCreate{Entity}()` — mutation cu `invalidateQueries`
   - `useUpdate{Entity}(id)` — mutation cu `invalidateQueries`
   - `useDelete{Entity}()` — mutation cu `invalidateQueries`

2. **`features/{module}/{entity}/schemas.ts`**
   - Zod schema pentru formular create/edit
   - Tipuri derivate cu `z.infer<typeof schema>`
   - Niciodată tipuri scrise manual

3. **`features/{module}/{entity}/{Entity}List.tsx`**
   - TanStack Table cu `createColumnHelper`
   - Paginare server-side
   - `usePermission()` pe butoane destructive
   - Link la pagina de detaliu

4. **`features/{module}/{entity}/{Entity}Form.tsx`**
   - React Hook Form + Zod resolver
   - `useFieldArray` dacă are linii repetabile
   - Submit via mutation hook
   - Redirect după success

**Reguli obligatorii:**
- Tipuri API din `src/api/generated/` — niciodată scrise manual
- Niciodată `any`
- Niciodată barrel files
- Niciodată `fetch` direct — exclusiv TanStack Query
- `usePermission('{module}.{entity}.{action}')` pe orice acțiune sensibilă
- `invalidateQueries({ queryKey: {entity}Keys.all })` după orice mutație
