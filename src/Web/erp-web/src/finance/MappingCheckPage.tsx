import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import { ErrorBanner } from '../components/Field';
import { t } from '../i18n';
import { chartKeys, useAltCharts } from './AltChartsPage';
import { ChartPicker } from './MappingRulesPage';

/**
 * Contabilitate › Verificarea mapării (02.02, C1 4.5; macheta c1-04): regulile care nu se aplică niciodată (acoperite de o regulă de mai
 * sus), celelalte suprapuneri și conturile alternative fără sursă. Liniile nemapate, lunile înghețate și recalcularea vin cu notele contabile.
 */
export function MappingCheckPage() {
  const charts = useAltCharts();
  const [picked, setPicked] = useState<number | undefined>();
  const chartId = picked ?? charts.data?.[0]?.id;
  const check = useQuery({
    queryKey: [...chartKeys.all, chartId ?? 0, 'check'],
    enabled: chartId !== undefined,
    queryFn: async () => unwrap(await api.GET('/api/finance/charts/{id}/check', { params: { path: { id: chartId ?? 0 } } })),
  });
  const c = check.data;
  const dead = c?.overlaps.filter((o) => o.isDead) ?? [];
  const other = c?.overlaps.filter((o) => !o.isDead) ?? [];

  return (
    <div className="page wide">
      <div className="crumb">{t('module.Finance')} › {t('menu.accounting')} › {t('menu.mappingCheck')}</div>
      <div className="head">
        <div><h1>{t('check.title')}</h1><div className="sub2">{t('check.subtitle')}</div></div>
        <div className="actions"><ChartPicker value={chartId} onChange={setPicked} /></div>
      </div>
      <ErrorBanner message={check.isError ? check.error.message : undefined} />
      {charts.data !== undefined && charts.data.length === 0 ? <p className="muted">{t('map.noCharts')}</p> : c !== undefined && (
        <>
          <div className="kpi-row">
            <div className="kpi"><div className="kpi-label">{t('check.dead')}</div><div className="kpi-value">{dead.length}</div></div>
            <div className="kpi"><div className="kpi-label">{t('check.overlaps')}</div><div className="kpi-value">{other.length}</div></div>
            <div className="kpi"><div className="kpi-label">{t('check.withoutSource')}</div><div className="kpi-value">{c.accountsWithoutSource.length}</div>
              <div className="muted small">{t('check.informative')}</div></div>
          </div>
          <section className="card" aria-label={t('check.dead')}>
            <h2>{t('check.dead')}</h2>
            {dead.length === 0 ? <p className="muted">{t('check.noDead')}</p> : (
              <ul>{dead.map((o) => <li key={`${o.winnerId}-${o.loserId}`}>{t('check.deadLine', { loser: o.loserPriority, winner: o.winnerPriority, accounts: o.commonAccounts.join(', ') })}</li>)}</ul>
            )}
            <h2>{t('check.overlaps')}</h2>
            {other.length === 0 ? <p className="muted">{t('check.noOverlaps')}</p> : (
              <ul>{other.map((o) => <li key={`${o.winnerId}-${o.loserId}`}>{t('check.overlapLine', { winner: o.winnerPriority, loser: o.loserPriority, accounts: o.commonAccounts.slice(0, 12).join(', ') })}</li>)}</ul>
            )}
            <h2>{t('check.withoutSource')}</h2>
            {c.accountsWithoutSource.length === 0 ? <p className="muted">{t('check.allHaveSource')}</p> : (
              <ul>{c.accountsWithoutSource.map((a) => <li key={a.id}><span className="mono">{a.code}</span> {a.name}</li>)}</ul>
            )}
            <p className="muted small">{t('check.laterHint')}</p>
          </section>
        </>
      )}
    </div>
  );
}
