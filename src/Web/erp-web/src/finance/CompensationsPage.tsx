import { useRef, useState } from 'react';
import { useOutletContext } from 'react-router';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { isoToday } from '../users/userModel';
import { fileToBase64 } from './bankApi';
import { formatAmount } from './cashApi';
import { downloadFile } from './paymentOrderApi';
import { Role } from './settlementApi';

type Compensation = components['schemas']['CompensationDetailDto'];
type CompensationItem = components['schemas']['CompensationItemDto'];
type Filter = 'all' | 'drafts' | 'unsigned';

const filters: readonly { readonly value: Filter; readonly label: TextKey }[] = [
  { value: 'all', label: 'comp.filter.all' },
  { value: 'drafts', label: 'comp.filter.drafts' },
  { value: 'unsigned', label: 'comp.filter.unsigned' },
];

const statusLabels: Readonly<Record<number, TextKey>> = { 1: 'comp.status.draft', 2: 'comp.status.validated', 3: 'comp.status.reversed' };
const statusPills: Readonly<Record<number, string>> = { 1: 'pill warn', 2: 'pill ok', 3: 'pill' };
const kindLabels: Readonly<Record<number, TextKey>> = { 1: 'comp.kind.1', 2: 'comp.kind.2', 3: 'comp.kind.3', 4: 'comp.kind.4' };
const keys = { all: ['finance', 'compensations'] as const };

/** Suma liniilor unui PV, in centi (echilibrul: 0). */
export function balanceOf(lines: readonly { readonly amount: string }[]): number {
  return lines.reduce((sum, l) => sum + Math.round(Number(l.amount) * 100), 0);
}

/** Fisierul descarcat (PDF tiparit sau scanul semnat), din raspunsul API (continut Base64). */
const download = (file: components['schemas']['DownloadedFile']) =>
  downloadFile({ fileName: file.fileName, contentType: file.contentType, contentBase64: file.content });

/**
 * Decontări › Compensări (02.02, F3 3.4, 4.4; macheta f3-05): lista PV-urilor (toate, ciorne, validate fără PV semnat), propunerile automate
 * pentru partenerii client și furnizor, fișa PV-ului cu creanțele și datoriile stinse, echilibrul, TVA-ul la încasare, PV-ul semnat atașat,
 * tipărirea, validarea și stornarea.
 */
