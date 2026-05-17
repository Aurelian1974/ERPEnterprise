---
name: ui-form-section
description: >-
  FormSection — wrapper standard pentru secțiuni de formular ERP.
  Titlu, descriere opțională, grid 1/2/3/4 coloane responsive, separator.
  SplitLayout — master-detail pattern. SidePanel — drawer edit rapid.
---

# FormSection, SplitLayout, SidePanel

---

## FormSection

```tsx
// components/common/FormSection/FormSection.tsx
import { cn } from '@/lib/utils';

type GridCols = 1 | 2 | 3 | 4;

interface FormSectionProps {
  title?:       string;
  description?: string;
  cols?:        GridCols;
  children:     React.ReactNode;
  className?:   string;
}

const gridClass: Record<GridCols, string> = {
  1: 'grid-cols-1',
  2: 'grid-cols-1 sm:grid-cols-2',
  3: 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3',
  4: 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-4',
};

export function FormSection({
  title,
  description,
  cols = 2,
  children,
  className,
}: FormSectionProps) {
  return (
    <div className={cn('space-y-4', className)}>
      {(title || description) && (
        <div className="border-b border-border-default pb-3">
          {title && (
            <h3 className="text-sm font-semibold text-text-primary">{title}</h3>
          )}
          {description && (
            <p className="text-xs text-text-muted mt-0.5">{description}</p>
          )}
        </div>
      )}
      <div className={cn('grid gap-4', gridClass[cols])}>
        {children}
      </div>
    </div>
  );
}

// Utilizare
// <FormSection title="Date identificare" cols={3}>
//   <FormField name="name" ... />
//   <FormField name="cui" ... />
//   <FormField name="regCom" ... />
// </FormSection>
// <FormSection title="Adresă" cols={2}>
//   <AddressInput ... />
// </FormSection>
```

---

## SplitLayout — Master-Detail

```tsx
// components/common/SplitLayout/SplitLayout.tsx
import { cn } from '@/lib/utils';

interface SplitLayoutProps {
  master:      React.ReactNode;
  detail:      React.ReactNode;
  masterWidth?: string;    // '280px' | '320px' | '360px'
  className?:  string;
}

export function SplitLayout({
  master,
  detail,
  masterWidth = '320px',
  className,
}: SplitLayoutProps) {
  return (
    <div className={cn('flex h-full overflow-hidden', className)}>
      {/* Master — lista stânga */}
      <div
        className="shrink-0 border-r border-border-default overflow-y-auto"
        style={{ width: masterWidth }}
      >
        {master}
      </div>

      {/* Detail — dreapta */}
      <div className="flex-1 overflow-y-auto">
        {detail}
      </div>
    </div>
  );
}

// Utilizare tipică
function InvoicesPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);

  return (
    <SplitLayout
      master={
        <InvoiceList
          onSelect={setSelectedId}
          selectedId={selectedId}
        />
      }
      detail={
        selectedId
          ? <InvoiceDetail id={selectedId} />
          : <div className="flex items-center justify-center h-full
                            text-text-muted text-sm">
              Selectați o factură
            </div>
      }
    />
  );
}
```

---

## SidePanel — Drawer Edit Rapid

```tsx
// components/common/SidePanel/SidePanel.tsx
import { Sheet, SheetContent, SheetHeader,
         SheetTitle, SheetDescription } from '@/components/ui/sheet';

interface SidePanelProps {
  open:          boolean;
  onClose:       () => void;
  title:         string;
  description?:  string;
  children:      React.ReactNode;
  size?:         'sm' | 'md' | 'lg';    // 400px | 600px | 800px
  footer?:       React.ReactNode;        // butoane Save/Cancel
}

const sizeClass = {
  sm: 'w-[400px]',
  md: 'w-[600px]',
  lg: 'w-[800px]',
};

export function SidePanel({
  open,
  onClose,
  title,
  description,
  children,
  size    = 'md',
  footer,
}: SidePanelProps) {
  return (
    <Sheet open={open} onOpenChange={(o) => !o && onClose()}>
      <SheetContent
        side="right"
        className={cn('flex flex-col p-0', sizeClass[size])}
      >
        <SheetHeader className="px-6 py-4 border-b border-border-default">
          <SheetTitle className="text-base">{title}</SheetTitle>
          {description && (
            <SheetDescription className="text-xs">{description}</SheetDescription>
          )}
        </SheetHeader>

        <div className="flex-1 overflow-y-auto px-6 py-4">
          {children}
        </div>

        {footer && (
          <div className="border-t border-border-default px-6 py-4 flex
                          justify-end gap-3 bg-surface-subtle">
            {footer}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}

// Utilizare
function InvoiceList() {
  const [editId, setEditId] = useState<string | null>(null);

  return (
    <>
      <DataTable ... />

      <SidePanel
        open={Boolean(editId)}
        onClose={() => setEditId(null)}
        title="Editare factură"
        size="lg"
        footer={
          <>
            <Button variant="outline" onClick={() => setEditId(null)}>
              Anulează
            </Button>
            <Button onClick={handleSave}>Salvează</Button>
          </>
        }
      >
        <InvoiceForm id={editId!} />
      </SidePanel>
    </>
  );
}
```
