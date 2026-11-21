import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';
import { t } from '../i18n';

export type CityOption = components['schemas']['CityOptionDto'];

/** Localitatile SIRUTA ale judetului care incep cu textul dat (fara diacritice), cel mult 30; sirutaCode = si cea aleasa. */
export async function searchCities(county: string, search: string, sirutaCode?: number | null): Promise<CityOption[]> {
  return unwrap(await api.GET('/api/core/cities', {
    params: {
      query: {
        County: county,
        ...(search.trim() === '' ? {} : { Search: search.trim() }),
        ...(sirutaCode === null || sirutaCode === undefined ? {} : { SirutaCode: sirutaCode }),
      },
    },
  }));
}

/** Prescurtarea unitatii (TIP din SIRUTA): municipiu, oras, comuna; Bucurestiul fara prescurtare. */
function unitPrefix(kind: number | null | undefined): string {
  switch (kind) {
    case 1: case 4: return t('city.unit.municipality');
    case 2: case 5: return t('city.unit.town');
    case 3: return t('city.unit.commune');
    default: return '';
  }
}

/**
 * Textul din lista: localitatea, apoi unitatea careia ii apartine si codul postal („Gârbova de Jos · mun. Aiud · 515204”);
 * localitatea cu acelasi nume ca unitatea ei (resedinta) nu repeta numele.
 */
export function cityLabel(city: CityOption): string {
  const parts = [city.name];
  if (city.parentName !== null && city.parentName !== city.name) {
    const prefix = unitPrefix(city.parentKind);
    parts.push(prefix === '' ? city.parentName : `${prefix} ${city.parentName}`);
  }
  if (city.postalCode !== null) {
    parts.push(city.postalCode);
  }
  return parts.join(' · ');
}