export function CompensationsPage() {
  const user = useOutletContext<CurrentUser>();
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [filter, setFilter] = useState<Filter>('all');
  const [search, setSearch] = useState('');
  const [year, setYear] = useState(Number(isoToday().slice(0, 4)));
  const [selected, setSelected] = useState<number | undefined>();
  const [error, setError] = useState<string | undefined>();
  const list = useQuery({
    queryKey: [...keys.all, 'list', filter, search, year],
    queryFn: async () => unwrap(await api.GET('/api/finance/compensations', { params: { query: {
      year, ...(filter === 'drafts' ? { status: 1 } : {}), ...(filter === 'unsigned' ? { withoutSigned: true } : {}),
      ...(search.trim() === '' ? {} : { search: search.trim() }) } } })),
    placeholderData: keepPreviousData,
  });
  const proposals = useQuery({
    queryKey: [...keys.all, 'proposals'],
    queryFn: async () => unwrap(await api.GET('/api/finance/compensations/proposals')),
  });
  const canCreate = user.permissions.includes('Fin.Compensation.Create');
  const items = list.data ?? [];
  const current = selected ?? items[0]?.id;

  const fromProposal = async (partnerId: number, currencyCode: string) => {
    setError(undefined);
    try {
      const lines = unwrap(await api.GET('/api/finance/compensations/propose', { params: { query: { partnerId, currencyCode } } }));
      const saved = unwrap(await api.POST('/api/finance/compensations', { body: { id: null, documentDate: isoToday(), kind: 1, externalRef: null, currencyCode,
        note: null, lines, rowVersion: null } }));
      notify(t('comp.created'));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      setFilter('all');
      setSelected(saved.id);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.settlements')} › {t('menu.compensations')}</div>
      <div className="head">
        <div>
          <h1>{t('comp.title')}</h1>
          <div className="sub2">{t('comp.subtitle')}</div>
        </div>
      </div>
      <div className="split cash-document">
        <div>
          <section className="card" aria-label={t('comp.title')}>
            <div className="toolbar-row">
              <input className="cell-input search" type="search" value={search} aria-label={t('comp.search')} placeholder={t('comp.search')}
                onChange={(e) => setSearch(e.target.value)} />
              <div className="chips-row" role="group" aria-label={t('comp.filter')}>
                {filters.map((f) => (
                  <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                    onClick={() => setFilter(f.value)}>{t(f.label)}</button>
                ))}
              </div>
              <label className="inline-field">{t('comp.year')}
                <input className="cell-input num year-input" type="number" value={year} onChange={(e) => setYear(Number(e.target.value))} />
              </label>
            </div>
            {list.isError && <ErrorBanner message={list.error.message} />}
            {list.data !== undefined && items.length === 0 ? <p className="muted">{t('comp.empty')}</p> : (
              <table className="partner-list cash-list" aria-busy={list.isFetching}>
                <thead>
                  <tr>
                    <th scope="col">{t('comp.col.pv')}</th>
                    <th scope="col" className="num">{t('settle.col.amount')}</th>
                    <th scope="col">{t('bank.col.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((c) => <ListRow key={c.id} item={c} selected={c.id === current} onSelect={() => setSelected(c.id)} />)}
                </tbody>
              </table>
            )}
          </section>
          <section className="card">
            <h2>{t('comp.proposals', { count: proposals.data?.length ?? 0 })}</h2>
            <ErrorBanner message={error} />
            {(proposals.data ?? []).length === 0 ? <p className="muted">{t('comp.noProposals')}</p> : (
              <ul className="proposal-list">
                {(proposals.data ?? []).map((p) => (
                  <li key={`${p.partnerId}-${p.currencyCode}`}>
                    <span>{p.partnerName}</span>
                    <span className="num strong">{formatAmount(p.amount)}{p.currencyCode === 'RON' ? '' : ` ${p.currencyCode}`}</span>
                    {canCreate && <button type="button" className="btn ghost small" onClick={() => void fromProposal(p.partnerId, p.currencyCode)}>
                      {t('comp.createDraft')}</button>}
                  </li>
                ))}
              </ul>
            )}
            <p className="muted small">{t('comp.proposalsHint')}</p>
          </section>
        </div>
        <aside className="card cash-side">
          {current === undefined ? <p className="muted">{t('comp.pick')}</p> : <Detail key={current} id={current} user={user} />}
        </aside>
      </div>
    </div>
  );
}

function ListRow({ item: c, selected, onSelect }: { readonly item: CompensationItem; readonly selected: boolean; readonly onSelect: () => void }) {
  return (
    <tr className={selected ? 'clickable picked' : 'clickable'} tabIndex={0} onClick={onSelect} onKeyDown={(e) => e.key === 'Enter' && onSelect()}>
      <td>
        <div className="strong">{c.number ?? t('comp.noNumber')} · {t(kindLabels[c.kind] ?? 'comp.kind.1')}</div>
        <div className="muted small">{c.partners ?? '—'} · {isoToRo(c.documentDate)}</div>
      </td>
      <td className="num">{formatAmount(c.totalAmount)}{c.currencyCode === 'RON' ? '' : ` ${c.currencyCode}`}</td>
      <td>
        <span className={statusPills[c.status] ?? 'pill'}>{t(statusLabels[c.status] ?? 'comp.status.draft')}</span>
        {c.status === 2 && !c.hasSignedScan && <span className="pill warn">{t('comp.noScan')}</span>}
      </td>
    </tr>
  );
}

function Detail({ id, user }: { readonly id: number; readonly user: CurrentUser }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const file = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | undefined>();
  const detail = useQuery({
    queryKey: [...keys.all, 'detail', id],
    queryFn: async () => unwrap(await api.GET('/api/finance/compensations/{id}', { params: { path: { id } } })),
  });
  if (detail.isError) return <ErrorBanner message={detail.error.message} />;
  const c = detail.data;
  if (c === undefined) return <p className="muted" role="status">{t('app.loading')}</p>;
  const can = (permission: string) => user.permissions.includes(permission);
  const receivables = c.lines.filter((l) => l.role === Role.Customer);
  const payables = c.lines.filter((l) => l.role === Role.Supplier);
  const balance = balanceOf(c.lines);
  const vatOnCollection = c.lines.some((l) => l.isVatOnCollection);

  const run = async (action: () => Promise<unknown>, message: TextKey) => {
    setError(undefined);
    try {
      await action();
      notify(t(message));
      await queryClient.invalidateQueries({ queryKey: keys.all });
      await queryClient.invalidateQueries({ queryKey: ['finance', 'settlement'] });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  const attach = async (picked: File | undefined) => {
    if (picked === undefined) return;
    await run(async () => unwrap(await api.POST('/api/finance/compensations/{id}/signed', { params: { path: { id } },
      body: { id, fileName: picked.name, content: await fileToBase64(picked) } })), 'comp.attached');
  };

  return (
    <div className="compensation">
      <h2>{c.number ?? t('comp.noNumber')} <span className={statusPills[c.status] ?? 'pill'}>{t(statusLabels[c.status] ?? 'comp.status.draft')}</span></h2>
      <p className="muted small">{t(kindLabels[c.kind] ?? 'comp.kind.1')} · {t('comp.dated', { date: isoToRo(c.documentDate) })} · {c.currencyCode}</p>
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">{t('comp.amount')}</div><div className="kpi-value">{formatAmount(c.totalAmount)}</div></div>
      </div>
      <LinesTable title={t('comp.receivables')} lines={receivables} />
      <LinesTable title={t('comp.payables')} lines={payables} />
      {balance === 0
        ? <div className="banner info">{t('comp.balanced')}</div>
        : <div className="banner warn">{t('comp.unbalanced', { amount: formatAmount((balance / 100).toFixed(2)) })}</div>}
      {vatOnCollection && <p className="muted small">{t('comp.vatNote')}</p>}
      <div className="banner">
        {c.signedAttachmentId == null ? t('comp.scanMissing') : (
          <button type="button" className="btn ghost small" onClick={() => void run(async () =>
            download(unwrap(await api.GET('/api/finance/compensations/{id}/signed', { params: { path: { id } } }))), 'comp.downloaded')}>{t('comp.openScan')}</button>
        )}
        {can('Fin.Compensation.Create') && c.status !== 3 && (
          <>
            <input ref={file} type="file" accept=".pdf,.png,.jpg,.jpeg,.tif,.tiff" hidden onChange={(e) => void attach(e.target.files?.[0])} />
            <button type="button" className="btn ghost small" onClick={() => file.current?.click()}>{t('comp.attach')}</button>
          </>
        )}
      </div>
      <p className="muted small">{t('comp.createdBy', { name: c.createdByName ?? '', date: isoToRo(c.createdAt.slice(0, 10)) })}</p>
      <ErrorBanner message={error} />
      <div className="actions">
        {c.status === 1 && can('Fin.Compensation.Create') && (
          <button type="button" className="btn ghost" onClick={() => window.confirm(t('comp.confirmDelete')) && void run(async () =>
            unwrap(await api.POST('/api/finance/compensations/{id}/delete', { params: { path: { id } }, body: { id, rowVersion: c.rowVersion } })), 'comp.deleted')}>
            {t('comp.delete')}</button>
        )}
        <button type="button" className="btn ghost" onClick={() => void run(async () =>
          download(unwrap(await api.GET('/api/finance/compensations/{id}/pdf', { params: { path: { id } } }))), 'comp.printed')}>{t('comp.print')}</button>
        {c.status === 1 && can('Fin.Compensation.Validate') && (
          <button type="button" className="btn primary" disabled={balance !== 0} onClick={() => void run(async () =>
            unwrap(await api.POST('/api/finance/compensations/{id}/validate', { params: { path: { id } }, body: { id, rowVersion: c.rowVersion } })), 'comp.validated')}>
            {t('comp.validate')}</button>
        )}
        {c.status === 2 && can('Fin.Compensation.Validate') && (
          <button type="button" className="btn ghost" onClick={() => window.confirm(t('comp.confirmReverse')) && void run(async () =>
            unwrap(await api.POST('/api/finance/compensations/{id}/reverse', { params: { path: { id } }, body: { id, rowVersion: c.rowVersion } })), 'comp.reversed')}>
            {t('comp.reverse')}</button>
        )}
      </div>
    </div>
  );
}

function LinesTable({ title, lines }: { readonly title: string; readonly lines: Compensation['lines'] }) {
  if (lines.length === 0) return null;
  const total = lines.reduce((sum, l) => sum + Math.abs(Number(l.amount)), 0);
  return (
    <div className="items-block">
      <h3 className="section-title">{title}</h3>
      <table className="partner-list cash-list">
        <thead>
          <tr>
            <th scope="col">{t('comp.col.invoice')}</th>
            <th scope="col" className="num">{t('settle.col.open')}</th>
            <th scope="col" className="num">{t('comp.col.settled')}</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((l) => (
            <tr key={l.lineNumber}>
              <td><div className="strong">{l.documentNo}</div><div className="muted small">{l.partnerName}</div></td>
              <td className="num">{formatAmount(Math.abs(Number(l.openAmount)).toFixed(2))}</td>
              <td className="num strong">{formatAmount(Math.abs(Number(l.amount)).toFixed(2))}</td>
            </tr>
          ))}
        </tbody>
        <tfoot><tr><td colSpan={2} className="strong">{t('settle.total')}</td><td className="num strong">{formatAmount(total.toFixed(2))}</td></tr></tfoot>
      </table>
    </div>
  );
}
