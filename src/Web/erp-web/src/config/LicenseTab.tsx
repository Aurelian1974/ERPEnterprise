import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { formatDate } from '../format/date';
import { moduleName, t } from '../i18n';
import { licenseApi, licenseKeys, useLicense, type License } from './licenseApi';

/** Toate modulele, in ordinea din licenta (08, 3.2); cele nelicentiate apar gri. */
const allModules = ['Core', 'Finance', 'Sales', 'Purchasing', 'Inventory', 'Integrations', 'Reporting', 'Wms', 'Tms', 'Manufacturing', 'Hr', 'Pos', 'Construction', 'Crm'];

/** O data calendaristica din API (AAAA-LL-ZZ), afisata in formatul romanesc, fara deplasare de fus orar. */
const day = (value: string | null) => (value === null ? undefined : formatDate(new Date(`${value}T12:00:00`)));

/** Administrare › Configurare › Licenta (08, 4.1; macheta 07j din 02.01): licenta activa, incarcarea unei licente noi, istoricul. */
export function LicenseTab() {
  const license = useLicense();
  if (license.data === undefined) {
    return <p className="muted">{license.isError ? t('app.loadError') : t('app.loading')}</p>;
  }
  return (
    <div className="split license">
      <section className="card" aria-labelledby="license-modules">
        <h2 id="license-modules">{t('license.modules')}</h2>
        <p className="muted">{t('license.modulesHint', { active: license.data.modules.length, total: allModules.length })}</p>
        <ul className="module-list">
          {allModules.map((code) => (
            <li key={code} className={license.data.modules.includes(code) ? 'on' : 'off'}>
              {moduleName(code)}
            </li>
          ))}
        </ul>
        <h2>{t('license.limits')}</h2>
        <dl className="kv">
          <dt>{t('license.users')}</dt>
          <dd>{license.data.users}</dd>
          <dt>{t('license.companies')}</dt>
          <dd>{license.data.fiscalCodes.length}</dd>
          <dt>{t('license.posTerminals')}</dt>
          <dd>{license.data.posTerminals}</dd>
          <dt>{t('license.payrollEmployees')}</dt>
          <dd>{license.data.payrollEmployees}</dd>
        </dl>
      </section>
      <section className="card detail-card" aria-labelledby="license-current">
        <CurrentLicense license={license.data} />
      </section>
    </div>
  );
}

function CurrentLicense({ license }: { readonly license: License }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const input = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);

  async function upload(file: File | undefined) {
    if (file === undefined) {
      return;
    }
    setBusy(true);
    setError(undefined);
    try {
      const saved = await licenseApi.upload(file);
      queryClient.setQueryData(licenseKeys.current, saved);
      await queryClient.invalidateQueries({ queryKey: ['core', 'me'] });
      notify(t('license.uploaded', { id: saved.licenseId ?? '' }));
    } catch (e) {
      const message = e instanceof ApiError ? (e.fieldErrors.fileBase64?.[0] ?? e.message) : t('app.loadError');
      setError(message);
      notify(message, 'error');
    } finally {
      setBusy(false);
      if (input.current !== null) {
        input.current.value = '';
      }
    }
  }

  return (
    <>
      <div className="card-head">
        <h2 id="license-current">{t('license.title')}</h2>
        <span className={license.state === 'Valid' ? 'pill ok' : 'pill off'}>{t(`license.state.${license.state}` as Parameters<typeof t>[0])}</span>
      </div>
      {license.clockBehind && <div className="banner warn" role="alert">{t('license.clockBehind')}</div>}
      <ErrorBanner message={error} />
      {license.hasLicense ? (
        <dl className="kv">
          <dt>{t('license.customer')}</dt>
          <dd>{license.customerName}</dd>
          <dt>{t('license.fiscalCodes')}</dt>
          <dd>{license.fiscalCodes.join(', ')}</dd>
          <dt>{t('license.number')}</dt>
          <dd>{`${license.licenseId} · ${t('license.key', { id: license.keyId ?? '' })}`}</dd>
          <dt>{t('license.validUntil')}</dt>
          <dd>{day(license.validUntil) ?? t('license.unlimited')}</dd>
          <dt>{t('license.updatesUntil')}</dt>
          <dd>{day(license.updatesUntil) ?? t('license.noLimit')}</dd>
        </dl>
      ) : (
        <p className="muted">{t('license.none')}</p>
      )}
      <p className="muted">{t('license.explain')}</p>
      <div className="actions">
        <input ref={input} type="file" accept=".lic" hidden aria-label={t('license.file')} onChange={(e) => void upload(e.target.files?.[0])} />
        <button type="button" className="btn primary" disabled={busy} onClick={() => input.current?.click()}>
          {busy ? t('app.loading') : t('license.upload')}
        </button>
      </div>
      {license.history.length > 0 && (
        <>
          <h3>{t('license.history')}</h3>
          <table className="simple">
            <thead>
              <tr>
                <th>{t('license.number')}</th>
                <th>{t('license.issuedAt')}</th>
                <th>{t('license.validUntil')}</th>
                <th>{t('license.loadedAt')}</th>
              </tr>
            </thead>
            <tbody>
              {license.history.map((h) => (
                <tr key={`${h.licenseId}-${h.loadedAt}`} className={h.isCurrent ? 'current' : undefined}>
                  <td>{h.licenseId}</td>
                  <td>{day(h.issuedAt)}</td>
                  <td>{day(h.validUntil) ?? t('license.unlimited')}</td>
                  <td>{formatDate(new Date(h.loadedAt))}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </>
  );
}
