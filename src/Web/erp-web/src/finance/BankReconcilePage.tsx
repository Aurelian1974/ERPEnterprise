import { useCallback, useEffect, useMemo, useState } from 'react';
import { useOutletContext, useSearchParams } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { CurrentUser } from '../api/session';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t } from '../i18n';
import { isoToRo } from '../partners/partnerModel';
import { bankApi, bankKeys, filterLines, LineStatus, lineStatusLabel, lineStatusPill, remainder, useBankAccounts, useBankLine, useBankReconciliation, type BankLineDetail,
  type BankReconciliation, type LineFilter } from './bankApi';
import { formatAmount } from './cashApi';

const lineFilters: readonly { readonly value: LineFilter; readonly label: Parameters<typeof t>[0] }[] = [
  { value: 'all', label: 'bank.filter.all' },
  { value: 'in', label: 'bank.filter.in' },
  { value: 'out', label: 'bank.filter.out' },
  { value: 'proposed', label: 'bank.filter.proposed' },
  { value: 'unmatched', label: 'bank.filter.unmatched' },
  { value: 'explained', label: 'bank.filter.explained' },
];

interface PartDraft {
  readonly operationTypeId: number;
  readonly amount: string;
  readonly counterTreasuryAccountId: number | null;
  readonly note: string;
  readonly paymentOrderId: number | null;
}

/**
 * Bancă › Extras de cont (02.02, F2 4.4; macheta f2-02): în antet soldul din extras, OP-urile neconfirmate și soldul scriptic; în stânga
 * liniile nevalidate ale contului (filtre, bifare) și OP-urile de confirmat; în dreapta linia aleasă: datele băncii, propunerile, editorul
 * de părți cu restul de explicat, „De clarificat (473)”, „Ține minte”; jos „Validează selecția” (F9). Space bifează, ↑↓ linia următoare.
 */
