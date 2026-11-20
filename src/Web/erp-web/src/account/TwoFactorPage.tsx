import { useState } from 'react';
import { useForm, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner, TextField } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { applyServerErrors, required } from '../components/forms';
import { t } from '../i18n';

type TwoFactorStatus = components['schemas']['TwoFactorStatusDto'];

const statusKey = ['account', 'two-factor'] as const;

const enableSchema = z.object({ password: required, code: required.regex(/^[0-9 -]{6,8}$/, 'Codul are 6 cifre.') });
type EnableValues = z.infer<typeof enableSchema>;
const disableSchema = z.object({ password: required });
type DisableValues = z.infer<typeof disableSchema>;

function downloadCodes(codes: readonly string[]) {
  const blob = new Blob([`${t('twoFactor.codesFile')}\r\n\r\n${codes.join('\r\n')}\r\n`], { type: 'text/plain;charset=utf-8' });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = 'erp-coduri-rezerva.txt';
  link.click();
  URL.revokeObjectURL(link.href);
}

/** Contul meu › Autentificare în doi pași (02.01, 11.2): activare si dezactivare cu parola contului. */
export function TwoFactorPage() {
  const queryClient = useQueryClient();
  const status = useQuery({
    queryKey: statusKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/account/two-factor', { signal })),
    // Cheia TOTP nu trebuie schimbata intre afisarea codului QR si confirmare.
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
  const [codes, setCodes] = useState<readonly string[] | undefined>();

  const refresh = () => queryClient.invalidateQueries({ queryKey: statusKey });
  const data: TwoFactorStatus | undefined = status.data;

  return (
    <div className="page">
      <div className="crumb">{t('user.account')} › {t('twoFactor.title')}</div>
      <div className="head">
        <div>
          <h1>{t('twoFactor.title')}</h1>
          <div className="sub2">{t('twoFactor.subtitle')}</div>
        </div>
        {data !== undefined && <span className={data.enabled ? 'pill ok' : 'pill off'}>{data.enabled ? t('twoFactor.enabled') : t('twoFactor.disabled')}</span>}
      </div>
      <section className="card detail-card">
        {codes !== undefined ? (
          <div className="form-body">
            <h2>{t('twoFactor.codesTitle')}</h2>
            <p className="muted">{t('twoFactor.codesHelp')}</p>
            <div className="bk" aria-label={t('twoFactor.codesTitle')}>
              {codes.map((code) => (
                <span key={code}>{code}</span>
              ))}
            </div>
            <div className="actions">
              <button type="button" className="btn secondary" onClick={() => downloadCodes(codes)}>{t('twoFactor.download')}</button>
              <button type="button" className="btn primary" onClick={() => { setCodes(undefined); void refresh(); }}>{t('twoFactor.done')}</button>
            </div>
          </div>
        ) : data === undefined ? (
          <p className="muted">{status.isPending ? t('app.loading') : t('common.notSaved')}</p>
        ) : data.enabled ? (
          <DisableForm required={data.required} onDone={refresh} />
        ) : (
          <EnableForm status={data} onEnabled={setCodes} />
        )}
      </section>
    </div>
  );
}

function EnableForm({ status, onEnabled }: { readonly status: TwoFactorStatus; readonly onEnabled: (codes: readonly string[]) => void }) {
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const form = useForm<EnableValues>({ resolver: zodResolver(enableSchema) as Resolver<EnableValues>, defaultValues: { password: '', code: '' } });

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      const result = unwrap(await api.POST('/api/account/two-factor/enable', { body: values }));
      notify(t('twoFactor.enabledNotice'));
      onEnabled(result.codes);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, ['password', 'code']);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
      form.resetField('password');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)}>
      <ErrorBanner message={error} />
      <div className="form-body">
        <h3 className="section">{t('twoFactor.scan')}</h3>
        {/* SVG generat de server din cheia contului curent. */}
        <div className="qr" role="img" aria-label={t('twoFactor.scan')} dangerouslySetInnerHTML={{ __html: status.qrSvg ?? '' }} />
        <p className="muted">{t('twoFactor.manualKey')} <code>{status.sharedKey}</code></p>
        <h3 className="section">{t('twoFactor.confirm')}</h3>
        <div className="grid g2">
          <TextField control={form.control} name="password" label={t('twoFactor.password')} password />
          <TextField control={form.control} name="code" label={t('twoFactor.code')} />
        </div>
      </div>
      <div className="form-footer">
        <span className="muted" />
        <div className="actions">
          <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>{t('twoFactor.enable')}</button>
        </div>
      </div>
    </form>
  );
}

function DisableForm({ required: mandatory, onDone }: { readonly required: boolean; readonly onDone: () => Promise<unknown> }) {
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const form = useForm<DisableValues>({ resolver: zodResolver(disableSchema) as Resolver<DisableValues>, defaultValues: { password: '' } });

  const submit = form.handleSubmit(async (values) => {
    if (!window.confirm(t('twoFactor.disableConfirm'))) {
      return;
    }
    setError(undefined);
    try {
      const result = await api.POST('/api/account/two-factor/disable', { body: values });
      if (result.error !== undefined) {
        throw new ApiError(result.response.status, result.error);
      }
      notify(t('twoFactor.disabledNotice'));
      form.reset();
      await onDone();
    } catch (e) {
      const message = applyServerErrors(e, form.setError, ['password']);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
      form.resetField('password');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  if (mandatory) {
    return <div className="banner warn">{t('twoFactor.required')}</div>;
  }

  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)}>
      <ErrorBanner message={error} />
      <div className="form-body">
        <p className="muted">{t('twoFactor.disableHint')}</p>
        <div className="grid g2">
          <TextField control={form.control} name="password" label={t('twoFactor.password')} password />
        </div>
      </div>
      <div className="form-footer">
        <span className="muted" />
        <div className="actions">
          <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>{t('twoFactor.disable')}</button>
        </div>
      </div>
    </form>
  );
}
