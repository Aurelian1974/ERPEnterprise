---
name: ui-split-layout
description: >-
  SplitLayout master-detail și SidePanel drawer pentru ERP.
  Vezi skill-ul ui-form-section care conține implementările complete
  pentru FormSection, SplitLayout și SidePanel.
---

# SplitLayout & SidePanel

Implementările complete se găsesc în skill-ul `ui-form-section`:

- **SplitLayout** — master-detail cu lista stânga și detaliu dreapta, `width` configurabil
- **SidePanel** — drawer din dreapta bazat pe shadcn/ui Sheet, size `sm/md/lg`, footer cu butoane

## Referință rapidă

```tsx
// SplitLayout
import { SplitLayout } from '@/components/common/SplitLayout/SplitLayout';

<SplitLayout
  master={<InvoiceList onSelect={setSelectedId} />}
  detail={selectedId ? <InvoiceDetail id={selectedId} /> : <EmptyDetail />}
  masterWidth="320px"
/>

// SidePanel
import { SidePanel } from '@/components/common/SidePanel/SidePanel';

<SidePanel
  open={Boolean(editId)}
  onClose={() => setEditId(null)}
  title="Editare factură"
  size="lg"
  footer={<><Button variant="outline">Anulează</Button><Button>Salvează</Button></>}
>
  <InvoiceForm id={editId!} />
</SidePanel>
```

## Când să folosești ce

| Pattern | Componentă | Când |
|---|---|---|
| Lista + detaliu simultan | `SplitLayout` | Pagini cu volum mare unde utilizatorul navighează rapid între înregistrări |
| Edit fără navigare | `SidePanel` | Editare rapidă a câtorva câmpuri fără a părăsi pagina curentă |
| Edit complet | Pagină separată | Formulare complexe cu multe secțiuni (EditableGrid, Attachments, etc.) |
