import { useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { downloadFile, fileToBase64 } from '../api/download';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { formatDate } from '../format/date';
import { t, type TextKey } from '../i18n';
import { partnerKeys } from '../partners/partnerApi';

type ImportJob = components['schemas']['ImportJobDto'];
type Issue = components['schemas']['ImportIssueDto'];

const importKeys = {
  job: (id: number) => ['core', 'import', id] as const,
  history: ['core', 'import', 'history'] as const,
};

/** Cate probleme se afiseaza in tabel; toate sunt in fisierul cu randurile gresite. */
export const SHOWN_ISSUES = 200;

const statusKey = (status: number) => `import.status.${status}` as TextKey;

/**
 * Configurare › Import (macheta 07g; fluxul 02.01, 4.8): sablonul, incarcarea si validarea, raportul cu erori si avertizari,
 * confirmarea (loturi de 500 pe server) si randurile cu erori ca Excel. Importul curent sta in URL (?import=12).
 */
export function ImportTab() {
  const [params, setParams] = useSearchParams();
  const jobId = Number(params.get('import')) || undefined;
  const job = useQuery({
    queryKey: importKeys.job(jobId ?? 0),
    enabled: jobId !== undefined,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/import/{id}', { params: { path: { id: jobId ?? 0 } }, signal })),
  });
  const history = useQuery({
    queryKey: importKeys.history,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/import', { params: { query: { entityType: 'Partner' } }, signal })),
  });
  const queryClient = useQueryClient();
  const open = (next: ImportJob | undefined) => {
    if (next !== undefined) {
      queryClient.setQueryData(importKeys.job(next.id), next);
    }
    setParams(next === undefined ? {} : { import: String(next.id) });
    void queryClient.invalidateQueries({ queryKey: importKeys.history });
  };

  return (
    <section className="card import-card" aria-labelledby="import-title">
      <div className="card-head">
        <h2 id="import-title">{t('import.title')}</h2>
        {jobId !== undefined && (
          <button type="button" className="btn primary" onClick={() => open(undefined)}>
            {t('import.new')}
          </button>
        )}
      </div>
      <Steps current={jobId === undefined ? 1 : job.data?.status === 1 ? 4 : 5} />
      {jobId === undefined ? (
        <UploadStep onValidated={open} />
      ) : job.data === undefined ? (
        <p className="muted" role="status">{job.isError ? job.error.message : t('app.loading')}</p>
      ) : (
        <Report job={job.data} onChanged={open} />
      )}
      <div className="import-footer muted small">
        <span>
          {history.data === undefined || history.data.length === 0
            ? t('import.historyEmpty')
            : `${t('import.history', { count: history.data.length })} · ${history.data.slice(0, 3).map((h) => t('import.historyItem', {
                file: h.fileName, date: formatDate(new Date(h.createdAt)), rows: h.totalRows.toLocaleString('ro-RO'), status: t(statusKey(h.status)).toLocaleLowerCase('ro-RO'),
              })).join(' · ')}`}
        </span>
        <span>{t('import.footer')}</span>
      </div>
    </section>
  );
}

function Steps({ current }: { readonly current: number }) {
  const steps: TextKey[] = ['import.step.entity', 'import.step.template', 'import.step.upload', 'import.step.report', 'import.step.confirm'];
  return (
    <ol className="steps" aria-label={t('import.title')}>
      {steps.map((key, index) => {
        const number = index + 1;
        const state = number < current || (current === 1 && number <= 3) ? 'done' : number === current ? 'current' : 'todo';
        return (
          <li key={key} className={`step ${state}`} aria-current={state === 'current' ? 'step' : undefined}>
            <span className="step-mark" aria-hidden="true">{state === 'done' && current > 1 ? '✓' : number}</span>
            {t(key)}
          </li>
        );
      })}
    </ol>
  );
}

function UploadStep({ onValidated }: { readonly onValidated: (job: ImportJob) => void }) {
  const { notify } = useNotifier();
  const input = useRef<HTMLInputElement | null>(null);
  const [file, setFile] = useState<File | undefined>();
  const [anafCheck, setAnafCheck] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | undefined>();

  async function template() {
    try {
      await downloadFile('/api/core/import/partners/template', {}, 'sablon-import-parteneri.xlsx');
    } catch (e) {
      notify(e instanceof Error ? e.message : t('app.loadError'), 'error');
    }
  }

  async function validate() {
    if (file === undefined) {
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      const job = unwrap(await api.POST('/api/core/import/partners', {
        body: { fileName: file.name, fileBase64: await fileToBase64(file), anafCheck },
      }));
      onValidated(job);
    } catch (e) {
      const message = e instanceof ApiError ? (e.fieldErrors.file?.join(' ') ?? e.message) : t('app.loadError');
      setError(message);
      notify(message, 'error');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="import-upload">
      <ErrorBanner message={error} />
      <p className="muted">{t('import.templateHint')}</p>
      <div>
        <button type="button" className="btn ghost" onClick={() => void template()}>
          {t('import.template')}
        </button>
      </div>
      <div className="field">
        <label htmlFor="import-file">{t('import.file')}</label>
        <input id="import-file" ref={input} type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          onChange={(e) => setFile(e.target.files?.[0])} />
      </div>
      <label className="check-row">
        <input type="checkbox" checked={anafCheck} onChange={(e) => setAnafCheck(e.target.checked)} />
        {t('import.anaf')}
      </label>
      <p className="muted small">{t('import.anafHint')}</p>
      <div className="actions">
        <button type="button" className="btn primary" disabled={file === undefined || busy} onClick={() => void validate()}>
          {busy ? t('import.validating') : t('import.validate')}
        </button>
      </div>
    </div>
  );
}

function Report({ job, onChanged }: { readonly job: ImportJob; readonly onChanged: (job: ImportJob) => void }) {
  const { notify } = useNotifier();
  const queryClient = useQueryClient();
  const [list, setList] = useState<'errors' | 'warnings'>(job.errors.length > 0 ? 'errors' : 'warnings');
  const [busy, setBusy] = useState(false);
  const issues: readonly Issue[] = list === 'errors' ? job.errors : job.warnings;
  const pending = job.status === 1;

  async function run(action: () => Promise<ImportJob>) {
    setBusy(true);
    try {
      const next = await action();
      onChanged(next);
      if (next.status === 2) {
        await queryClient.invalidateQueries({ queryKey: partnerKeys.all });
        notify(t('import.doneText', { count: next.importedRows.toLocaleString('ro-RO'), total: next.totalRows.toLocaleString('ro-RO') }));
      } else if (next.status === 4) {
        await queryClient.invalidateQueries({ queryKey: partnerKeys.all });
        notify(next.lastError ?? t(statusKey(4)), 'error');
      }
    } catch (e) {
      notify(e instanceof Error ? e.message : t('app.loadError'), 'error');
    } finally {
      setBusy(false);
    }
  }

  async function errorsFile() {
    try {
      await downloadFile(`/api/core/import/${job.id}/errors`, {}, 'erori-import.xlsx');
    } catch (e) {
      notify(e instanceof Error ? e.message : t('app.loadError'), 'error');
    }
  }

  return (
    <div className="import-report">
      <div className="import-main">
        <div className="tiles">
          <div className="tile">
            <span className="tile-label">{t('import.tile.total')}</span>
            <strong>{job.totalRows.toLocaleString('ro-RO')}</strong>
            <span className="muted small">{job.fileName}</span>
          </div>
          <div className="tile ok">
            <span className="tile-label">{t('import.tile.valid')}</span>
            <strong>{job.validRows.toLocaleString('ro-RO')}</strong>
            <span className="muted small">{t('import.tile.validHint')}</span>
          </div>
          <div className="tile bad">
            <span className="tile-label">{t('import.tile.errors')}</span>
            <strong>{job.errorRows.toLocaleString('ro-RO')}</strong>
            <span className="muted small">{t('import.tile.errorsHint')}</span>
          </div>
          <div className="tile warn">
            <span className="tile-label">{t('import.tile.warnings')}</span>
            <strong>{job.warningRows.toLocaleString('ro-RO')}</strong>
            <span className="muted small">{t('import.tile.warningsHint')}</span>
          </div>
        </div>
        {!pending && (
          <div className={job.status === 2 ? 'banner info' : job.status === 4 ? 'banner error' : 'banner'} role="status">
            <strong>{t(statusKey(job.status))}.</strong>{' '}
            {job.status === 2 && t('import.doneText', { count: job.importedRows.toLocaleString('ro-RO'), total: job.totalRows.toLocaleString('ro-RO') })}
            {job.status === 4 && t('import.partialText', { count: job.importedRows.toLocaleString('ro-RO'), error: job.lastError ?? '' })}
          </div>
        )}
        <div className="card issues">
          <div className="card-head">
            <h3>
              {t(list === 'errors' ? 'import.errors' : 'import.warnings')}{' '}
              {issues.length > SHOWN_ISSUES && <span className="muted small">{t('import.shown', { shown: SHOWN_ISSUES, total: issues.length })}</span>}
            </h3>
            <div className="chips-row">
              <button type="button" className={list === 'errors' ? 'chip on' : 'chip'} aria-pressed={list === 'errors'} onClick={() => setList('errors')}>
                {t('import.errors')} ({job.errors.length})
              </button>
              <button type="button" className={list === 'warnings' ? 'chip on' : 'chip'} aria-pressed={list === 'warnings'} onClick={() => setList('warnings')}>
                {t('import.warnings')} ({job.warnings.length})
              </button>
            </div>
          </div>
          {issues.length === 0 ? (
            <p className="muted">{t('import.none')}</p>
          ) : (
            <table className="simple issues-table">
              <thead>
                <tr>
                  <th scope="col">{t('import.col.row')}</th>
                  <th scope="col">{t('import.col.column')}</th>
                  <th scope="col">{t('import.col.value')}</th>
                  <th scope="col">{t('import.col.problem')}</th>
                </tr>
              </thead>
              <tbody>
                {issues.slice(0, SHOWN_ISSUES).map((issue, index) => (
                  <tr key={`${issue.row}-${issue.column}-${index}`}>
                    <td className="num">{issue.row === 0 ? '—' : issue.row}</td>
                    <td><strong>{issue.column}</strong></td>
                    <td className="mono">{issue.value ?? '—'}</td>
                    <td className={list === 'errors' ? 'problem bad' : 'problem warn'}>{issue.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
      <aside className="import-side">
        <div className="card next">
          <h3 className="section">{t('import.next')}</h3>
          <p>{t('import.nextText', { count: job.validRows.toLocaleString('ro-RO') })}</p>
        </div>
        <dl className="facts">
          <dt>{t('import.mode')}</dt>
          <dd>{t('import.modeText')}</dd>
          <dt>{t('import.anafLabel')}</dt>
          <dd>{t(job.anafCheck ? 'common.yes' : 'common.no')}</dd>
          <dt>{t('import.company')}</dt>
          <dd>{t('import.companyText')}</dd>
        </dl>
        {job.errorRows > 0 && (
          <button type="button" className="btn ghost wide" onClick={() => void errorsFile()}>
            {t('import.downloadErrors', { count: job.errorRows.toLocaleString('ro-RO') })}
          </button>
        )}
        {pending && (
          <>
            <button type="button" className="btn primary wide" disabled={busy || job.validRows === 0}
              onClick={() => void run(async () => unwrap(await api.POST('/api/core/import/{id}/confirm', { params: { path: { id: job.id } } })))}>
              {busy ? t('import.confirming') : t('import.confirm', { count: job.validRows.toLocaleString('ro-RO') })}
            </button>
            <button type="button" className="btn ghost wide" disabled={busy}
              onClick={() => void run(async () => unwrap(await api.POST('/api/core/import/{id}/cancel', { params: { path: { id: job.id } } })))}>
              {t('import.cancel')}
            </button>
          </>
        )}
      </aside>
    </div>
  );
}
