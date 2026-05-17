---
name: ui-print-layout
description: >-
  PrintLayout pentru documente printabile ERP (@media print, A4, fără UI chrome)
  și AuditTrail timeline din audit.audit_log. Implementările complete sunt în
  skill-ul ui-step-wizard.
---

# PrintLayout & AuditTrail

Implementările complete se găsesc în skill-ul `ui-step-wizard`.

## PrintLayout — referință rapidă

```tsx
import { PrintLayout } from '@/components/common/PrintLayout/PrintLayout';

function InvoiceDetailPage({ id }: { id: string }) {
  const { data } = useInvoice(id);

  return (
    <PrintLayout>
      {/* Conținut document — afișat și pe ecran și la printare */}
      <div className="max-w-3xl mx-auto p-8">
        <h1>Factură {data?.invoiceNumber}</h1>
        {/* ... */}
      </div>
    </PrintLayout>
  );
}
```

**Reguli print:**
- `@page { size: A4; margin: 15mm 20mm }` — standard documente românești
- `page-break-inside: avoid` pe `<tr>` — nu rupe rânduri tabel între pagini
- `thead { display: table-header-group }` — header tabel pe fiecare pagină
- `font-size: 11pt` în print — mai mic decât pe ecran, densitate mai mare
- Ascunde: `nav`, `aside`, `header`, butoane, filtre — clasă `.print:hidden`

## AuditTrail — referință rapidă

```tsx
import { AuditTrail } from '@/components/common/AuditTrail/AuditTrail';

// Într-un tab "Istoric" din pagina de detaliu
<AuditTrail
  entityType="Invoice"
  entityId={invoiceId}
/>
```

**Backend necesar:**
```sql
-- usp_GetAuditTrail.sql
CREATE OR ALTER PROCEDURE administration.usp_GetAuditTrail
    @TenantId   UNIQUEIDENTIFIER,
    @EntityType NVARCHAR(100),
    @EntityId   NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 100
           a.id          AS Id,
           a.action      AS Action,
           a.user_name   AS UserName,
           a.old_values  AS OldValues,
           a.new_values  AS NewValues,
           a.created_at  AS CreatedAt
    FROM audit.audit_log a
    WHERE a.tenant_id   = @TenantId
      AND a.entity_type = @EntityType
      AND a.entity_id   = @EntityId
    ORDER BY a.created_at DESC;
END;
GO
```
