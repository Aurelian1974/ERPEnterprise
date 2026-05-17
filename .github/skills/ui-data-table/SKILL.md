---
name: ui-data-table
description: >-
  DataGrid Syncfusion pentru listing ERP: server-side pagination/sort/filter,
  selecție rânduri, export Excel/PDF, toolbar, column templates, locale română.
  Syncfusion Community Edition — înregistrare licență necesară.
---

# DataTable — Syncfusion Grid

## Când se aplică
Orice pagină de listing ERP (Facturi, Angajați, Produse, Parteneri, etc.)
cu sorting, filtrare, paginare server-side și export.

## Instalare
```bash
npm install @syncfusion/ej2-react-grids @syncfusion/ej2-react-navigations
npm install @syncfusion/ej2-react-buttons @syncfusion/ej2-react-dropdowns
npm install @syncfusion/ej2-react-excel-export @syncfusion/ej2-react-pdf-export
```

## Înregistrare licență (Community Edition — în main.tsx)
```tsx
import { registerLicense } from '@syncfusion/ej2-base';
registerLicense(import.meta.env.VITE_SYNCFUSION_LICENSE_KEY);
```

## CSS import (în main.tsx sau globals.css)
```tsx
import '@syncfusion/ej2-base/styles/material.css';
import '@syncfusion/ej2-react-grids/styles/material.css';
```

---

## 1. Wrapper component

```tsx
// components/common/DataTable/DataTable.tsx
import { useRef, useCallback } from 'react';
import {
  GridComponent, ColumnsDirective, ColumnDirective,
  Inject, Page, Sort, Filter, Toolbar,
  ExcelExport, PdfExport, Resize,
  type DataStateChangeEventArgs,
  type GridModel,
} from '@syncfusion/ej2-react-grids';
import { cn } from '@/lib/utils';

export interface DataTableColumn {
  field:        string;
  headerText:   string;
  width?:       number | string;
  minWidth?:    number;
  textAlign?:   'Left' | 'Right' | 'Center';
  format?:      string;             // 'N2', 'C2', 'dd.MM.yyyy'
  template?:    (row: any) => JSX.Element;
  isPrimaryKey?: boolean;
  visible?:     boolean;
  allowSorting?: boolean;
  allowFiltering?: boolean;
}

interface DataTableProps {
  columns:       DataTableColumn[];
  dataSource:    object[];
  totalCount:    number;
  pageSize?:     number;
  onStateChange: (state: { page: number; pageSize: number; sort?: string; filter?: string }) => void;
  loading?:      boolean;
  toolbar?:      ('Search' | 'ExcelExport' | 'PdfExport')[];
  allowSelection?: boolean;
  onRowSelected?: (data: any) => void;
  height?:        string;
  className?:     string;
}

export function DataTable({
  columns,
  dataSource,
  totalCount,
  pageSize     = 25,
  onStateChange,
  loading      = false,
  toolbar      = ['Search', 'ExcelExport'],
  allowSelection = false,
  onRowSelected,
  height       = 'auto',
  className,
}: DataTableProps) {
  const gridRef = useRef<GridComponent>(null);

  // Server-side state change (page, sort, filter)
  const handleDataStateChange = useCallback(
    (state: DataStateChangeEventArgs) => {
      const page     = (state.skip! / state.take!) + 1;
      const pageSize = state.take!;

      let sort: string | undefined;
      if (state.sorted?.length) {
        const s = state.sorted[0];
        sort = `${s.name} ${s.direction}`;
      }

      onStateChange({ page, pageSize, sort });
    },
    [onStateChange]
  );

  const handleToolbarClick = (args: any) => {
    if (args.item.id.includes('excelexport')) {
      gridRef.current?.excelExport();
    }
    if (args.item.id.includes('pdfexport')) {
      gridRef.current?.pdfExport();
    }
  };

  return (
    <div className={cn('syncfusion-erp-grid', loading && 'opacity-60', className)}>
      <GridComponent
        ref={gridRef}
        dataSource={{ result: dataSource, count: totalCount }}
        allowPaging
        allowSorting
        allowFiltering
        allowResizing
        allowExcelExport
        allowPdfExport
        enableStickyHeader
        height={height}
        pageSettings={{ pageSize, pageSizes: [10, 25, 50, 100] }}
        filterSettings={{ type: 'Menu' }}
        sortSettings={{ columns: [] }}
        toolbar={toolbar}
        dataStateChange={handleDataStateChange}
        toolbarClick={handleToolbarClick}
        rowSelected={onRowSelected ? (e) => onRowSelected(e.data) : undefined}
        selectionSettings={allowSelection
          ? { type: 'Multiple', mode: 'Row' }
          : { type: 'Single', mode: 'Row' }
        }
        locale="ro"
        loadingIndicator={{ indicatorType: 'Shimmer' }}
      >
        <ColumnsDirective>
          {columns.map((col) => (
            <ColumnDirective
              key={col.field}
              field={col.field}
              headerText={col.headerText}
              width={col.width}
              minWidth={col.minWidth ?? 80}
              textAlign={col.textAlign ?? 'Left'}
              format={col.format}
              template={col.template}
              isPrimaryKey={col.isPrimaryKey}
              visible={col.visible ?? true}
              allowSorting={col.allowSorting ?? true}
              allowFiltering={col.allowFiltering ?? true}
            />
          ))}
        </ColumnsDirective>
        <Inject services={[Page, Sort, Filter, Toolbar, ExcelExport, PdfExport, Resize]} />
      </GridComponent>
    </div>
  );
}
```

