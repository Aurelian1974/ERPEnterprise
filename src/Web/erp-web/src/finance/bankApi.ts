import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

export type BankStatementItem = components['schemas']['BankStatementItemDto'];
export type BankImportResult = components['schemas']['BankImportResultDto'];
export type BankReconciliation = components['schemas']['BankReconciliationDto'];
export type ReconciliationLine = components['schemas']['ReconciliationLineDto'];
export type BankLineDetail = components['schemas']['BankLineDetailDto'];
export type BankLinePartInput = components['schemas']['BankLinePartInput'];
export type MatchingResult = components['schemas']['MatchingResultDto'];

/** Starea liniei de extras (F2 3.4); aceleasi valori ca BankLineMatchStatus pe server. */
export const LineStatus = { Unmatched: 1, Proposed: 2, Explained: 3, Validated: 4 } as const;

/** Continuitatea extrasului (F2 3.3): 1 OK, 2 gol, 3 gol acceptat. */
export const Continuity = { Ok: 1, Gap: 2, GapAccepted: 3 } as const;

/** Starea extrasului (F2 3.3): 1 importat, 2 in reconciliere, 3 inchis. */
export const StatementStatus = { Imported: 1, Reconciling: 2, Closed: 3 } as const;

const lineLabels: Readonly<Record<number, TextKey>> = {
  1: 'bank.line.unmatched',
  2: 'bank.line.proposed',
  3: 'bank.line.explained',
  4: 'bank.line.validated',
};

const linePills: Readonly<Record<number, string>> = { 1: 'pill bad', 2: 'pill warn', 3: 'pill info', 4: 'pill ok' };

export const lineStatusLabel = (status: number): TextKey => lineLabels[status] ?? 'bank.line.unmatched';
export const lineStatusPill = (status: number): string => linePills[status] ?? 'pill';

const statementLabels: Readonly<Record<number, TextKey>> = { 1: 'bank.status.imported', 2: 'bank.status.reconciling', 3: 'bank.status.closed' };
export const statementStatusLabel = (status: number): TextKey => statementLabels[status] ?? 'bank.status.imported';

const sourceLabels: Readonly<Record<number, string>> = { 1: 'CAMT.053', 2: 'MT940', 3: 'CSV', 4: 'Manual', 5: 'Sold inițial' };
export const sourceLabel = (source: number): string => sourceLabels[source] ?? '';

/** Filtrul liniilor din ecranul Extras de cont (macheta f2-02). */
export type LineFilter = 'all' | 'in' | 'out' | 'proposed' | 'unmatched' | 'explained';

export function filterLines(lines: readonly ReconciliationLine[], filter: LineFilter): ReconciliationLine[] {
  return lines.filter((l) => {
    switch (filter) {
      case 'in':
        return !l.amount.startsWith('-');
      case 'out':
        return l.amount.startsWith('-');
      case 'proposed':
        return l.matchStatus === LineStatus.Proposed;
      case 'unmatched':
        return l.matchStatus === LineStatus.Unmatched;
      case 'explained':
        return l.matchStatus === LineStatus.Explained;
      default:
        return true;
    }
  });
}

/**
 * Restul de explicat al unei linii: suma liniei minus suma partilor, in bani intregi (fara erori de virgula mobila); sirurile sunt invariante
 * („-12.40”) sau introduse romaneste („-12,40”).
 */
export function remainder(lineAmount: string, parts: readonly { readonly amount: string }[]): string {
  const cents = (value: string): number => {
    const normalized = value.trim().replace(/\s/g, '').replace(',', '.');
    if (!/^-?\d+(\.\d{1,2})?$/.test(normalized)) {
      return 0;
    }
    const negative = normalized.startsWith('-');
    const [whole = '0', fraction = ''] = (negative ? normalized.slice(1) : normalized).split('.');
    const total = Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
    return negative ? -total : total;
  };
  const rest = cents(lineAmount) - parts.reduce((sum, p) => sum + cents(p.amount), 0);
  const sign = rest < 0 ? '-' : '';
  const absolute = Math.abs(rest);
  return `${sign}${Math.floor(absolute / 100)}.${String(absolute % 100).padStart(2, '0')}`;
}

/** Continutul unui fisier ca Base64 (importul extrasului trimite fisierul in corpul cererii). */
export async function fileToBase64(file: Blob): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
}

export const bankKeys = {
  all: ['finance', 'bank'] as const,
  statements: (accountId: number | undefined) => ['finance', 'bank', 'statements', accountId ?? 0] as const,
  reconciliation: (accountId: number) => ['finance', 'bank', 'reconciliation', accountId] as const,
  line: (id: number) => ['finance', 'bank', 'line', id] as const,
};

export type BankAccountOption = components['schemas']['BankAccountLookupDto'];

/** Conturile bancare active ale firmei (Bancă, Card, Depozit), cu dreptul Fin.Bank.View. */
export function useBankAccounts() {
  return useQuery({
    queryKey: ['finance', 'bank', 'accounts'] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/bank-accounts', { signal })),
  });
}

export function useBankStatements(accountId: number | undefined) {
  return useQuery({
    queryKey: bankKeys.statements(accountId),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/bank-statements', {
      params: { query: accountId === undefined ? {} : { TreasuryAccountId: accountId } },
      signal,
    })),
    placeholderData: keepPreviousData,
  });
}

export function useBankReconciliation(accountId: number | undefined) {
  return useQuery({
    queryKey: bankKeys.reconciliation(accountId ?? 0),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/bank-reconciliation/{accountId}', {
      params: { path: { accountId: accountId ?? 0 } },
      signal,
    })),
    enabled: accountId !== undefined,
    placeholderData: keepPreviousData,
  });
}

export function useBankLine(id: number | undefined) {
  return useQuery({
    queryKey: bankKeys.line(id ?? 0),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/bank-statement-lines/{id}', { params: { path: { id: id ?? 0 } }, signal })),
    enabled: id !== undefined,
  });
}

export const bankApi = {
  async importFile(treasuryAccountId: number | null, file: File) {
    return unwrap(await api.POST('/api/finance/bank-statements/import', {
      body: { treasuryAccountId, fileName: file.name, contentBase64: await fileToBase64(file) },
    }));
  },
  async acceptGap(id: number, reason: string, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/bank-statements/{id}/accept-gap', { params: { path: { id } }, body: { reason, rowVersion } }));
  },
  async autoMatch(treasuryAccountId: number) {
    return unwrap(await api.POST('/api/finance/bank-statements/auto-match', { body: { bankStatementId: null, treasuryAccountId } }));
  },
  async explain(id: number, parts: BankLinePartInput[], rowVersion: string) {
    return unwrap(await api.PUT('/api/finance/bank-statement-lines/{id}/explanation', { params: { path: { id } }, body: { parts, rowVersion } }));
  },
  async validate(lines: { id: number; rowVersion: string }[]) {
    return unwrap(await api.POST('/api/finance/bank-statement-lines/validate', { body: { lines } }));
  },
  async remember(id: number) {
    return unwrap(await api.POST('/api/finance/bank-statement-lines/{id}/remember', {
      params: { path: { id } },
      body: { bankStatementLineId: id, remittanceContains: null, autoValidate: false },
    }));
  },
};
