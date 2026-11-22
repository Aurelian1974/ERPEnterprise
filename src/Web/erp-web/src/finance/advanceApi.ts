import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { TextKey } from '../i18n';

export type AdvancePage = components['schemas']['CashAdvancePageDto'];
export type AdvanceItem = components['schemas']['CashAdvanceItemDto'];
export type AdvanceLookups = components['schemas']['AdvanceLookupsDto'];
export type CreateAdvance = components['schemas']['CreateCashAdvance'];
export type ExpenseReportPage = components['schemas']['ExpenseReportPageDto'];
export type ExpenseReportDetail = components['schemas']['ExpenseReportDetailDto'];
export type SaveExpenseReport = components['schemas']['SaveExpenseReport'];

/** Scopul avansului (F1 3.5). */
export const Purpose = { CurrentExpenses: 1, DomesticTrip: 2, ForeignTrip: 3, Purchase: 4 } as const;
export const purposes = [Purpose.CurrentExpenses, Purpose.DomesticTrip, Purpose.ForeignTrip, Purpose.Purchase] as const;
export const isTrip = (purpose: number) => purpose === Purpose.DomesticTrip || purpose === Purpose.ForeignTrip;

/** Starea avansului: 0 de plătit, 1 acordat, 2 decontat parțial, 3 decontat, 4 depășit, 5 stornat. */
export const advanceStatusLabel = (status: number): TextKey =>
  (['advance.status.toPay', 'advance.status.granted', 'advance.status.partial', 'advance.status.settled', 'advance.status.overdue',
    'advance.status.reversed'] as const)[status] ?? 'advance.status.granted';

export const advancePill = (status: number) => (status === 4 ? 'pill bad' : status === 3 ? 'pill ok' : status === 5 ? 'pill off' : 'pill info');

export const purposeLabel = (purpose: number): TextKey =>
  (['advance.purpose.current', 'advance.purpose.domestic', 'advance.purpose.foreign', 'advance.purpose.purchase'] as const)[purpose - 1]
    ?? 'advance.purpose.current';

/** Starea decontului: 1 ciornă, 2 trimis, 3 aprobat, 4 validat, 5 stornat. */
export const ReportStatus = { Draft: 1, Submitted: 2, Approved: 3, Validated: 4, Reversed: 5 } as const;
export const reportStatusLabel = (status: number): TextKey =>
  (['report.status.draft', 'report.status.submitted', 'report.status.approved', 'report.status.validated', 'report.status.reversed'] as const)[status - 1]
    ?? 'report.status.draft';

/** Justificativul liniei (F1 3.7). */
export const Justification = { CompanyInvoice: 1, Receipt: 2, PerDiem: 3, Other: 4 } as const;
export const justificationLabel = (kind: number): TextKey =>
  (['report.justification.invoice', 'report.justification.receipt', 'report.justification.perDiem', 'report.justification.other'] as const)[kind - 1]
    ?? 'report.justification.other';

export const advanceKeys = {
  all: ['finance', 'advances'] as const,
  list: (filter: number) => ['finance', 'advances', 'list', filter] as const,
  reports: ['finance', 'advances', 'reports'] as const,
  report: (id: number) => ['finance', 'advances', 'report', id] as const,
};

export function useAdvances(filter: number) {
  return useQuery({
    queryKey: advanceKeys.list(filter),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/cash-advances', { params: { query: { Filter: filter } }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useExpenseReports() {
  return useQuery({
    queryKey: advanceKeys.reports,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/expense-reports', { signal })),
    placeholderData: keepPreviousData,
  });
}

export function useExpenseReport(id: number | undefined) {
  return useQuery({
    queryKey: advanceKeys.report(id ?? 0),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/finance/expense-reports/{id}', { params: { path: { id: id ?? 0 } }, signal })),
    enabled: id !== undefined,
  });
}

export const advanceApi = {
  async create(body: CreateAdvance) {
    return unwrap(await api.POST('/api/finance/cash-advances', { body }));
  },
  async saveReport(body: SaveExpenseReport) {
    return body.id === null
      ? unwrap(await api.POST('/api/finance/expense-reports', { body }))
      : unwrap(await api.PUT('/api/finance/expense-reports/{id}', { params: { path: { id: body.id } }, body }));
  },
  async submit(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/expense-reports/{id}/submit', { params: { path: { id } }, body: { rowVersion } }));
  },
  async approve(id: number, rowVersion: string) {
    return unwrap(await api.POST('/api/finance/expense-reports/{id}/approve', { params: { path: { id } }, body: { rowVersion } }));
  },
  async validate(id: number, rowVersion: string, settlementTreasuryAccountId: number | null) {
    return unwrap(await api.POST('/api/finance/expense-reports/{id}/validate', { params: { path: { id } }, body: { rowVersion, settlementTreasuryAccountId } }));
  },
};