---

## 2. Locale română (ro.json)

```typescript
// lib/syncfusion-locale.ts — importat în main.tsx
import { L10n } from '@syncfusion/ej2-base';

L10n.load({
  ro: {
    grid: {
      EmptyRecord:         'Nu există înregistrări de afișat',
      GroupDropArea:       'Trageți un antet de coloană pentru a grupa după acea coloană',
      UnGroup:             'Faceți clic pentru a desgrupa',
      GroupCaption:        '  ',
      ActionFailure:       'Acțiunea a eșuat',
      Item:                'Element',
      Items:               'Elemente',
      EditOperationAlert:  'Nu sunt selectate înregistrări pentru editare',
      DeleteOperationAlert:'Nu sunt selectate înregistrări pentru ștergere',
      SaveButton:          'Salvează',
      OKButton:            'OK',
      CancelButton:        'Anulează',
      EditFormTitle:       'Detalii: ',
      AddFormTitle:        'Adaugă înregistrare nouă',
      BatchSaveConfirm:    'Sigur doriți să salvați modificările?',
      BatchSaveLostChanges:'Modificările nesalvate vor fi pierdute. Continuați?',
      ConfirmDelete:       'Sigur doriți să ștergeți această înregistrare?',
      CancelEdit:          'Sigur doriți să anulați modificările?',
      ChooseColumns:       'Alegeți coloanele',
      SearchColumns:       'Căutați coloane',
      Matchs:              'Nu s-au găsit rezultate',
      FilterButton:        'Filtrare',
      ClearButton:         'Ștergere',
      StartsWith:          'Începe cu',
      EndsWith:            'Se termină cu',
      Contains:            'Conține',
      Equal:               'Egal',
      NotEqual:            'Diferit',
      LessThan:            'Mai mic decât',
      LessThanOrEqual:     'Mai mic sau egal cu',
      GreaterThan:         'Mai mare decât',
      GreaterThanOrEqual:  'Mai mare sau egal cu',
      ChooseDate:          'Alegeți o dată',
      EnterValue:          'Introduceți valoarea',
      Copy:                'Copiere',
      Group:               'Grupare după această coloană',
      Ungroup:             'Desgruparetă după această coloană',
      autoFitAll:          'Ajustare automată toate',
      autoFit:             'Ajustare automată',
      Export:              'Export',
      ExcelExport:         'Export Excel',
      CsvExport:           'Export CSV',
      PdfExport:           'Export PDF',
      Pdfexport:           'Export PDF',
      Excelexport:         'Export Excel',
      Csvexport:           'Export CSV',
      Search:              'Căutare',
      Columnchooser:       'Coloane',
      FirstPage:           'Prima pagină',
      LastPage:            'Ultima pagină',
      PreviousPage:        'Pagina anterioară',
      NextPage:            'Pagina următoare',
      SortAscending:       'Sortare crescătoare',
      SortDescending:      'Sortare descrescătoare',
      EditRecord:          'Editare înregistrare',
      DeleteRecord:        'Ștergere înregistrare',
      FilterMenu:          'Filtru',
      SelectAll:           'Selectare totală',
      Blanks:              'Goluri',
      FilterTrue:          'Adevărat',
      FilterFalse:         'Fals',
      NoResult:            'Nu s-au găsit rezultate',
      ClearFilter:         'Eliminare filtru',
      NumberFilter:        'Filtru numeric',
      TextFilter:          'Filtru text',
      DateFilter:          'Filtru dată',
      MatchCase:           'Potrivire majuscule',
      Between:             'Între',
      CustomFilter:        'Filtru personalizat',
      CustomFilterPlaceHolder: 'Introduceți valoarea',
      CustomFilterDatePlaceHolder: 'Alegeți data',
      AND:                 'ȘI',
      OR:                  'SAU',
      ShowRowsWhere:       'Afișați rândurile unde:',
      currentPageInfo:     '{0} din {1} pagini',
      totalItemsInfo:      '({0} elemente)',
      firstPageTooltip:    'Mergeți la prima pagină',
      lastPageTooltip:     'Mergeți la ultima pagină',
      nextPageTooltip:     'Mergeți la pagina următoare',
      previousPageTooltip: 'Mergeți la pagina anterioară',
    },
    pager: {
      currentPageInfo:     '{0} din {1} pagini',
      totalItemsInfo:      '({0} elemente)',
      firstPageTooltip:    'Prima pagină',
      lastPageTooltip:     'Ultima pagină',
      nextPageTooltip:     'Pagina următoare',
      previousPageTooltip: 'Pagina anterioară',
    },
  },
});
```

