import { contextName, contextOwner, useContextNode } from '../app/node';
import { OwnerFilter } from '../org/OwnerFilter';
import { ownerLabel, useOwnerOptions } from '../org/owner';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useOutletContext, useSearchParams } from 'react-router';
import { downloadFile } from '../api/download';
import { useNotifier } from '../components/Notifier';
import { TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import type { CurrentUser } from '../api/session';
import { tabKeyTarget } from '../components/tabs';
import { formatDate } from '../format/date';
import { t } from '../i18n';
import { useOrgLookups, type OrgLookups } from '../org/orgApi';
import { DataType, PAGE_SIZE, PartnerType, searchQuery, usePartner, usePartnerLookups, usePartners, type PartnerCriteria, type PartnerLookups } from './partnerApi';
import { PartnerForm, type PartnerSection } from './PartnerForm';
import { anafBadge, displayFiscalCode } from './partnerModel';
import { PartnerBlockTab } from './BlockTab';
import { DefaultDimensionsTab } from '../dimensions/DefaultDimensions';
import { blockedText } from './blockApi';
import { PartnerAddressesTab, PartnerBankAccountsTab, PartnerContactsTab, PartnerHistoryTab } from './PartnerTabs';

type Chip = 'all' | 'customers' | 'suppliers' | 'individuals' | 'anafInactive' | 'blocked';

/** Textul cautat ajunge la server dupa o scurta pauza, nu la fiecare tasta. */
export function useDebounced(value: string, delay = 250): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/** Selectia din URL (?id=5 sau ?nou=1), ca fisa sa poata fi deschisa dintr-un link. */
function useSelection() {
  const [params, setParams] = useSearchParams();
  const id = Number(params.get('id'));
  return {
    id: Number.isInteger(id) && id > 0 ? id : undefined,
    creating: params.get('nou') === '1',
    select: (partnerId: number) => setParams({ id: String(partnerId) }),
    startCreate: () => setParams({ nou: '1' }),
    clear: () => setParams({}),
  };
}

/** Parteneri (macheta 04): lista cu cautare si filtre in stanga, fisa partenerului ales in dreapta. */
export function PartnersPage() {
  const user = useOutletContext<CurrentUser>();
  const lookups = usePartnerLookups();
  const orgLookups = useOrgLookups();
  const selection = useSelection();
  const [search, setSearch] = useState('');
  const [chip, setChip] = useState<Chip>('all');
  const [showInactive, setShowInactive] = useState(false);
  const [propertyId, setPropertyId] = useState<number | undefined>();
  const [propertyValue, setPropertyValue] = useState<string | undefined>();
  const debounced = useDebounced(search);
  // Pagina tine de filtrele cu care a fost aleasa: un filtru nou incepe de la prima pagina.
  const [owner, setOwner] = useState('');
  const ownerOptions = useOwnerOptions('Partner');
  const context = useContextNode(user);
  const filterKey = JSON.stringify([debounced, chip, showInactive, propertyId, propertyValue, owner, context]);
  const [paging, setPaging] = useState({ key: filterKey, page: 1 });
  const page = paging.key === filterKey ? paging.page : 1;
  const setPage = (next: number) => setPaging({ key: filterKey, page: next });
  const canManage = user.permissions.includes('Core.Partner.Manage');
  const canImport = user.permissions.includes('Core.Import.Run');
  const { notify } = useNotifier();
  const [exporting, setExporting] = useState(false);

  const property = lookups.data?.properties.find((p) => p.id === propertyId);
  const criteria: PartnerCriteria = {
    search: debounced,
    role: chip === 'customers' ? 1 : chip === 'suppliers' ? 2 : undefined,
    partnerType: chip === 'individuals' ? PartnerType.Individual : undefined,
    anafInactive: chip === 'anafInactive' ? true : undefined,
    blocked: chip === 'blocked' ? true : undefined,
    showInactive,
    propertyDefinitionId: property !== undefined && propertyValue !== undefined ? property.id : undefined,
    propertyOptionId: property !== undefined && property.dataType !== DataType.Bool && propertyValue !== undefined ? Number(propertyValue) : undefined,
    propertyBool: property?.dataType === DataType.Bool && propertyValue !== undefined ? propertyValue === 'true' : undefined,
    owner,
    context,
    page,
  };
  const list = usePartners(criteria);

  /** Exportul Excel al listei filtrate (toate paginile), cu coloanele sablonului de import. */
  async function exportExcel() {
    setExporting(true);
    try {
      const query = Object.fromEntries(Object.entries(searchQuery(criteria)).filter(([key]) => key !== 'Page' && key !== 'PageSize'));
      await downloadFile('/api/core/partners/export', query, 'parteneri.xlsx');
      notify(t('partner.exportDone'));
    } catch (e) {
      notify(e instanceof Error ? e.message : t('app.loadError'), 'error');
    } finally {
      setExporting(false);
    }
  }

  if (lookups.data === undefined || orgLookups.data === undefined) {
    return <div className="page" role="status">{lookups.isError || orgLookups.isError ? t('app.loadError') : t('app.loading')}</div>;
  }

  const chips: [Chip, string][] = [
    ['all', t('partner.filter.all')],
    ['customers', t('partner.filter.customers')],
    ['suppliers', t('partner.filter.suppliers')],
    ['individuals', t('partner.filter.individuals')],
    ['anafInactive', t('partner.filter.anafInactive')],
    ['blocked', t('partner.filter.blocked')],
  ];
  const data = list.data;

  return (
    <div className="page wide">
      <div className="crumb">{t('menu.nomenclatures')} › {t('menu.partners')}</div>
      <div className="head">
        <div>
          <h1>{t('partner.title')}</h1>
          <div className="sub2">{t('partner.subtitle', { count: (data?.activeCount ?? 0).toLocaleString('ro-RO') })}</div>
        </div>
        <div className="actions">
          <button type="button" className="btn ghost" disabled={exporting} onClick={() => void exportExcel()}>
            {t('partner.export')}
          </button>
          {canImport && (
            <Link to="/admin/config/import" className="btn ghost">
              {t('partner.import')}
            </Link>
          )}
          {canManage && (
            <button type="button" className="btn primary" onClick={selection.startCreate}>
              {t('partner.new')}
            </button>
          )}
        </div>
      </div>

      <div className="split partners">
        <section className="card list-card" aria-label={t('menu.partners')}>
          <TextBoxComponent
            placeholder={t('partner.searchPlaceholder')}
            value={search}
            input={(args: { value: string }) => setSearch(args.value)}
            showClearButton
            htmlAttributes={{ 'aria-label': t('partner.searchPlaceholder') }}
          />
          <div className="chips-row">
            {chips.map(([key, label]) => (
              <button key={key} type="button" className={chip === key ? 'chip on' : 'chip'} aria-pressed={chip === key} onClick={() => setChip(key)}>
                {label}
              </button>
            ))}
            <OwnerFilter options={ownerOptions.data} context={context} value={owner} onChange={setOwner} />
            <button type="button" className={showInactive ? 'chip on' : 'chip'} aria-pressed={showInactive} onClick={() => setShowInactive(!showInactive)}>
              {t('partner.filter.showInactive')}
            </button>
          </div>
          <PropertyFilter lookups={lookups.data} propertyId={propertyId} value={propertyValue}
            onChange={(id, value) => {
              setPropertyId(id);
              setPropertyValue(value);
            }} />
          {list.isError && data === undefined && (
            <div className="banner error" role="alert">
              {t('app.loadError')}{' '}
              <button type="button" className="btn ghost" onClick={() => void list.refetch()}>
                {t('app.retry')}
              </button>
            </div>
          )}
          {data !== undefined && data.items.length === 0 && <p className="muted">{t('partner.empty')}</p>}
          {data !== undefined && data.items.length > 0 && (
            <table className="partner-list" aria-busy={list.isFetching}>
              <thead>
                <tr>
                  <th scope="col">{t('partner.col.partner')}</th>
                  <th scope="col">{t('partner.col.type')}</th>
                  <th scope="col">{t('partner.col.role')}</th>
                  <th scope="col">{t('partner.col.anaf')}</th>
                  <th scope="col">{t('owner.field')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((item) => {
                  const badge = anafBadge(item);
                  const current = item.id === selection.id;
                  return (
                    <tr key={item.id} className={[current ? 'current' : '', item.isActive ? '' : 'inactive'].join(' ').trim()}
                      onClick={() => selection.select(item.id)}>
                      <td>
                        <button type="button" className="row-link" aria-current={current ? 'true' : undefined} onClick={(e) => {
                          e.stopPropagation();
                          selection.select(item.id);
                        }}>
                          {item.name}
                        </button>
                        {item.blockedFor > 0 && <span className="pill bad" title={blockedTitle(item.blockedFor)}>{t('block.badge')}</span>}
                        <div className="muted small">
                          {item.code}
                          {item.partnerType === PartnerType.Individual ? ` · ${t('partner.typeShort.4').toLocaleLowerCase('ro-RO')}` : ` · ${displayFiscalCode(item)}`}
                        </div>
                      </td>
                      <td>{t(`partner.typeShort.${item.partnerType as 1 | 2 | 3 | 4}`)}</td>
                      <td>
                        <div className="roles">
                          {item.isCustomer && <span className="tag customer">{t('partner.role.customer')}</span>}
                          {item.isSupplier && <span className="tag supplier">{t('partner.role.supplier')}</span>}
                        </div>
                      </td>
                      <td>{badge === undefined ? t('common.none') : <span className={`pill ${badge.kind}`}>{badge.text}</span>}</td>
                      <td className="small">{ownerLabel(item.ownerLevel, item.ownerName)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
          {data !== undefined && data.total > 0 && <Pager page={page} total={data.total} onPage={setPage} />}
        </section>

        <section className="card detail-card">
          {selection.creating ? (
            <NewPartnerPanel lookups={lookups.data} orgLookups={orgLookups.data} canManage={canManage}
              onSaved={(id) => selection.select(id)} onCancel={selection.clear} />
          ) : selection.id === undefined ? (
            <p className="muted empty-detail">{t('partner.select')}</p>
          ) : (
            <PartnerPanel key={selection.id} partnerId={selection.id} lookups={lookups.data} orgLookups={orgLookups.data} user={user} />
          )}
        </section>
      </div>
    </div>
  );
}

export function Pager({ page, total, onPage }: { readonly page: number; readonly total: number; readonly onPage: (page: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const from = (page - 1) * PAGE_SIZE + 1;
  const to = Math.min(page * PAGE_SIZE, total);
  return (
    <div className="pager">
      <span className="muted">{t('partner.pageInfo', { from, to, total: total.toLocaleString('ro-RO') })}</span>
      <div className="actions">
        <button type="button" className="btn ghost" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label={t('partner.prevPage')}>
          ‹
        </button>
        <span>{`${page} / ${pages}`}</span>
        <button type="button" className="btn ghost" disabled={page >= pages} onClick={() => onPage(page + 1)} aria-label={t('partner.nextPage')}>
          ›
        </button>
      </div>
    </div>
  );
}

/** Proprietatile marcate „filtrabile” apar ca filtru al listei (02.01, 3.2): lista de optiuni sau da/nu. */
function PropertyFilter({ lookups, propertyId, value, onChange }: {
  readonly lookups: PartnerLookups;
  readonly propertyId: number | undefined;
  readonly value: string | undefined;
  readonly onChange: (propertyId: number | undefined, value: string | undefined) => void;
}) {
  const filterable = useMemo(
    () => lookups.properties.filter((p) => p.isFilterable && (p.dataType === DataType.List || p.dataType === DataType.MultiList || p.dataType === DataType.Bool)),
    [lookups.properties],
  );
  if (filterable.length === 0) {
    return null;
  }
  const property = filterable.find((p) => p.id === propertyId);
  const values = property === undefined
    ? []
    : property.dataType === DataType.Bool
      ? [{ value: 'true', text: t('common.yes') }, { value: 'false', text: t('common.no') }]
      : lookups.options.filter((o) => o.propertyDefinitionId === property.id && o.isActive).map((o) => ({ value: String(o.id), text: o.label }));
  return (
    <div className="chips-row property-filter">
      <select className="chip" aria-label={t('partner.filter.property')} value={propertyId ?? ''}
        onChange={(e) => onChange(e.target.value === '' ? undefined : Number(e.target.value), undefined)}>
        <option value="">{t('partner.filter.noProperty')}</option>
        {filterable.map((p) => (
          <option key={p.id} value={p.id}>{p.name}</option>
        ))}
      </select>
      {property !== undefined && (
        <select className="chip" aria-label={t('partner.filter.value')} value={value ?? ''}
          onChange={(e) => onChange(property.id, e.target.value === '' ? undefined : e.target.value)}>
          <option value="">{t('common.select')}</option>
          {values.map((v) => (
            <option key={v.value} value={v.value}>{v.text}</option>
          ))}
        </select>
      )}
    </div>
  );
}

/** Partener nou: doar Date generale si Proprietati (adresele, conturile si contactele se adauga dupa prima salvare). */
function NewPartnerPanel({ lookups, orgLookups, canManage, onSaved, onCancel }: {
  readonly lookups: PartnerLookups;
  readonly orgLookups: OrgLookups;
  readonly canManage: boolean;
  readonly onSaved: (id: number) => void;
  readonly onCancel: () => void;
}) {
  const [section, setSection] = useState<PartnerSection>('general');
  const user = useOutletContext<CurrentUser>();
  const context = useContextNode(user);
  const newOwner = { key: contextOwner(context), name: contextName(user, context) };
  return (
    <div className="company-panel">
      <div className="tabs" role="tablist" aria-label={t('partner.newTitle')}>
        {(['general', 'properties'] as const).map((key) => (
          <button key={key} type="button" role="tab" id={`tab-${key}`} aria-selected={section === key} aria-controls="partner-form"
            tabIndex={section === key ? 0 : -1} className={section === key ? 'tab on' : 'tab'} onClick={() => setSection(key)}
            onKeyDown={(event) => {
              if (tabKeyTarget(0, event.key, 2) !== undefined) {
                event.preventDefault();
                setSection(section === 'general' ? 'properties' : 'general');
              }
            }}>
            {t(key === 'general' ? 'partner.tab.general' : 'partner.tab.properties')}
          </button>
        ))}
      </div>
      <div id="partner-form" role="tabpanel" aria-labelledby={`tab-${section}`}>
        <PartnerForm key="new" partner={undefined} lookups={lookups} orgLookups={orgLookups} section={section} canManage={canManage}
          canViewPersonalData={false} onSaved={(saved) => onSaved(saved.id)} onCancel={onCancel} newOwner={newOwner} />
      </div>
      <p className="muted small">{t('partner.saveFirst')}</p>
    </div>
  );
}

/** Ce e blocat la partener, pentru eticheta din lista. */
function blockedTitle(mask: number): string {
  return blockedText(mask, 7, (f) => t(`block.partner.${f as 1 | 2 | 4}`), t('block.all')).toLocaleLowerCase('ro-RO');
}

type PartnerTab = PartnerSection | 'addresses' | 'bank' | 'contacts' | 'block' | 'dimensions' | 'history';

/** Fisa partenerului cu tab-urile machetei; un panou se monteaza la prima vizita si apoi doar se ascunde (UI-18). */
function PartnerPanel({ partnerId, lookups, orgLookups, user }: {
  readonly partnerId: number;
  readonly lookups: PartnerLookups;
  readonly orgLookups: OrgLookups;
  readonly user: CurrentUser;
}) {
  const partner = usePartner(partnerId);
  const [tab, setTab] = useState<PartnerTab>('general');
  const [visited, setVisited] = useState<ReadonlySet<PartnerTab>>(() => new Set<PartnerTab>(['general']));
  const tabButtons = useRef(new Map<PartnerTab, HTMLButtonElement>());
  const canManage = user.permissions.includes('Core.Partner.Manage');
  const canViewPersonalData = user.permissions.includes('Core.Partner.ViewPersonalData');

  if (partner.data === undefined) {
    return <p role="status">{partner.isError ? partner.error.message : t('app.loading')}</p>;
  }
  const data = partner.data;
  const tabs: [PartnerTab, string][] = [
    ['general', t('partner.tab.general')],
    ['properties', t('partner.tab.properties')],
    ['addresses', t('partner.tab.addresses')],
    ['bank', t('partner.tab.bank')],
    ['contacts', t('partner.tab.contacts')],
    ['block', t('partner.tab.block')],
    ['dimensions', t('tab.dimensions')],
    ['history', t('partner.tab.history')],
  ];
  const open = (next: PartnerTab, focus = false) => {
    setTab(next);
    setVisited((current) => (current.has(next) ? current : new Set([...current, next])));
    if (focus) {
      tabButtons.current.get(next)?.focus();
    }
  };
  const panel = (key: PartnerTab, content: ReactNode) =>
    visited.has(key) && (
      <div role="tabpanel" id={`panel-${key}`} aria-labelledby={`tab-${key}`} hidden={tab !== key}>
        {content}
      </div>
    );
  const formSection: PartnerSection | undefined = tab === 'general' || tab === 'properties' ? tab : undefined;

  return (
    <div className="company-panel">
      <div className="panel-head">
        <div>
          <h2>
            {data.name} <span className={data.isActive ? 'pill ok' : 'pill off'}>{data.isActive ? t('common.active') : t('common.inactive')}</span>
          </h2>
          <div className="muted">{t('partner.header', { code: data.code, date: formatDate(new Date(data.createdAt)) })}</div>
          <div className="chips-row">
            <span className="chip static">{t('partner.addressCount', { count: data.addresses.length })}</span>
            <span className="chip static">{t('partner.bankCount', { count: data.bankAccounts.length })}</span>
            <span className="chip static">{t('partner.contactCount', { count: data.contacts.length })}</span>
          </div>
        </div>
      </div>
      <div className="tabs" role="tablist" aria-label={data.name}>
        {tabs.map(([key, label], index) => (
          <button
            key={key}
            ref={(button) => {
              if (button === null) {
                tabButtons.current.delete(key);
              } else {
                tabButtons.current.set(key, button);
              }
            }}
            type="button"
            role="tab"
            id={`tab-${key}`}
            aria-selected={tab === key}
            aria-controls={key === 'general' || key === 'properties' ? 'partner-form' : visited.has(key) ? `panel-${key}` : undefined}
            tabIndex={tab === key ? 0 : -1}
            className={tab === key ? 'tab on' : 'tab'}
            onClick={() => open(key)}
            onKeyDown={(event) => {
              const target = tabKeyTarget(index, event.key, tabs.length);
              const next = target === undefined ? undefined : tabs[target]?.[0];
              if (next !== undefined) {
                event.preventDefault();
                open(next, true);
              }
            }}
          >
            {label}
          </button>
        ))}
      </div>
      {/* Date generale si Proprietati sunt un singur formular, cu o singura salvare; ramane montat pe celelalte tab-uri. */}
      <div id="partner-form" role="tabpanel" aria-labelledby={`tab-${formSection ?? 'general'}`} hidden={formSection === undefined}>
        <PartnerForm partner={data} lookups={lookups} orgLookups={orgLookups} section={formSection} canManage={canManage}
          canViewPersonalData={canViewPersonalData} onSaved={() => undefined} onCancel={() => undefined} />
      </div>
      {panel('addresses', <PartnerAddressesTab partner={data} orgLookups={orgLookups} canManage={canManage} />)}
      {panel('bank', <PartnerBankAccountsTab partner={data} canManage={canManage} />)}
      {panel('contacts', <PartnerContactsTab partner={data} canManage={canManage} />)}
      {panel('block', <PartnerBlockTab partnerId={partnerId} active={tab === 'block'} canManage={canManage} isCustomer={data.isCustomer}
        canCredit={user.permissions.includes('Core.Partner.ManageCredit')} />)}
      {panel('dimensions', <DefaultDimensionsTab source={1} sourceId={partnerId} active={tab === 'dimensions'}
        canManage={user.permissions.includes('Core.Dimension.Manage')} />)}
      {panel('history', <PartnerHistoryTab partnerId={partnerId} active={tab === 'history'} />)}
    </div>
  );
}
