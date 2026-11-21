import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { ApiError } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { t } from '../i18n';
import { AUDIT_PAGE_SIZE, useAuditEntry, useAuditPage, useLoginEvents, type AuditFilter, type LoginEvent } from './auditApi';
import {
  actionOption, actionText, actionTone, diffChanges, entityLink, entityName, fieldLabel, INTERVALS, intervalStart, loginReason, whenLongText,
  whenText, type Interval,
} from './auditModel';

function problem(error: unknown): string {
  return error instanceof ApiError || error instanceof Error ? error.message : t('app.loadError');
}

const TONE = { warn: 'pill warn', error: 'pill bad', info: 'pill tag' } as const;

/** Inceputul si eticheta intervalului; inceputul se calculeaza o data, la alegere, ca interogarea sa nu se repete la fiecare randare. */
interface Range {
  readonly interval: Interval;
  readonly from: string | undefined;
}

/**
 * Configurare › Audit (02.01, 11; macheta 07h), doar citire: jurnalul cu filtre pe interval, utilizator, entitate si actiune,
 * detaliul intrarii (Camp / Inainte / Dupa) si comutatorul „Autentificări” pentru jurnalul de autentificari.
 */
export function AuditTab() {
  // ?entitate=Core.Company&id=5 (din Istoricul unei fise): tot istoricul inregistrarii, pe tot jurnalul.
  const [params] = useSearchParams();
  const linkedId = Number(params.get('id'));
  const linked = params.get('entitate') !== null && linkedId > 0 ? { type: params.get('entitate') ?? '', id: linkedId } : undefined;
  const [range, setRange] = useState<Range>(() => linked === undefined
    ? { interval: '7', from: intervalStart('7', new Date()) } : { interval: 'all', from: undefined });
  const [search, setSearch] = useState('');
  const [userId, setUserId] = useState<number | undefined>();
  const [entityType, setEntityType] = useState<string | undefined>();
  const [action, setAction] = useState<string | undefined>();
  const [record, setRecord] = useState<{ type: string; id: number } | undefined>(linked);
  const [page, setPage] = useState(1);
  const [logins, setLogins] = useState(false);
  const [success, setSuccess] = useState<boolean | undefined>();
  const [selected, setSelected] = useState<number | undefined>();

  const filter: AuditFilter = {
    from: range.from, userId, entityType: record?.type ?? entityType, entityId: record?.id, action, search, page,
  };
  const audit = useAuditPage(filter, !logins);
  const loginQuery = useLoginEvents({ from: range.from, success, search, page }, logins);
  const data = audit.data;
  const reset = <T,>(set: (value: T) => void) => (value: T) => { set(value); setPage(1); setSelected(undefined); };

  const total = logins ? loginQuery.data?.total ?? 0 : data?.total ?? 0;
  const rows = logins ? loginQuery.data?.events.length ?? 0 : data?.entries.length ?? 0;
  const pages = Math.max(1, Math.ceil(total / AUDIT_PAGE_SIZE));
  const first = (page - 1) * AUDIT_PAGE_SIZE + 1;
  const allCompanies = logins ? loginQuery.data?.allCompanies : data?.allCompanies;
  const error = logins ? loginQuery.error : audit.error;

  return (
    <div className="audit-layout">
      <section className="card audit-list" aria-label={t('config.tab.audit')}>
        <div className="role-filter audit-filters">
          <input className="search" type="search" placeholder={logins ? t('audit.login.search') : t('audit.search')}
            aria-label={logins ? t('audit.login.search') : t('audit.search')} value={search}
            onChange={(e) => reset(setSearch)(e.target.value)} />
          <select className="chip on" aria-label={t('audit.interval')} value={range.interval}
            onChange={(e) => {
              const interval = e.target.value as Interval;
              reset(setRange)({ interval, from: intervalStart(interval, new Date()) });
            }}>
            {INTERVALS.map((i) => <option key={i} value={i}>{t(`audit.interval.${i}`)}</option>)}
          </select>
          {logins ? (
            <select className={success === undefined ? 'chip' : 'chip on'} aria-label={t('audit.col.result')} value={success === undefined ? '' : String(success)}
              onChange={(e) => reset(setSuccess)(e.target.value === '' ? undefined : e.target.value === 'true')}>
              <option value="">{t('audit.login.all')}</option>
              <option value="true">{t('audit.login.ok')}</option>
              <option value="false">{t('audit.login.failed')}</option>
            </select>
          ) : (
            <>
              <select className={userId === undefined ? 'chip' : 'chip on'} aria-label={t('audit.user')} value={userId ?? ''}
                onChange={(e) => reset(setUserId)(e.target.value === '' ? undefined : Number(e.target.value))}>
                <option value="">{t('audit.allUsers')}</option>
                {(data?.users ?? []).map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
              </select>
              <select className={entityType === undefined ? 'chip' : 'chip on'} aria-label={t('audit.entity')} value={entityType ?? ''}
                disabled={record !== undefined} onChange={(e) => reset(setEntityType)(e.target.value === '' ? undefined : e.target.value)}>
                <option value="">{t('audit.allEntities')}</option>
                {(data?.entityTypes ?? []).map((x) => <option key={x} value={x}>{entityName(x)}</option>)}
              </select>
              <select className={action === undefined ? 'chip' : 'chip on'} aria-label={t('audit.action')} value={action ?? ''}
                onChange={(e) => reset(setAction)(e.target.value === '' ? undefined : e.target.value)}>
                <option value="">{t('audit.allActions')}</option>
                {[...(data?.actions ?? [])].sort((a, b) => actionOption(a).localeCompare(actionOption(b), 'ro'))
                  .map((x) => <option key={x} value={x}>{actionOption(x)}</option>)}
              </select>
            </>
          )}
          <button type="button" className={logins ? 'chip on' : 'chip'} aria-pressed={logins} onClick={() => reset(setLogins)(!logins)}>
            {t('audit.logins')}
          </button>
          {record !== undefined && !logins && (
            <button type="button" className="chip on" onClick={() => reset(setRecord)(undefined)} title={t('audit.clearRecord')}>
              {t('audit.recordFilter', { entity: entityName(record.type), id: record.id })} ✕
            </button>
          )}
        </div>
        {error !== null && <ErrorBanner message={problem(error)} />}
        {allCompanies === false && <p className="banner dashed small">{t('audit.partialScope')}</p>}
        <div className="matrix-scroll">
          {logins ? (
            <LoginTable events={loginQuery.data?.events} selected={selected} onSelect={setSelected} />
          ) : (
            <table className="simple audit-table">
              <thead>
                <tr>
                  <th scope="col">{t('audit.col.when')}</th>
                  <th scope="col">{t('audit.col.who')}</th>
                  <th scope="col">{t('audit.col.action')}</th>
                  <th scope="col">{t('audit.col.entity')}</th>
                  <th scope="col">{t('audit.col.record')}</th>
                </tr>
              </thead>
              <tbody>
                {data !== undefined && data.entries.length === 0 && <tr><td colSpan={5} className="muted">{t('audit.empty')}</td></tr>}
                {(data?.entries ?? []).map((e) => (
                  <tr key={e.id} className={e.id === selected ? 'current' : ''} onClick={() => setSelected(e.id)}>
                    <td className="nowrap">
                      <button type="button" className="row-link" aria-current={e.id === selected ? 'true' : undefined}
                        onClick={(ev) => { ev.stopPropagation(); setSelected(e.id); }}>{whenText(e.occurredAt)}</button>
                    </td>
                    <td>{e.userName}</td>
                    <td><span className={TONE[actionTone(e.action)]}>{actionText(e.action, e.isFirst)}</span></td>
                    <td>{entityName(e.entityType)}</td>
                    <td className="strong">{e.recordLabel ?? (e.entityId == null ? '—' : `#${e.entityId}`)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        <div className="audit-footer">
          <span className="muted small">
            {rows === 0 ? t('audit.footerEmpty') : t('audit.footer', {
              from: first, to: first + rows - 1, total: total.toLocaleString('ro-RO'),
            })}
          </span>
          <span className="audit-pager">
            <button type="button" className="btn ghost small" disabled={page <= 1} onClick={() => { setPage(page - 1); setSelected(undefined); }}>
              {t('audit.prev')}
            </button>
            <span className="muted small">{t('audit.page', { page, pages })}</span>
            <button type="button" className="btn ghost small" disabled={page >= pages} onClick={() => { setPage(page + 1); setSelected(undefined); }}>
              {t('audit.next')}
            </button>
          </span>
        </div>
      </section>

      <section className="card audit-detail">
        {logins ? (
          <LoginDetail event={loginQuery.data?.events.find((e) => e.id === selected)} />
        ) : selected === undefined ? (
          <p className="muted empty-detail">{t('audit.select')}</p>
        ) : (
          <EntryDetail id={selected} onRecordHistory={(type, id) => {
            setRecord({ type, id });
            setEntityType(undefined);
            setRange({ interval: 'all', from: undefined });
            setPage(1);
          }} />
        )}
      </section>
    </div>
  );
}

function LoginTable({ events, selected, onSelect }: {
  readonly events: readonly LoginEvent[] | undefined;
  readonly selected: number | undefined;
  readonly onSelect: (id: number) => void;
}) {
  return (
    <table className="simple audit-table">
      <thead>
        <tr>
          <th scope="col">{t('audit.col.when')}</th>
          <th scope="col">{t('audit.user')}</th>
          <th scope="col">{t('audit.col.result')}</th>
          <th scope="col">{t('audit.col.reason')}</th>
          <th scope="col">{t('audit.col.ip')}</th>
        </tr>
      </thead>
      <tbody>
        {events !== undefined && events.length === 0 && <tr><td colSpan={5} className="muted">{t('audit.empty')}</td></tr>}
        {(events ?? []).map((e) => (
          <tr key={e.id} className={e.id === selected ? 'current' : ''} onClick={() => onSelect(e.id)}>
            <td className="nowrap">
              <button type="button" className="row-link" aria-current={e.id === selected ? 'true' : undefined}
                onClick={(ev) => { ev.stopPropagation(); onSelect(e.id); }}>{whenText(e.occurredAt)}</button>
            </td>
            <td>
              {e.displayName ?? e.userName}
              {e.userId == null && <span className="muted small"> · {t('audit.login.unknownUser')}</span>}
            </td>
            <td><span className={e.success ? 'pill ok' : 'pill bad'}>{e.success ? t('audit.login.success') : t('audit.login.failure')}</span></td>
            <td className="muted">{loginReason(e.failureReason)}</td>
            <td className="mono">{e.ipAddress ?? '—'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function LoginDetail({ event }: { readonly event: LoginEvent | undefined }) {
  return (
    <div className="role-editor">
      <div className="role-editor-head">
        <h2>{t('audit.logins')}</h2>
        <span className="pill tag">{t('audit.readOnly')}</span>
      </div>
      {event !== undefined && (
        <dl className="audit-kv">
          <dt>{t('audit.user')}</dt><dd>{event.displayName ?? event.userName} <span className="muted">({event.userName})</span></dd>
          <dt>{t('audit.detail.when')}</dt><dd>{whenLongText(event.occurredAt)}</dd>
          <dt>{t('audit.col.result')}</dt><dd>{event.success ? t('audit.login.success') : `${t('audit.login.failure')} · ${loginReason(event.failureReason)}`}</dd>
          <dt>{t('audit.col.ip')}</dt><dd className="mono">{event.ipAddress ?? '—'}</dd>
          <dt>{t('audit.login.browser')}</dt><dd className="small">{event.userAgent ?? '—'}</dd>
        </dl>
      )}
      <p className="banner dashed small">{t('audit.login.hint')}</p>
    </div>
  );
}

function EntryDetail({ id, onRecordHistory }: { readonly id: number; readonly onRecordHistory: (type: string, id: number) => void }) {
  const query = useAuditEntry(id);
  const entry = query.data;
  if (entry === undefined) {
    return query.isError ? <ErrorBanner message={problem(query.error)} /> : <p className="muted" role="status">{t('app.loading')}</p>;
  }
  const isFirst = entry.entityId != null && entry.previousChanges == null;
  const rows = diffChanges(entry.changes, entry.previousChanges);
  const link = entityLink(entry.entityType, entry.entityId);
  const title = `${actionText(entry.action, entry.action.endsWith('.Save') ? isFirst : undefined)} ${entityName(entry.entityType).toLocaleLowerCase('ro-RO')}`;

  return (
    <div className="role-editor audit-entry">
      <div className="role-editor-head">
        <h2>{title}</h2>
        <span className="pill tag">{t('audit.readOnly')}</span>
      </div>
      <dl className="audit-kv">
        <dt>{t('audit.detail.record')}</dt>
        <dd className="strong">{entry.recordLabel ?? (entry.entityId == null ? '—' : `#${entry.entityId}`)}</dd>
        <dt>{t('audit.detail.who')}</dt><dd>{entry.userName} <span className="muted">({entry.login})</span></dd>
        <dt>{t('audit.detail.when')}</dt><dd>{whenLongText(entry.occurredAt)}</dd>
        <dt>{t('audit.detail.company')}</dt><dd>{entry.companyName ?? t('audit.detail.noCompany')}</dd>
        <dt>{t('audit.detail.ip')}</dt><dd className="mono">{entry.ipAddress ?? '—'} · {entry.correlationId}</dd>
      </dl>
      {rows.length > 0 ? (
        <table className="simple audit-diff">
          <thead>
            <tr>
              <th scope="col">{t('audit.diff.field')}</th>
              {!isFirst && entry.previousChanges != null && <th scope="col">{t('audit.diff.before')}</th>}
              <th scope="col">{t('audit.diff.after')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.field}>
                <td>{fieldLabel(r.field)}</td>
                {r.before !== undefined && <td className="before">{r.before}</td>}
                <td className="after">{r.after}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p className="muted small">
          {entry.changes == null ? t('audit.diff.none') : t('audit.diff.same', { when: entry.previousAt == null ? '—' : whenText(entry.previousAt) })}
        </p>
      )}
      {rows.length > 0 && (
        <p className="muted small">
          {isFirst ? t('audit.diff.created')
            : entry.previousAt == null ? '' : t('audit.diff.previous', { when: whenText(entry.previousAt) })}
        </p>
      )}
      <p className="banner dashed small">{t('audit.retention')}</p>
      <div className="form-footer">
        <span />
        <div className="actions">
          {link !== undefined && <Link className="btn ghost" to={link}>{t('audit.open')}</Link>}
          {entry.entityId != null && (
            <button type="button" className="btn ghost" onClick={() => onRecordHistory(entry.entityType, entry.entityId ?? 0)}>
              {t('audit.recordHistory')}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