---

## 3. Utilizare în feature

```tsx
// features/finance/invoices/InvoiceList.tsx
import { useState } from 'react';
import { DataTable } from '@/components/common/DataTable/DataTable';
import { StatusBadge, INVOICE_STATUS_MAP } from '@/components/common/StatusBadge/StatusBadge';
import { CurrencyDisplay } from '@/components/common/CurrencyInput/CurrencyInput';
import { useInvoices } from './api';
import { usePermission } from '@/hooks/usePermission';
import { Button } from '@/components/ui/button';
import { Link } from '@tanstack/react-router';

export function InvoiceList() {
  const [state, setState] = useState({ page: 1, pageSize: 25 });
  const canCreate = usePermission('finance.invoices.create');

  const { data, isLoading } = useInvoices({
    page:     state.page,
    pageSize: state.pageSize,
    sort:     state.sort,
  });

  const columns = [
    {
      field:      'invoiceNumber',
      headerText: 'Număr',
      width:      130,
      template:   (row: any) => (
        <Link
          to="/finance/invoices/$id"
          params={{ id: row.id }}
          className="text-primary-500 hover:underline font-mono"
        >
          {row.invoiceNumber}
        </Link>
      ),
    },
    { field: 'customerName', headerText: 'Client', minWidth: 150 },
    {
      field:      'status',
      headerText: 'Status',
      width:      130,
      template:   (row: any) => (
        <StatusBadge status={row.status} statusMap={INVOICE_STATUS_MAP} />
      ),
    },
    {
      field:      'dueDate',
      headerText: 'Scadent',
      width:      110,
      format:     'dd.MM.yyyy',
      textAlign:  'Center' as const,
    },
    {
      field:      'totalAmount',
      headerText: 'Total',
      width:      130,
      textAlign:  'Right' as const,
      template:   (row: any) => (
        <CurrencyDisplay value={row.totalAmount} currency="RON" />
      ),
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Facturi</h1>
        {canCreate && (
          <Link to="/finance/invoices/new">
            <Button>Factură nouă</Button>
          </Link>
        )}
      </div>

      <DataTable
        columns={columns}
        dataSource={data?.items ?? []}
        totalCount={data?.totalCount ?? 0}
        pageSize={state.pageSize}
        onStateChange={setState}
        loading={isLoading}
        toolbar={['Search', 'ExcelExport']}
      />
    </div>
  );
}
```

---

## 4. Stiluri ERP (override Syncfusion Material theme)

```css
/* styles/syncfusion-override.css */
.syncfusion-erp-grid .e-grid {
  font-family:  inherit;
  font-size:    14px;
  border-color: var(--color-border-default);
  border-radius: 6px;
  overflow: hidden;
}

.syncfusion-erp-grid .e-headercell {
  background-color: var(--color-surface-muted) !important;
  color:            var(--color-text-secondary);
  font-size:        11px;
  font-weight:      600;
  text-transform:   uppercase;
  letter-spacing:   0.04em;
}

.syncfusion-erp-grid .e-row:hover .e-rowcell {
  background-color: var(--color-surface-overlay) !important;
}

.syncfusion-erp-grid .e-pager {
  background-color: var(--color-surface-subtle);
  border-top:       1px solid var(--color-border-default);
  font-size:        12px;
}

/* Primary color override */
.syncfusion-erp-grid .e-btn.e-primary,
.syncfusion-erp-grid .e-pager .e-currentitem {
  background-color: var(--color-primary-500) !important;
  border-color:     var(--color-primary-500) !important;
}
```

## Reguli obligatorii
- `locale="ro"` pe GridComponent — întotdeauna
- `dataStateChange` — server-side, nu client-side pentru date reale
- `loadingIndicator={{ indicatorType: 'Shimmer' }}` — feedback vizual la loading
- License key în `.env` (`VITE_SYNCFUSION_LICENSE_KEY`) — niciodată hardcodat
- CSS override în fișier separat — nu inline styles
- `format: 'dd.MM.yyyy'` pe coloane dată — standard românesc
- `textAlign: 'Right'` pe coloane numerice/monetare
