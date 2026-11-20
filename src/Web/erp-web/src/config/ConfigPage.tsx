import { useEffect, useMemo, useState } from 'react';
import { NavLink, useLocation, useOutletContext, useSearchParams } from 'react-router';
import { useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ColumnDirective, ColumnsDirective, GridComponent, Inject, Sort } from '@syncfusion/ej2-react-grids';
import { z } from 'zod';
import type { CurrentUser } from '../api/session';
import { ErrorBanner, NumberField, SelectField, SwitchField, TextField } from '../components/Field';
import { useNotifier } from '../components/Notifier';
import { applyServerErrors, required } from '../components/forms';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { orgApi, orgKeys, useNumberSeries, useOrgLookups, useOrgTree, type NumberSeries } from '../org/orgApi';
import { LicenseTab } from './LicenseTab';
import { AddressTypesTab } from './AddressTypesTab';

/** Administrare › Configurare (02.01, 11.1): tab-urile apar dupa permisiuni; acum Serii si Licenta (celelalte vin cu etapele lor). */
export function ConfigPage() {
  const user = useOutletContext<CurrentUser>();
  const canSeries = user.permissions.includes('Core.NumberSeries.Manage');
  const canLicense = user.permissions.includes('Core.License.Manage');
  const canAddressTypes = user.permissions.includes('Core.Company.Manage');
  const path = useLocation().pathname;
  const tab = path.endsWith('/licenta') && canLicense ? 'license' : path.endsWith('/tipuri-adrese') && canAddressTypes ? 'addressTypes' : 'series';
  const tabTitle = tab === 'license' ? t('config.tab.license') : tab === 'addressTypes' ? t('config.tab.addressTypes') : t('config.tab.series');
  return (
    <div className="page wide">
      <div className="crumb">{t('menu.admin')} › {t('menu.config')} › {tabTitle}</div>
      <div className="head">
        <div>
          <h1>{t('config.title')}</h1>
          <div className="sub2">{t('config.subtitle')}</div>
        </div>
      </div>
      <nav className="config-tabs" aria-label={t('menu.config')}>
        <div className="family">
          <span className="family-name">{t('config.family.documents')}</span>
          {canSeries && (
            <NavLink to="/admin/config/serii" className="tab">
              # {t('config.tab.series')}
            </NavLink>
          )}
        </div>
        {canAddressTypes && (
          <div className="family">
            <span className="family-name">{t('config.family.nomenclature')}</span>
            <NavLink to="/admin/config/tipuri-adrese" className="tab">
              ⌂ {t('config.tab.addressTypes')}
            </NavLink>
          </div>
        )}
        {canLicense && (
          <div className="family">
            <span className="family-name">{t('config.family.system')}</span>
            <NavLink to="/admin/config/licenta" className="tab">
              ✓ {t('config.tab.license')}
            </NavLink>
          </div>
        )}
      </nav>
      {tab === 'license' ? <LicenseTab /> : tab === 'addressTypes' ? <AddressTypesTab /> : canSeries && <SeriesTab user={user} />}
    </div>
  );
}

const fiscalPill = (series: NumberSeries) => (series.isFiscal ? t('series.fiscal') : t('common.none'));

