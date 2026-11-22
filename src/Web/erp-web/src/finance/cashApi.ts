import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

export type CashDesk = components['schemas']['CashDeskDto'];
export type CashDeskLookups = components['schemas']['CashDeskLookupsDto'];
export type CashDayDocument = components['schemas']['CashDayDocumentDto'];
export type CashDocumentDetail = components['schemas']['CashDocumentDetailDto'];
export type CashOperationType = components['schemas']['CashOperationTypeDto'];
export type SaveCashDocument = components['schemas']['SaveCashDocument'];

/** Felul documentului de casa (F1 3.3); aceleasi valori ca CashDocumentKind pe server. */
export const CashKind = { Receipt: 1, CashIn: 2, CashOut: 3, OpeningBalance: 4 } as const;
export type CashKindValue = (typeof CashKind)[keyof typeof CashKind];

export const CashStatus = { Draft: 1, Validated: 2, Reversed: 3 } as const;

/** Sensul tipului de operatiune: 1 intrare, 2 iesire. */
export const Direction = { In: 1, Out: 2 } as const;

export const cashKinds: readonly CashKindValue[] = [CashKind.Receipt, CashKind.CashIn, CashKind.CashOut, CashKind.OpeningBalance];

const kindLabels: Readonly<Record<CashKindValue, TextKey>> = {
  1: 'cash.kind.receipt',
  2: 'cash.kind.cashIn',
  3: 'cash.kind.cashOut',
  4: 'cash.kind.opening',
};

export const cashKindLabel = (kind: number): TextKey => kindLabels[kind as CashKindValue];

/** Chitanta, dispozitia de incasare si soldul initial sunt intrari; dispozitia de plata e iesire. */
export const directionOf = (kind: number) => (kind === CashKind.CashOut ? Direction.Out : Direction.In);

/** Tipurile de operatiuni potrivite felului documentului (regula 1 din F1 3.3). */
export function operationsFor(types: readonly CashOperationType[], kind: number): CashOperationType[] {
  const direction = directionOf(kind);
  return types.filter((type) => type.direction === direction);
}

export function statusLabel(document: { readonly status: number; readonly reversalOfId?: number | null }): TextKey {
  if (document.status === CashStatus.Draft) {
    return 'cash.status.draft';
  }
  if (document.status === CashStatus.Reversed) {
    return 'cash.status.reversed';
  }
  return document.reversalOfId == null ? 'cash.status.validated' : 'cash.status.reversal';
}

/** Filtrul listei zilei (macheta f1-02). */
export type DayFilter = 'all' | 'in' | 'out' | 'drafts';

export function filterDay(documents: readonly CashDayDocument[], filter: DayFilter): CashDayDocument[] {
  return documents.filter((d) =>
    filter === 'all' ? true : filter === 'drafts' ? d.status === CashStatus.Draft : directionOf(d.kind) === (filter === 'in' ? Direction.In : Direction.Out));
}

/** Suma unui document de casa pe coloana Incasare sau Plata (o stornare are suma negativa pe coloana documentului initial). */
export function amountColumns(document: CashDayDocument): { readonly cashIn: string | undefined; readonly cashOut: string | undefined } {
  return directionOf(document.kind) === Direction.In ? { cashIn: document.totalAmount, cashOut: undefined } : { cashIn: undefined, cashOut: document.totalAmount };
}

/** Suma invarianta „1234.50” afisata romaneste „1.234,50” (fara calcule cu number pe bani: doar formatarea sirului). */
export function formatAmount(value: string | null | undefined): string {
  if (value == null || value === '') {
    return '';
  }
  const negative = value.startsWith('-');
  const [whole = '0', fraction = '00'] = (negative ? value.slice(1) : value).split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return `${negative ? '−' : ''}${grouped},${fraction.padEnd(2, '0').slice(0, 2)}`;
}

export const cashKeys = {
  all: ['finance', 'cash'] as const,
  desk: (accountId: number | undefined, day: string | undefined) => ['finance', 'cash', 'desk', accountId ?? 0, day ?? ''] as const,
  document: (id: number) => ['finance', 'cash', 'document', id] as const,
};

/** Ecranul Casa: casele vizibile, ziua aleasa si documentele ei (fara casa sau zi: prima casa, azi). */
export function useCashDesk(accountId: number | undefined, day: string | undefined) {
  return useQuery({
    queryKey: cashKeys.desk(accountId, day),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/cash-desk', {
      params: { query: { ...(accountId === undefined ? {} : { TreasuryAccountId: accountId }), ...(day === undefined ? {} : { Day: day }) } },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
}

export function useCashDocument(id: number | undefined) {
  return useQuery({
    queryKey: cashKeys.document(id ?? 0),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/cash-documents/{id}', { params: { path: { id: id ?? 0 } }, signal })),
    enabled: id !== undefined,
  });
}

export const cashApi = {
  async save(body: SaveCashDocument) {
    return body.id === null
      ? unwrap(await api.POST('/api/finance/cash-documents', { body }))
      : unwrap(await api.PUT('/api/finance/cash-documents/{id}', { params: { path: { id: body.id } }, body }));
  },
  async remove(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/cash-documents/{id}/delete', { params: { path: { id } }, body: { rowVersion } }));
  },
  async validate(id: number, rowVersion: string, confirmWarnings: boolean) {
    return unwrap(await api.POST('/api/finance/cash-documents/{id}/validate', { params: { path: { id } }, body: { rowVersion, confirmWarnings } }));
  },
  async reverse(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/cash-documents/{id}/reverse', { params: { path: { id } }, body: { rowVersion } }));
  },
};
