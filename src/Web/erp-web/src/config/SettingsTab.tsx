import { useEffect, useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
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

/** Configurare › Setari (macheta 07f, familia Sistem): adresa serviciului web ANAF; celelalte setari vin cu etapele lor. */
export function SettingsTab() {
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