function SeriesTab({ user }: { readonly user: CurrentUser }) {
  const [params] = useSearchParams();
  const companyId = Number(params.get('firma') ?? user.companyId) || undefined;
  const series = useNumberSeries(companyId);
  const lookups = useOrgLookups();
  const tree = useOrgTree();
  const [selected, setSelected] = useState<NumberSeries | 'new' | undefined>();
  const [year, setYear] = useState<number | undefined>(new Date().getFullYear());
  const [onlyFiscal, setOnlyFiscal] = useState(false);

  const rows = useMemo(
    () =>
      (series.data ?? [])
        .filter((s) => year === undefined || s.year === year)
        .filter((s) => !onlyFiscal || s.isFiscal)
        .map((s) => ({ ...s, branchText: s.branchName ?? t('series.headquarters'), next: format(s.prefix, s.year, s.nextNumber, s.padding), fiscalText: fiscalPill(s),
          statusText: s.closedAt === null ? t('series.open') : t('series.closed') })),
    [series.data, year, onlyFiscal],
  );

  if (companyId === undefined) {
    return <p className="muted">{t('series.noCompany')}</p>;
  }
  const years = [...new Set([new Date().getFullYear(), ...(series.data ?? []).map((s) => s.year)])].sort((a, b) => b - a);
  const company = user.companies.find((c) => c.id === companyId);

  return (
    <div className="split series">
      <section className="card" aria-labelledby="series-title">
        <div className="card-head">
          <h2 id="series-title">{t('series.title')} {company === undefined ? '' : `· ${company.name}`}</h2>
          <div className="chips-row">
            <select aria-label={t('series.year')} value={year ?? ''} onChange={(e) => setYear(e.target.value === '' ? undefined : Number(e.target.value))} className="chip">
              <option value="">{t('series.allYears')}</option>
              {years.map((y) => (
                <option key={y} value={y}>{`${t('series.year')}: ${y}`}</option>
              ))}
            </select>
            <button type="button" className={onlyFiscal ? 'chip on' : 'chip'} aria-pressed={onlyFiscal} onClick={() => setOnlyFiscal(!onlyFiscal)}>
              {t('series.onlyFiscal')}
            </button>
            <button type="button" className="btn primary" onClick={() => setSelected('new')}>
              {t('series.new')}
            </button>
          </div>
        </div>
        {rows.length === 0 ? (
          <p className="muted">{series.isPending ? t('app.loading') : t('series.empty')}</p>
        ) : (
          <GridComponent dataSource={rows} allowSorting height="auto" rowSelected={(args: { data: NumberSeries }) => setSelected(args.data)}>
            <ColumnsDirective>
              <ColumnDirective field="documentTypeName" headerText={t('series.documentType')} width="160" />
              <ColumnDirective field="branchText" headerText={t('series.branch')} width="130" />
              <ColumnDirective field="prefix" headerText={t('series.prefix')} width="90" />
              <ColumnDirective field="year" headerText={t('series.year')} width="70" />
              <ColumnDirective field="next" headerText={t('series.next')} width="160" />
              <ColumnDirective field="usedCount" headerText={t('series.used')} width="90" textAlign="Right" format="N0" />
              <ColumnDirective field="fiscalText" headerText={t('series.type')} width="90" />
              <ColumnDirective field="statusText" headerText={t('series.status')} width="90" />
            </ColumnsDirective>
            <Inject services={[Sort]} />
          </GridComponent>
        )}
      </section>
      <section className="card detail-card">
        {selected === undefined || lookups.data === undefined || tree.data === undefined ? (
          <p className="muted empty-detail">{t('org.selectNode')}</p>
        ) : (
          <SeriesForm
            key={selected === 'new' ? 'new' : `${selected.id}-${selected.rowVersion}`}
            companyId={companyId}
            series={selected === 'new' ? undefined : selected}
            documentTypes={lookups.data.documentTypes.map((d) => ({ value: d.code, text: d.name }))}
            branches={tree.data.branches.filter((b) => b.companyId === companyId).map((b) => ({ value: String(b.id), text: b.name }))}
            onDone={(next) => setSelected(next)}
          />
        )}
      </section>
    </div>
  );
}

const format = (prefix: string, year: number, number: number, padding: number) => `${prefix}-${year}-${String(number).padStart(padding, '0')}`;

const seriesSchema = z.object({
  documentTypeCode: required,
  branchId: z.number().nullable(),
  prefix: required.regex(/^[A-Za-z0-9]{1,20}$/, 'Prefixul are 1–20 litere sau cifre.'),
  year: z.number().int().min(2000).max(2999),
  nextNumber: z.number().int().min(1),
  padding: z.number().int().min(1).max(12),
  isFiscal: z.boolean(),
  isDefault: z.boolean(),
  autoRollover: z.boolean(),
});
type SeriesValues = z.infer<typeof seriesSchema>;