export function BankReconcilePage() {
  const user = useOutletContext<CurrentUser>();
  const [params, setParams] = useSearchParams();
  const accounts = useBankAccounts();
  const bankAccounts = useMemo(() => (accounts.data ?? []).filter((a) => a.kind === 1), [accounts.data]);
  const fromUrl = Number(params.get('cont'));
  const account = Number.isInteger(fromUrl) && fromUrl > 0 ? fromUrl : bankAccounts[0]?.id;
  const screen = useBankReconciliation(account);
  const [filter, setFilter] = useState<LineFilter>('all');
  const [selected, setSelected] = useState<number | undefined>();
  const [checked, setChecked] = useState<ReadonlySet<number>>(new Set());
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const data = screen.data;
  const lines = useMemo(() => (data === undefined ? [] : filterLines(data.lines, filter)), [data, filter]);
  const current = selected ?? lines[0]?.id;

  const refresh = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: bankKeys.all });
  }, [queryClient]);

  const validate = useCallback(async () => {
    if (data === undefined) return;
    const chosen = data.lines.filter((l) => checked.has(l.id));
    if (chosen.length === 0) return;
    try {
      const saved = await bankApi.validate(chosen.map((l) => ({ id: l.id, rowVersion: l.rowVersion })));
      notify(t('bank.validated', { count: saved.length }));
      setChecked(new Set());
      await refresh();
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  }, [checked, data, notify, refresh]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'F9') {
        e.preventDefault();
        void validate();
        return;
      }
      const target = e.target as HTMLElement | null;
      if (target !== null && ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes(target.tagName)) return;
      const index = lines.findIndex((l) => l.id === current);
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        const next = lines[Math.min(Math.max(index + (e.key === 'ArrowDown' ? 1 : -1), 0), lines.length - 1)];
        if (next !== undefined) setSelected(next.id);
      } else if (e.key === ' ' && current !== undefined) {
        e.preventDefault();
        setChecked((set) => toggle(set, current));
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [current, lines, validate]);

  const match = async () => {
    if (account === undefined) return;
    try {
      const result = await bankApi.autoMatch(account);
      notify(t('bank.matched', { explained: result.explained, validated: result.autoValidated }));
      await refresh();
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.bank')} › {t('menu.bankReconcile')}</div>
      <div className="head">
        <div>
          <h1>{t('bank.reconcile.title')}</h1>
          <div className="sub2">{t('bank.reconcile.subtitle')}</div>
        </div>
        <div className="actions">
          {user.permissions.includes('Fin.BankStatement.Reconcile') &&
            <button type="button" className="btn ghost" onClick={() => void match()}>{t('bank.runMatching')}</button>}
        </div>
      </div>
      {bankAccounts.length === 0 && accounts.data !== undefined ? <p className="muted">{t('bank.noAccounts')}</p> : (
        <>
          <section className="card cash-summary bank-summary" aria-label={t('bank.reconcile.title')}>
            <label>{t('bank.account')}
              <select className="cell-input" value={account ?? ''} onChange={(e) => { setParams({ cont: e.target.value }); setSelected(undefined); setChecked(new Set()); }}>
                {bankAccounts.map((a) => <option key={a.id} value={a.id}>{`${a.code} · ${a.name}`}</option>)}
              </select>
            </label>
            {data !== undefined && <Summary data={data} />}
          </section>
          {screen.isError && <ErrorBanner message={screen.error.message} />}
          {data !== undefined && (
            <div className="split cash-document">
              <section className="card" aria-label={t('bank.openLines')}>
                <div className="chips-row" role="group" aria-label={t('bank.filter.label')}>
                  {lineFilters.map((f) => (
                    <button key={f.value} type="button" className={filter === f.value ? 'chip on' : 'chip'} aria-pressed={filter === f.value}
                      onClick={() => setFilter(f.value)}>{t(f.label)}</button>
                  ))}
                </div>
                {lines.length === 0 ? <p className="muted">{t('bank.noOpenLines')}</p> : (
                  <table className="partner-list cash-list" aria-busy={screen.isFetching}>
                    <thead>
                      <tr>
                        <th scope="col"><span className="visually-hidden">{t('bank.select')}</span></th>
                        <th scope="col">{t('bank.col.date')}</th>
                        <th scope="col">{t('bank.col.counterparty')}</th>
                        <th scope="col" className="num">{t('bank.col.amount')}</th>
                        <th scope="col">{t('bank.col.status')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {lines.map((l) => (
                        <tr key={l.id} className={l.id === current ? 'selected' : undefined} onClick={() => setSelected(l.id)} aria-selected={l.id === current}>
                          <td><input type="checkbox" checked={checked.has(l.id)} aria-label={t('bank.select')}
                            disabled={l.matchStatus !== LineStatus.Explained} onChange={() => setChecked((set) => toggle(set, l.id))} /></td>
                          <td className="small">{isoToRo(l.bookingDate)}</td>
                          <td>
                            <strong>{l.partnerName ?? l.counterpartyName ?? '—'}</strong>
                            <div className="mono small">{l.remittanceInfo}</div>
                          </td>
                          <td className={l.amount.startsWith('-') ? 'num neg' : 'num pos'}>{formatAmount(l.amount)}</td>
                          <td><span className={lineStatusPill(l.matchStatus)}>{t(lineStatusLabel(l.matchStatus))}</span></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
                <h2 className="section-title">{t('bank.unconfirmedOrders')}</h2>
                {data.unconfirmedOrders.length === 0 ? <p className="muted">{t('bank.noUnconfirmed')}</p> : (
                  <table className="partner-list cash-list">
                    <tbody>
                      {data.unconfirmedOrders.map((o) => (
                        <tr key={o.id}>
                          <td className="small">{isoToRo(o.documentDate)}</td>
                          <td><strong>{o.number}</strong> · {o.beneficiaryName}
                            <div className="muted small">{o.batchNumber == null ? t(o.isApproved ? 'bank.order.approved' : 'bank.order.awaitingApproval')
                              : t('bank.order.inBatch', { batch: o.batchNumber })}</div></td>
                          <td className="num neg">{formatAmount(o.amount)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
                <div className="muted small footer-hint">{t('bank.keys')} · {t('bank.selectedCount', { count: checked.size })}</div>
              </section>
              <aside className="card cash-side">
                {current === undefined ? <p className="muted">{t('bank.pickLine')}</p>
                  : <LinePanel key={current} id={current} data={data} canReconcile={user.permissions.includes('Fin.BankStatement.Reconcile')}
                    onChanged={refresh} />}
                <div className="actions">
                  <button type="button" className="btn primary" disabled={checked.size === 0 || !user.permissions.includes('Fin.BankStatement.Validate')}
                    onClick={() => void validate()}>{t('bank.validateSelection')} <kbd>F9</kbd></button>
                </div>
              </aside>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function toggle(set: ReadonlySet<number>, id: number): ReadonlySet<number> {
  const next = new Set(set);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  return next;
}

function Summary({ data }: { readonly data: BankReconciliation }) {
  return (
    <>
      <div><span className="label">{t('bank.statement')}</span><strong>{data.statementNo == null ? '—'
        : `nr. ${data.statementNo} · ${data.statementDate == null ? '' : isoToRo(data.statementDate)}`}</strong></div>
      <div><span className="label">{t('bank.statementBalance')}</span><strong>{formatAmount(data.statementBalance)}</strong></div>
      <div><span className="label">{t('bank.unconfirmed')}</span><strong className="neg">{formatAmount(data.unconfirmedAmount)}</strong></div>
      <div className="highlight"><span className="label">{t('bank.bookBalance')}</span><strong>{formatAmount(data.bookBalance)}</strong></div>
      <div className="muted small">{t('bank.openLineCount', { count: data.openLineCount })}</div>
    </>
  );
}

function LinePanel({ id, data, canReconcile, onChanged }: { readonly id: number; readonly data: BankReconciliation; readonly canReconcile: boolean;
  readonly onChanged: () => Promise<void> }) {
  const line = useBankLine(id);
  const detail = line.data;
  if (detail === undefined) {
    return <p role="status" className="muted">{line.isError ? line.error.message : t('app.loading')}</p>;
  }
  return <LineEditor key={detail.rowVersion} line={detail} data={data} canReconcile={canReconcile} onChanged={onChanged} />;
}

function LineEditor({ line, data, canReconcile, onChanged }: { readonly line: BankLineDetail; readonly data: BankReconciliation;
  readonly canReconcile: boolean; readonly onChanged: () => Promise<void> }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [parts, setParts] = useState<PartDraft[]>(() => line.parts.map((p) => ({
    operationTypeId: p.operationTypeId, amount: p.amount, counterTreasuryAccountId: p.counterTreasuryAccountId ?? null, note: p.note ?? '',
    paymentOrderId: p.paymentOrderId ?? null,
  })));
  const [remember, setRemember] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const incoming = !line.amount.startsWith('-');
  const types = data.operationTypes.filter((ty) => ty.direction === 3 || ty.direction === (incoming ? 1 : 2));
  const typeById = new Map(data.operationTypes.map((ty) => [ty.id, ty]));
  const rest = remainder(line.amount, parts);
  const locked = line.matchStatus === LineStatus.Validated || !canReconcile;

  const save = async (draft: readonly PartDraft[]) => {
    setError(undefined);
    try {
      await bankApi.explain(line.id, draft.map((p) => ({
        operationTypeId: p.operationTypeId, amount: p.amount.replace(',', '.'), partnerId: typeById.get(p.operationTypeId)?.requiresPartner ? line.partnerId ?? null : null,
        employeeId: null, settlementRef: null, cashDocumentId: null, counterTreasuryAccountId: p.counterTreasuryAccountId, budgetObligationRef: null,
        costCenterId: null, projectId: null, note: p.note.trim() === '' ? null : p.note.trim(), paymentOrderId: p.paymentOrderId, cardSettlementExpectedId: null,
      })), line.rowVersion);
      if (remember && draft.length === 1) {
        await bankApi.remember(line.id);
      }
      notify(t('common.saved'));
      await queryClient.invalidateQueries({ queryKey: bankKeys.line(line.id) });
      await onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : t('common.notSaved'));
    }
  };

  const unknownType = data.operationTypes.find((ty) => ty.code === 'BANK-NEIDENTIFICAT');
  const supplierType = data.operationTypes.find((ty) => ty.code === 'PLATA-FURNIZOR');
  const update = (index: number, patch: Partial<PartDraft>) => setParts(parts.map((p, i) => (i === index ? { ...p, ...patch } : p)));

  return (
    <div className="bank-line">
      <h2>{`${formatAmount(line.amount)} ${line.currencyCode}`} · {line.partnerName ?? line.counterpartyName ?? '—'}
        {' '}<span className={lineStatusPill(line.matchStatus)}>{t(lineStatusLabel(line.matchStatus))}</span></h2>
      <p className="muted small">{t('bank.bankData', { date: isoToRo(line.bookingDate), reference: line.bankReference })}
        {line.counterpartyIban == null ? '' : ` · IBAN ${line.counterpartyIban}`}</p>
      <p className="mono small">{line.remittanceInfo}</p>

      {(line.suggestions ?? []).length > 0 && (
        <>
          <h3 className="section-title">{t('bank.suggestions')}</h3>
          {(line.suggestions ?? []).map((s, index) => (
            <div key={`${s.paymentOrderId ?? index}`} className="suggestion">
              <span className="pill info">{`${s.score}%`}</span>
              <span>{s.description} · {formatAmount(s.amount)}</span>
              {!locked && supplierType !== undefined && s.paymentOrderId != null && (
                <button type="button" className="btn ghost small" onClick={() => void save([{ operationTypeId: supplierType.id, amount: line.amount,
                  counterTreasuryAccountId: null, note: '', paymentOrderId: s.paymentOrderId ?? null }])}>{t('bank.accept')}</button>
              )}
            </div>
          ))}
        </>
      )}

      <h3 className="section-title">{t('bank.parts')}</h3>
      <table className="partner-list">
        <thead>
          <tr>
            <th scope="col">{t('bank.col.type')}</th>
            <th scope="col">{t('bank.col.detail')}</th>
            <th scope="col" className="num">{t('bank.col.amount')}</th>
            <th scope="col"><span className="visually-hidden">{t('bank.removePart')}</span></th>
          </tr>
        </thead>
        <tbody>
          {parts.map((p, index) => {
            const type = typeById.get(p.operationTypeId);
            return (
              <tr key={index}>
                <td>
                  <select className="cell-input" value={p.operationTypeId} disabled={locked} aria-label={t('bank.col.type')}
                    onChange={(e) => update(index, { operationTypeId: Number(e.target.value) })}>
                    {(type === undefined || types.includes(type) ? types : [type, ...types]).map((ty) => <option key={ty.id} value={ty.id}>{ty.name}</option>)}
                  </select>
                </td>
                <td>
                  {type?.requiresCounterAccount ? (
                    <select className="cell-input" value={p.counterTreasuryAccountId ?? ''} disabled={locked} aria-label={t('bank.counterAccount')}
                      onChange={(e) => update(index, { counterTreasuryAccountId: e.target.value === '' ? null : Number(e.target.value) })}>
                      <option value="">{t('bank.counterAccount')}</option>
                      {data.accounts.filter((a) => a.id !== data.treasuryAccountId).map((a) => <option key={a.id} value={a.id}>{a.code}</option>)}
                    </select>
                  ) : type?.requiresPartner ? <span className="small">{line.partnerName ?? t('bank.noPartner')}</span> : (
                    <input className="cell-input" value={p.note} disabled={locked} aria-label={t('bank.note')} placeholder={t('bank.note')}
                      onChange={(e) => update(index, { note: e.target.value })} />
                  )}
                  {p.paymentOrderId != null && <div className="muted small">{t('bank.confirmsOrder', { id: p.paymentOrderId })}</div>}
                </td>
                <td><input className="cell-input num" value={p.amount.replace('.', ',')} disabled={locked} aria-label={t('bank.col.amount')}
                  onChange={(e) => update(index, { amount: e.target.value })} /></td>
                <td>{!locked && <button type="button" className="btn ghost small" aria-label={t('bank.removePart')}
                  onClick={() => setParts(parts.filter((_, i) => i !== index))}>×</button>}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {!locked && (
        <div className="row-between">
          <button type="button" className="btn ghost small" disabled={types.length === 0}
            onClick={() => setParts([...parts, { operationTypeId: types[0]?.id ?? 0, amount: rest, counterTreasuryAccountId: null, note: '', paymentOrderId: null }])}>
            {t('bank.newPart')}</button>
          <span className="small">{t('bank.remainder')} <strong className={rest === '0.00' ? 'pos' : 'neg'}>{formatAmount(rest)}</strong></span>
        </div>
      )}
      {!locked && (
        <label className="check"><input type="checkbox" checked={remember} disabled={parts.length !== 1} onChange={(e) => setRemember(e.target.checked)} />
          {t('bank.remember')}</label>
      )}
      <ErrorBanner message={error} />
      {!locked && (
        <div className="actions">
          {unknownType !== undefined && (
            <button type="button" className="btn ghost" onClick={() => void save([{ operationTypeId: unknownType.id, amount: line.amount, counterTreasuryAccountId: null,
              note: t('bank.toClarify'), paymentOrderId: null }])}>{t('bank.toClarifyButton')}</button>
          )}
          {!incoming && line.matchStatus !== LineStatus.Explained && supplierType !== undefined && line.partnerId != null && (
            <NewOrderButton line={line} typeId={supplierType.id} onDone={onChanged} />
          )}
          <button type="button" className="btn primary" disabled={parts.length > 0 && rest !== '0.00'} onClick={() => void save(parts)}>{t('bank.saveExplanation')}</button>
        </div>
      )}
    </div>
  );
}

/** „OP nou” din linie (F2 4.2a): OP-ul precompletat din linie, legat de factura data, validat și confirmat în același pas. */
function NewOrderButton({ line, typeId, onDone }: { readonly line: BankLineDetail; readonly typeId: number; readonly onDone: () => Promise<void> }) {
  const { notify } = useNotifier();
  const create = async () => {
    const invoice = window.prompt(t('bank.newOrder.invoice'));
    if (invoice === null || invoice.trim() === '') return;
    try {
      await unwrap(await api.POST('/api/finance/bank-statement-lines/{id}/payment-order', {
        params: { path: { id: line.id } },
        body: {
          bankStatementLineId: line.id, lineRowVersion: line.rowVersion, purpose: 1, partnerId: line.partnerId ?? null, employeeId: null, partnerBankAccountId: null,
          beneficiaryFiscalCode: null, budgetObligationRef: null, lines: [{ operationTypeId: typeId, amount: line.amount.slice(1), settlementRef: invoice.trim(),
            costCenterId: null, projectId: null }], confirmAnafInactive: false,
        },
      }));
      notify(t('bank.newOrder.done'));
      await onDone();
    } catch (e) {
      notify(e instanceof Error ? e.message : t('common.notSaved'), 'error');
    }
  };
  return <button type="button" className="btn ghost" onClick={() => void create()}>{t('bank.newOrder')}</button>;
}
