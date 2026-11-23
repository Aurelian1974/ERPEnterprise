import { useState } from 'react';
import { useSearchParams } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';
import { fileToBase64 } from './bankApi';
import { formatAmount } from './cashApi';
import { downloadFile } from './paymentOrderApi';

type OpeningImport = components['schemas']['OpeningImportDto'];
const shownIssues = 50;
const statusPills: Readonly<Record<number, string>> = { 1: 'pill info', 2: 'pill ok', 3: 'pill', 4: 'pill warn' };
const download = (file: components['schemas']['DownloadedFile']) =>
  downloadFile({ fileName: file.fileName, contentType: file.contentType, contentBase64: file.content });

/**
 * Configurare › Finanțe › Solduri inițiale (02.02, F3 4.9; mecanismul de import 02.01, 4.8): șablonul, încărcarea și validarea, raportul
 * (rânduri valide, deja importate, cu erori; totalurile pe rol și monedă de comparat cu balanța de pornire), confirmarea în loturi și
 * rândurile cu erori ca Excel. Importul curent stă în URL (?import=12).
 */
export function OpeningBalancesTab() {
  const [params, setParams] = useSearchParams();
  const jobId = Number(params.get('import')) || undefined;
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [file, setFile] = useState<File | undefined>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const job = useQuery({
    queryKey: ['finance', 'opening-balances', jobId ?? 0],
    enabled: jobId !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/opening-balances/{id}', { params: { path: { id: jobId ?? 0 } } })),
  });

  const run = async (action: () => Promise<OpeningImport | undefined>, message?: TextKey) => {
    setError(undefined);
    setBusy(true);
    try {
      const next = await action();
      if (next !== undefined) {
        queryClient.setQueryData(['finance', 'opening-balances', next.jobId], next);
        setParams({ import: String(next.jobId) });
      }
      if (message !== undefined) notify(t(message));
      await queryClient.invalidateQueries({ queryKey: ['finance', 'settlement'] });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    } finally {
      setBusy(false);
    }
  };

  const upload = () => run(async () => {
    if (file === undefined) return undefined;
    return unwrap(await api.POST('/api/finance/opening-balances', { body: { fileName: file.name, fileBase64: await fileToBase64(file) } }));
  }, 'open.validated');

  return (
    <section className="card" aria-labelledby="opening-title">
      <div className="card-head">
        <h2 id="opening-title">{t('open.title')}</h2>
        {jobId !== undefined && <button type="button" className="btn ghost" onClick={() => { setParams({}); setFile(undefined); }}>{t('open.new')}</button>}
      </div>
      <p className="muted small">{t('open.help')}</p>
      {jobId === undefined ? (
        <div className="order-from-invoices">
          <button type="button" className="btn ghost"
            onClick={() => void run(async () => { download(unwrap(await api.GET('/api/finance/opening-balances/template'))); return undefined; })}>
            {t('open.template')}
          </button>
          <label>{t('open.file')}<input type="file" accept=".xlsx" onChange={(e) => setFile(e.target.files?.[0])} /></label>
          <button type="button" className="btn primary" disabled={file === undefined || busy} onClick={() => void upload()}>{t('open.upload')}</button>
        </div>
      ) : job.data === undefined ? (
        <p className="muted" role="status">{job.isError ? job.error.message : t('app.loading')}</p>
      ) : (
        <Report job={job.data} busy={busy} run={run} />
      )}
      <ErrorBanner message={error} />
    </section>
  );
}

