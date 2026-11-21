import { useContext, useEffect, useMemo, useRef, useState } from 'react';
import { useController, useWatch, type Control, type FieldValues, type Path, type PathValue, type UseFormReturn } from 'react-hook-form';
import { DropDownListComponent, type FilteringEventArgs } from '@syncfusion/ej2-react-dropdowns';
import { FieldIdPrefix, TextField } from '../components/Field';
import { t } from '../i18n';
import { cityLabel, searchCities, type CityOption } from './cityApi';

/** Campurile de adresa comune formularelor (firma, punct de lucru, locatie, adresele firmei si ale partenerului). */
interface AddressFields {
  countryCode: string | null;
  countyCode: string | null;
  city: string | null;
  citySirutaCode?: number | null | undefined;
  postalCode: string | null;
}

/**
 * Localitatea unei adrese (02.01, 3.5; e-Factura cere adrese corecte): in Romania, cu judetul ales, se alege din nomenclatorul
 * SIRUTA (cautare fara diacritice, cu comuna si codul postal); alegerea completeaza numele oficial si codul postal, daca lipseste.
 * In strainatate (sau fara judet) ramane text liber. O adresa veche, nelegata, isi arata textul pana la alegerea din lista;
 * serverul o leaga singur daca numele e unic in judet.
 */
export function CityField<T extends FieldValues & AddressFields>({ form, label, className }: {
  readonly form: UseFormReturn<T, unknown, unknown>;
  readonly label: string;
  readonly className?: string;
}) {
  const country = useWatch({ control: form.control, name: 'countryCode' as Path<T> }) as string | null;
  const county = useWatch({ control: form.control, name: 'countyCode' as Path<T> }) as string | null;
  const siruta = country === 'RO' && county !== null && county !== '';

  // Alt judet (sau alta tara): codul vechi nu mai e valabil; textul ramane, ca utilizatorul sa vada ce avea.
  const previousCounty = useRef(county);
  useEffect(() => {
    if (previousCounty.current !== county || !siruta) {
      if (form.getValues('citySirutaCode' as Path<T>) !== null) {
        form.setValue('citySirutaCode' as Path<T>, null as PathValue<T, Path<T>>, { shouldDirty: true });
      }
    }
    previousCounty.current = county;
  }, [form, county, siruta]);

  return siruta
    ? <SirutaSelect key={county} form={form} county={county} label={label} className={className} />
    : <TextField<T> control={form.control as unknown as Control<T>} name={'city' as Path<T>} label={label} {...(className === undefined ? {} : { className })} />;
}

function SirutaSelect<T extends FieldValues & AddressFields>({ form, county, label, className }: {
  readonly form: UseFormReturn<T, unknown, unknown>;
  readonly county: string;
  readonly label: string;
  readonly className?: string | undefined;
}) {
  const prefix = useContext(FieldIdPrefix);
  const id = `${prefix}-city`;
  const code = useController({ control: form.control, name: 'citySirutaCode' as Path<T> });
  const city = useController({ control: form.control, name: 'city' as Path<T> });
  const value = (code.field.value as number | null | undefined) ?? null;
  const text = (city.field.value as string | null) ?? '';
  const known = useRef(new Map<number, CityOption>());
  const [first, setFirst] = useState<CityOption[]>([]);

  // Primele localitati ale judetului (resedintele intai) si cea aleasa, o singura data pe judet.
  useEffect(() => {
    let active = true;
    void searchCities(county, '', form.getValues('citySirutaCode' as Path<T>) as number | null).then((list) => {
      if (active) {
        list.forEach((c) => known.current.set(c.sirutaCode, c));
        setFirst(list);
      }
    }, () => undefined);
    return () => {
      active = false;
    };
  }, [county, form]);

  const options = (list: readonly CityOption[]) => list.map((c) => ({ value: String(c.sirutaCode), text: cityLabel(c) }));
  const data = useMemo(() => options(first), [first]);
  const filtering = useMemo(() => (args: FilteringEventArgs) => {
    args.preventDefaultAction = true;
    void searchCities(county, args.text).then((list) => {
      list.forEach((c) => known.current.set(c.sirutaCode, c));
      args.updateData(options(list) as unknown as { [key: string]: object }[]);
    }, () => args.updateData([]));
  }, [county]);

  const error = city.fieldState.error?.message ?? code.fieldState.error?.message;
  return (
    <div className={className === undefined ? 'field' : `field ${className}`}>
      <label htmlFor={id} id={`${id}-label`}>{label}</label>
      <DropDownListComponent
        id={id}
        dataSource={data as unknown as { [key: string]: object }[]}
        fields={{ text: 'text', value: 'value' }}
        value={value === null ? null : String(value)}
        placeholder={t('city.choose')}
        popupWidth="380px"
        allowFiltering
        filterBarPlaceholder={t('city.search')}
        filtering={filtering}
        select={(args: { itemData: { value?: unknown } | null }) => {
          const chosen = known.current.get(Number(args.itemData?.value));
          if (chosen === undefined) {
            return;
          }
          code.field.onChange(chosen.sirutaCode);
          city.field.onChange(chosen.name);
          const postal = form.getValues('postalCode' as Path<T>) as string | null;
          if ((postal === null || postal === '') && chosen.postalCode !== null) {
            form.setValue('postalCode' as Path<T>, chosen.postalCode as PathValue<T, Path<T>>, { shouldDirty: true });
          }
        }}
        cssClass={error === undefined ? '' : 'e-error'}
        htmlAttributes={{ 'aria-label': label }}
      />
      {value === null && text !== '' && <span className="muted small">{t('city.notLinked', { city: text })}</span>}
      {error !== undefined && <span className="field-error" id={`${id}-error`} role="alert">{error}</span>}
    </div>
  );
}
