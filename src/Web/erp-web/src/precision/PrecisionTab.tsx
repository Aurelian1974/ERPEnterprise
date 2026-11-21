import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { t, type TextKey } from '../i18n';
import { formatNumber, precisionApi, precisionKeys, previewLine, ROUNDING_MODES, usePrecisionPolicies, type PrecisionPage, type PrecisionPolicy } from './precisionApi';

function problem(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error ? error.message : t('app.loadError');
  }
  const messages = Object.values(error.fieldErrors).flat();
  return messages.length > 0 ? [...new Set(messages)].join(' · ') : error.message;
}

const DIGITS = [0, 1, 2, 3, 4, 5, 6] as const;
const EXAMPLE = { quantity: 3.25, price: 12.345678 };

/**
 * Configurare › Precizie (02.01, 3.5; macheta 07d): politicile de zecimale (pret, cantitate; valoarea fixa 2), unde se aplica
 * (firmele, articolele cu zecimale proprii) si previzualizarea pe un exemplu. Schimbarea afecteaza doar documentele noi.
 */
export function PrecisionTab() {
  const query = usePrecisionPolicies();
  const [selected, setSelected] = useState<number | 'new' | undefined>();
  const data = query.data;
  if (data === undefined) {
    return <p className="muted" role="status">{query.isError ? problem(query.error) : t('app.loading')}</p>;
  }
  const policy = selected === 'new' ? undefined : data.policies.find((p) => p.id === (selected ?? data.policies[0]?.id));

  return (
    <div className="dimensions-layout precision-layout">
      <section className="card dimension-types" aria-labelledby="precision-list-title">
        <div className="card-head">
          <h2 id="precision-list-title">{t('precision.policies')}</h2>
          <button type="button" className="btn ghost small" onClick={() => setSelected('new')}>{t('precision.new')}</button>
        </div>
        <ul>
          {data.policies.map((p) => (
            <li key={p.id}>
              <button type="button" className={p.id === policy?.id && selected !== 'new' ? 'current' : ''} onClick={() => setSelected(p.id)}>
                <span>{p.code}{p.isActive ? '' : ` · ${t('dim.inactiveOne')}`}</span>
                <span className="muted small">{p.pricePrecision} / {p.quantityPrecision} / {p.amountPrecision}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="muted small">{t('precision.legend')}</p>
      </section>
      <PolicyForm key={selected === 'new' ? 'new' : `${policy?.id}-${policy?.rowVersion}`} page={data} policy={policy} onSaved={setSelected} />
    </div>
  );
}

function PolicyForm({ page, policy, onSaved }: {
  readonly page: PrecisionPage;
  readonly policy: PrecisionPolicy | undefined;
  readonly onSaved: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [code, setCode] = useState(policy?.code ?? '');
  const [name, setName] = useState(policy?.name ?? '');
  const [price, setPrice] = useState(policy?.pricePrecision ?? 4);
  const [quantity, setQuantity] = useState(policy?.quantityPrecision ?? 3);
  const [rounding, setRounding] = useState(policy?.roundingMode ?? 1);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const dirty = policy === undefined ? code !== '' || name !== ''
    : code !== policy.code || name !== policy.name || price !== policy.pricePrecision || quantity !== policy.quantityPrecision
      || rounding !== policy.roundingMode;
  const guard = useUnsavedChanges(dirty);
  const companies = page.companies.filter((c) => c.policyId === policy?.id);

  const save = async (isActive: boolean) => {
    setError(undefined);
    setBusy(true);
    try {
      const saved = await precisionApi.save({
        id: policy?.id ?? null, code: code.trim(), name: name.trim(), pricePrecision: price, quantityPrecision: quantity, roundingMode: rounding,
        isActive, rowVersion: policy?.rowVersion ?? null,
      });
      guard.markClean();
      await queryClient.invalidateQueries({ queryKey: precisionKeys.all });
      notify(t('common.saved'));
      onSaved(saved.id);
    } catch (e) {
      setError(problem(e));
    } finally {
      setBusy(false);
    }
  };

  const digits = (label: string, value: number, set: (n: number) => void) => (
    <div className="field">
      <span className="label">{label}</span>
      <div className="segmented digits" role="radiogroup" aria-label={label}>
        {DIGITS.map((n) => (
          <button key={n} type="button" role="radio" aria-checked={value === n} className={value === n ? 'on' : ''} onClick={() => set(n)}>{n}</button>
        ))}
      </div>
    </div>
  );

  const rows = [...new Set([2, 4, 6, price])].sort((a, b) => a - b).map((p) => ({ p, line: previewLine(EXAMPLE.quantity, EXAMPLE.price, p, quantity) }));

  return (
    <>
      <section className="card dimension-values" aria-labelledby="precision-form-title">
        <div className="role-editor-head">
          <h2 id="precision-form-title">{policy?.code ?? t('precision.newTitle')}</h2>
          {companies.length > 0 && <span className="muted">{t('precision.defaultFor', { count: companies.length })}</span>}
        </div>
        <ErrorBanner message={error} />
        <div className="grid g2">
          <label className="field">
            <span className="label">{t('field.code')}</span>
            <input className="cell-input" value={code} maxLength={20} onChange={(e) => setCode(e.target.value.toUpperCase())} />
          </label>
          <label className="field">
            <span className="label">{t('field.name')}</span>
            <input className="cell-input" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
          </label>
        </div>
        <div className="grid g2">
          {digits(t('precision.price'), price, setPrice)}
          {digits(t('precision.quantity'), quantity, setQuantity)}
          <div className="field">
            <span className="label">{t('precision.amount')}</span>
            <span className="readonly-value">2 🔒 {t('precision.amountFixed')}</span>
          </div>
          <label className="field">
            <span className="label">{t('precision.rounding')}</span>
            <select className="cell-input" value={rounding} onChange={(e) => setRounding(Number(e.target.value))}>
              {ROUNDING_MODES.map((m) => <option key={m} value={m}>{t(`precision.mode.${m}` as TextKey)}</option>)}
            </select>
          </label>
        </div>
        <h3 className="section">{t('precision.where')}</h3>
        <p className="banner dashed small">{t('precision.hierarchy')}</p>
        <table className="simple">
          <thead>
            <tr><th scope="col">{t('precision.level')}</th><th scope="col">{t('precision.whereCol')}</th><th scope="col">{t('precision.policy')}</th></tr>
          </thead>
          <tbody>
            {page.companies.map((c) => (
              <tr key={c.companyId}>
                <td>{t('dim.company')}</td>
                <td>{c.companyName}</td>
                <td><span className={c.policyId === policy?.id ? 'pill info' : 'pill'}>{page.policies.find((p) => p.id === c.policyId)?.code ?? '—'}</span></td>
              </tr>
            ))}
            <tr>
              <td>{t('precision.item')}</td>
              <td>{t('precision.itemOverrides', { count: page.itemOverrides })}</td>
              <td className="muted">{t('precision.ownDigits')}</td>
            </tr>
          </tbody>
        </table>
        <div className="form-footer">
          <span className="muted small">{policy === undefined ? '' : t('precision.usedBy', { count: policy.companyCount })}</span>
          <div className="actions">
            {policy !== undefined && (
              <button type="button" className="btn ghost" disabled={busy} onClick={() => void save(!policy.isActive)}>
                {policy.isActive ? t('dim.deactivate') : t('dim.activate')}
              </button>
            )}
            <button type="button" className="btn primary" disabled={busy || !dirty} onClick={() => void save(policy?.isActive ?? true)}>{t('common.save')}</button>
          </div>
        </div>
      </section>

      <section className="card dimension-detail" aria-labelledby="precision-preview-title">
        <div className="role-editor-head">
          <h2 id="precision-preview-title">{t('precision.preview')}</h2>
          <span className="muted">{t('precision.previewSub')}</span>
        </div>
        <table className="simple rates-table">
          <thead>
            <tr>
              <th scope="col">{t('precision.decimals')}</th>
              <th scope="col" className="num">{t('precision.priceShort')}</th>
              <th scope="col" className="num">{t('precision.quantityShort')}</th>
              <th scope="col" className="num">{t('precision.amountShort')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(({ p, line }) => (
              <tr key={p} className={p === price ? 'current' : ''}>
                <td>{p} / {quantity}</td>
                <td className="num">{formatNumber(line.price, p)}</td>
                <td className="num">{formatNumber(line.quantity, quantity)}</td>
                <td className="num">{formatNumber(line.amount, 2)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="banner dashed small">{t('precision.example')}</p>
        <p className="banner warn small">{t('precision.onlyNew')}</p>
      </section>
    </>
  );
}