function Report({ job, busy, run }: {
  readonly job: OpeningImport;
  readonly busy: boolean;
  readonly run: (action: () => Promise<OpeningImport | undefined>, message?: TextKey) => Promise<void>;
}) {
  const id = job.jobId;
  const issues = [...job.errors, ...job.warnings];
  const status = (['open.status.1', 'open.status.2', 'open.status.3', 'open.status.4'] as const)[job.status - 1] ?? 'open.status.1';
  return (
    <div>
      <p><span className="strong">{job.fileName}</span> <span className={statusPills[job.status] ?? 'pill'}>{t(status)}</span></p>
      {job.lastError != null && <div className="banner warn" role="status">{t('open.lastError', { error: job.lastError })}</div>}
      <div className="kpi-row">
        <div className="kpi"><div className="kpi-label">{t('open.rows')}</div><div className="kpi-value">{job.totalRows.toLocaleString('ro-RO')}</div></div>
        <div className="kpi"><div className="kpi-label">{t('open.valid')}</div><div className="kpi-value">{job.validRows.toLocaleString('ro-RO')}</div></div>
        <div className="kpi"><div className="kpi-label">{t('open.existing')}</div><div className="kpi-value">{job.existingRows.toLocaleString('ro-RO')}</div></div>
        <div className={job.errorRows > 0 ? 'kpi bad' : 'kpi'}><div className="kpi-label">{t('open.errors')}</div><div className="kpi-value">{job.errorRows.toLocaleString('ro-RO')}</div></div>
        <div className="kpi"><div className="kpi-label">{t('open.imported')}</div><div className="kpi-value">{job.importedRows.toLocaleString('ro-RO')}</div></div>
      </div>
      {job.totals.length > 0 && (
        <>
          <h3 className="section-title">{t('open.totals')}</h3>
          <table className="partner-list cash-list">
            <thead>
              <tr>
                <th scope="col">{t('open.col.role')}</th><th scope="col">{t('open.col.currency')}</th><th scope="col" className="num">{t('open.col.rows')}</th>
                <th scope="col" className="num">{t('open.col.total')}</th><th scope="col" className="num">{t('open.col.totalRon')}</th>
              </tr>
            </thead>
            <tbody>
              {job.totals.map((x) => (
                <tr key={`${x.role}-${x.currencyCode}`}>
                  <td>{t(x.role === 1 ? 'settle.role.customer' : 'settle.role.supplier')}</td><td>{x.currencyCode}</td>
                  <td className="num">{x.rows.toLocaleString('ro-RO')}</td><td className="num">{formatAmount(x.total)}</td><td className="num">{formatAmount(x.totalRon)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      {issues.length > 0 && (
        <table className="partner-list cash-list">
          <thead>
            <tr><th scope="col">{t('open.col.row')}</th><th scope="col">{t('open.col.column')}</th><th scope="col">{t('open.col.message')}</th></tr>
          </thead>
          <tbody>
            {issues.slice(0, shownIssues).map((i, n) => (
              <tr key={n}><td>{i.row}</td><td>{i.column}</td><td>{i.message}{i.value == null ? '' : ` („${i.value}”)`}</td></tr>
            ))}
          </tbody>
        </table>
      )}
      {issues.length > shownIssues && <p className="muted small">{t('open.moreErrors', { count: shownIssues })}</p>}
      <div className="actions">
        {job.errorRows > 0 && (
          <button type="button" className="btn ghost" onClick={() => void run(async () => {
            download(unwrap(await api.GET('/api/finance/opening-balances/{id}/errors', { params: { path: { id } } })));
            return undefined;
          })}>{t('open.downloadErrors')}</button>
        )}
        {job.status === 1 && (
          <>
            <button type="button" className="btn ghost" disabled={busy}
              onClick={() => void run(async () => unwrap(await api.POST('/api/finance/opening-balances/{id}/cancel', { params: { path: { id } } })), 'open.cancelled')}>
              {t('open.cancel')}
            </button>
            <button type="button" className="btn primary" disabled={busy || job.validRows === 0}
              onClick={() => void run(async () => unwrap(await api.POST('/api/finance/opening-balances/{id}/confirm', { params: { path: { id } } })), 'open.confirmed')}>
              {t('open.confirm')}
            </button>
          </>
        )}
      </div>
    </div>
  );
}
