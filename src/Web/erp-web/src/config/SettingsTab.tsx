import { useEffect, useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner, TextField } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { useUnsavedChanges } from '../components/UnsavedChanges';
import { applyServerErrors } from '../components/forms';
import { t } from '../i18n';

const settingsKey = ['core', 'settings'] as const;

const schema = z.object({
  anafVatRegistryUrl: z.string().trim().max(500).refine((value) => {
    try {
      return new URL(value).protocol === 'https:';
    } catch {
      return false;
    }
  }, t('validation.httpsUrl')),
});
type Values = z.infer<typeof schema>;

/** Configurare › Setari (macheta 07f, familia Sistem): adresa serviciului web ANAF si emailul SMTP; celelalte setari vin cu etapele lor. */
export function SettingsTab() {
  return (
    <>
      <AnafSettings />
      <SmtpSettings />
    </>
  );
}

function AnafSettings() {
  const settings = useQuery({ queryKey: settingsKey, queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/settings', { signal })) });
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const form = useForm<Values>({ resolver: zodResolver(schema) as Resolver<Values>, defaultValues: { anafVatRegistryUrl: '' } });
  const data = settings.data;
  useEffect(() => {
    if (data !== undefined) {
      form.reset({ anafVatRegistryUrl: data.anafVatRegistryUrl });
    }
  }, [data, form]);
  const guard = useUnsavedChanges(form.formState.isDirty);

  if (data === undefined) {
    return <p className="muted" role="status">{settings.isError ? t('app.loadError') : t('app.loading')}</p>;
  }

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const saved = unwrap(await api.PUT('/api/core/settings', { body: { anafVatRegistryUrl: values.anafVatRegistryUrl, rowVersion: data.rowVersion ?? null } }));
      form.reset({ anafVatRegistryUrl: saved.anafVatRegistryUrl });
      guard.markClean();
      queryClient.setQueryData(settingsKey, saved);
      notify(t('common.saved'));
    } catch (e) {
      const message = applyServerErrors(e, form.setError, ['anafVatRegistryUrl']);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <section className="card settings-card" aria-labelledby="anaf-settings-title">
      <form className="node-form" noValidate onSubmit={(e) => void submit(e)}>
        <h2 id="anaf-settings-title" className="form-title">{t('settings.anaf.title')}</h2>
        <ErrorBanner message={error} />
        <div className="form-body">
          <p className="muted">{t('settings.anaf.hint')}</p>
          <TextField control={form.control} name="anafVatRegistryUrl" label={t('settings.anaf.url')} />
          <div className="field-row">
            <span className="muted small">{t('settings.anaf.default', { url: data.defaultAnafVatRegistryUrl })}</span>
            <button type="button" className="btn ghost small"
              onClick={() => form.setValue('anafVatRegistryUrl', data.defaultAnafVatRegistryUrl, { shouldDirty: true, shouldValidate: true })}>
              {t('settings.anaf.useDefault')}
            </button>
          </div>
          <a href="https://static.anaf.ro/static/10/Anaf/Informatii_R/Servicii_web/servicii_sincron.html" target="_blank" rel="noreferrer noopener">
            {t('settings.anaf.docs')}
          </a>
        </div>
        <div className="form-footer">
          <span className="muted" />
          <div className="actions">
            <button type="button" className="btn ghost" onClick={() => form.reset({ anafVatRegistryUrl: data.anafVatRegistryUrl })}>
              {t('common.cancel')}
            </button>
            <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
              {t('common.save')}
            </button>
          </div>
        </div>
      </form>
    </section>
  );
}

type Smtp = components['schemas']['SmtpSettingsDto'];
const smtpKey = ['core', 'settings', 'smtp'] as const;

/**
 * Email (SMTP) al instalarii (02.01, 4.7; macheta 07f): serverul, portul și criptarea, utilizatorul, parola (criptată, nu se afișează niciodată;
 * gol = rămâne cea salvată), expeditorul și adresa de răspuns; „Trimite email de test” trimite imediat cu setările salvate.
 */
function SmtpSettings() {
  const smtp = useQuery({ queryKey: smtpKey, queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/settings/smtp', { signal })) });
  return smtp.data === undefined ? <p className="muted" role="status">{smtp.isError ? t('app.loadError') : t('app.loading')}</p>
    : <SmtpForm key={smtp.data.rowVersion ?? 'new'} value={smtp.data} />;
}

function SmtpForm({ value }: { readonly value: Smtp }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [draft, setDraft] = useState({ host: value.host ?? '', port: value.port, security: value.security, userName: value.userName ?? '', password: '',
    fromAddress: value.fromAddress ?? '', fromName: value.fromName ?? '', replyTo: value.replyTo ?? '' });
  const [testTo, setTestTo] = useState('');
  const [error, setError] = useState<string | undefined>();
  const [result, setResult] = useState<string | undefined>();
  const set = (patch: Partial<typeof draft>) => setDraft({ ...draft, ...patch });
  const optional = (text: string) => (text.trim() === '' ? null : text.trim());

  const save = async () => {
    setError(undefined);
    try {
      const saved = unwrap(await api.PUT('/api/core/settings/smtp', { body: { host: draft.host, port: draft.port, security: draft.security,
        userName: optional(draft.userName), password: draft.password === '' ? null : draft.password, fromAddress: draft.fromAddress, fromName: optional(draft.fromName),
        replyTo: optional(draft.replyTo), rowVersion: value.rowVersion ?? null } }));
      queryClient.setQueryData(smtpKey, saved);
      notify(t('common.saved'));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  const test = async () => {
    setError(undefined);
    setResult(undefined);
    try {
      const sent = unwrap(await api.POST('/api/core/settings/smtp/test', { body: { to: testTo } }));
      setResult(t('settings.smtp.testOk', { to: sent.to, ms: sent.elapsedMilliseconds }));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <section className="card settings-card" aria-labelledby="smtp-settings-title">
      <div className="node-form">
        <h2 id="smtp-settings-title" className="form-title">{t('settings.smtp.title')}</h2>
        <div className="form-body settings-form">
          <p className="muted">{t('settings.smtp.hint')}</p>
          <label>{t('settings.smtp.host')}<input className="cell-input" value={draft.host} onChange={(e) => set({ host: e.target.value })} /></label>
          <div className="row-between">
            <label>{t('settings.smtp.port')}<input className="cell-input num" type="number" min={1} max={65535} value={draft.port}
              onChange={(e) => set({ port: Number(e.target.value) })} /></label>
            <label>{t('settings.smtp.security')}
              <select className="cell-input" value={draft.security} onChange={(e) => set({ security: Number(e.target.value) })}>
                <option value={1}>STARTTLS</option>
                <option value={2}>SSL / TLS</option>
                <option value={0}>{t('settings.smtp.none')}</option>
              </select>
            </label>
          </div>
          <label>{t('settings.smtp.user')}<input className="cell-input" value={draft.userName} autoComplete="off" onChange={(e) => set({ userName: e.target.value })} /></label>
          <label>{t('settings.smtp.password')}<input className="cell-input" type="password" value={draft.password} autoComplete="new-password"
            placeholder={value.hasPassword ? t('settings.smtp.passwordKept') : ''} onChange={(e) => set({ password: e.target.value })} /></label>
          <label>{t('settings.smtp.fromName')}<input className="cell-input" value={draft.fromName} onChange={(e) => set({ fromName: e.target.value })} /></label>
          <label>{t('settings.smtp.fromAddress')}<input className="cell-input" type="email" value={draft.fromAddress} onChange={(e) => set({ fromAddress: e.target.value })} /></label>
          <label>{t('settings.smtp.replyTo')}<input className="cell-input" type="email" value={draft.replyTo} onChange={(e) => set({ replyTo: e.target.value })} /></label>
          <ErrorBanner message={error} />
          {result !== undefined && <div className="banner info" role="status">{result}</div>}
        </div>
        <div className="form-footer">
          <span className="test-email">
            <input className="cell-input" type="email" value={testTo} placeholder={t('settings.smtp.testTo')} aria-label={t('settings.smtp.testTo')}
              onChange={(e) => setTestTo(e.target.value)} />
            <button type="button" className="btn ghost" disabled={testTo.trim() === ''} onClick={() => void test()}>{t('settings.smtp.test')}</button>
          </span>
          <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button></div>
        </div>
      </div>
    </section>
  );
}