function SeriesForm({ companyId, series, documentTypes, branches, onDone }: {
  readonly companyId: number;
  readonly series: NumberSeries | undefined;
  readonly documentTypes: { value: string; text: string }[];
  readonly branches: { value: string; text: string }[];
  readonly onDone: (next: NumberSeries | undefined) => void;
}) {
  const queryClient = useQueryClient();
  const { notify } = useNotifier();
  const [error, setError] = useState<string | undefined>();
  const defaults: SeriesValues = series === undefined
    ? { documentTypeCode: '', branchId: null, prefix: '', year: new Date().getFullYear(), nextNumber: 1, padding: 6, isFiscal: true, isDefault: true, autoRollover: false }
    : { documentTypeCode: series.documentTypeCode, branchId: series.branchId, prefix: series.prefix, year: series.year, nextNumber: series.nextNumber,
        padding: series.padding, isFiscal: series.isFiscal, isDefault: series.isDefault, autoRollover: series.autoRollover };
  const form = useForm<SeriesValues>({ resolver: zodResolver(seriesSchema) as Resolver<SeriesValues>, defaultValues: defaults });
  useEffect(() => form.reset(defaults), [series?.id]); // eslint-disable-line react-hooks/exhaustive-deps
  const [prefix, year, nextNumber, padding] = useWatch({ control: form.control, name: ['prefix', 'year', 'nextNumber', 'padding'] });
  const closed = series?.closedAt !== null && series?.closedAt !== undefined;
  const used = series !== undefined && series.usedCount > 0;
  const locked = closed || (used && series.isFiscal);

  async function refresh() {
    await queryClient.invalidateQueries({ queryKey: orgKeys.series(companyId) });
  }

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      await orgApi.saveSeries({ ...values, id: series?.id ?? null, companyId, rowVersion: series?.rowVersion ?? null });
      await refresh();
      notify(t('common.saved'));
      onDone(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, Object.keys(defaults));
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }, () => notify(t('common.notSaved'), 'error'));

  async function close() {
    if (series === undefined || !window.confirm(t('series.closeConfirm', { name: `${series.prefix} ${series.year}` }))) {
      return;
    }
    setError(undefined);
    try {
      await orgApi.closeSeries(companyId, series.id, series.rowVersion);
      await refresh();
      notify(t('series.closedNotice'));
      onDone(undefined);
    } catch (e) {
      const message = applyServerErrors(e, form.setError, []);
      setError(message);
      notify(message ?? t('common.notSaved'), 'error');
    }
  }

  return (
    <form className="node-form" noValidate onSubmit={(e) => void submit(e)}>
      <h2 className="form-title">
        {series === undefined ? t('series.new').replace('+ ', '') : `${series.prefix} · ${series.documentTypeName} ${series.year}`}{' '}
        {series !== undefined && <span className={closed ? 'pill off' : 'pill ok'}>{closed ? t('series.closed') : t('series.open')}</span>}
      </h2>
      <ErrorBanner message={error} />
      <div className="form-body">
        <div className="grid g2">
          <SelectField control={form.control} name="documentTypeCode" label={t('series.documentType')} options={documentTypes} disabled={locked} />
          <SelectField control={form.control} name="branchId" label={t('series.branch')} options={branches} allowEmpty={t('series.headquarters')} numeric disabled={locked} />
          <TextField control={form.control} name="prefix" label={t('series.prefix')} upper disabled={locked} />
          <NumberField control={form.control} name="year" label={t('series.year')} min={2000} max={2999} grouping={false} disabled={locked} />
          <NumberField control={form.control} name="padding" label={t('series.padding')} min={1} max={12} disabled={closed} />
          <NumberField control={form.control} name="nextNumber" label={t('series.nextNumber')} min={1} disabled={locked} />
        </div>
        <div className="field">
          <span className="label">{t('series.format')}</span>
          <span className="readonly">
            {'{Prefix}-{An}-{Număr}'} <span className="example">{t('series.example', { value: format(prefix || 'PREFIX', year, nextNumber, padding) })}</span>
          </span>
        </div>
        <SwitchField control={form.control} name="isFiscal" label={t('series.isFiscal')} disabled={locked} />
        {used && series.isFiscal && !closed && (
          <div className="banner warn">{t('series.lockedWarning', { count: series.usedCount.toLocaleString('ro-RO') })}</div>
        )}
        <h3 className="section">{t('series.usage')}</h3>
        <SwitchField control={form.control} name="isDefault" label={t('series.isDefault')} disabled={closed} />
        <SwitchField control={form.control} name="autoRollover" label={t('series.autoRollover')} disabled={closed} />
      </div>
      <div className="form-footer">
        <span className="muted">{series?.lastUsedAt == null ? '' : t('series.lastUsed', { date: formatDate(new Date(series.lastUsedAt)) })}</span>
        <div className="actions">
          {series !== undefined && !closed && (
            <button type="button" className="btn ghost" onClick={() => void close()}>
              {t('series.close')}
            </button>
          )}
          {!closed && (
            <button type="submit" className="btn primary" disabled={form.formState.isSubmitting}>
              {t('common.save')}
            </button>
          )}
        </div>
      </div>
    </form>
  );
}
