import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ApiError, unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { ErrorBanner } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { t, type TextKey } from '../i18n';

type Settings = components['schemas']['FxSettingsDto'];

/**
 * Configurare › Finanțe › Valută (02.02, F4 3.3, 4.6; macheta cf-08): regula implicită de curs încrucișat a firmei și marja, reevaluarea
 * avansurilor, blocarea alocărilor cu dată într-o lună reevaluată. Salvarea cere Fin.Fx.Settings.
 */
export function FxSettingsTab({ canSave }: { readonly canSave: boolean }) {
  const settings = useQuery({ queryKey: ['finance', 'fx', 'settings'], queryFn: async () => unwrap(await api.GET('/api/finance/fx/settings')) });
  if (settings.data === undefined) return settings.isError ? <ErrorBanner message={settings.error.message} /> : <p className="muted">{t('app.loading')}</p>;
  return <Form initial={settings.data} canSave={canSave} />;
}

function Form({ initial, canSave }: { readonly initial: Settings; readonly canSave: boolean }) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [draft, setDraft] = useState<Settings>(initial);
  const [error, setError] = useState<string | undefined>();

  const save = async () => {
    setError(undefined);
    try {
      const saved = unwrap(await api.PUT('/api/finance/fx/settings', { body: draft }));
      setDraft(saved);
      queryClient.setQueryData(['finance', 'fx', 'settings'], saved);
      notify(t('fx.set.saved'));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('common.notSaved'));
    }
  };

  return (
    <section className="card settings-form" aria-labelledby="fx-settings-title">
      <h2 id="fx-settings-title">{t('fx.set.title')}</h2>
      <fieldset disabled={!canSave}>
        <legend className="strong">{t('fx.set.cross')}</legend>
        <p className="muted small">{t('fx.set.crossHelp')}</p>
        {[1, 2, 3].map((rule) => (
          <label key={rule} className="check">
            <input type="radio" name="cross-rule" checked={draft.crossCurrencyRule === rule} onChange={() => setDraft({ ...draft, crossCurrencyRule: rule })} />
            {t(`fx.set.rule.${rule}` as TextKey)}
          </label>
        ))}
        <label>{t('fx.set.markup')}<input className="cell-input num" value={draft.crossCurrencyMarkupPercent}
          onChange={(e) => setDraft({ ...draft, crossCurrencyMarkupPercent: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={draft.revalueAdvances} onChange={(e) => setDraft({ ...draft, revalueAdvances: e.target.checked })} />
          {t('fx.set.revalueAdvances')}</label>
        <label className="check"><input type="checkbox" checked={draft.blockAllocationsInRevaluedMonth}
          onChange={(e) => setDraft({ ...draft, blockAllocationsInRevaluedMonth: e.target.checked })} />{t('fx.set.block')}</label>
      </fieldset>
      <p className="muted small">{t('fx.set.fixed')}</p>
      <ErrorBanner message={error} />
      {canSave && <div className="actions"><button type="button" className="btn primary" onClick={() => void save()}>{t('common.save')}</button></div>}
    </section>
  );
}
