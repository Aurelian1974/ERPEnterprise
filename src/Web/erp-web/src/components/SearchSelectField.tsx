import { useEffect, useMemo, useState } from 'react';
import { Controller, type Control, type FieldPath, type FieldValues } from 'react-hook-form';
import { DropDownListComponent, type FilteringEventArgs } from '@syncfusion/ej2-react-dropdowns';
import { t } from '../i18n';
import type { Option } from './Field';

/**
 * Alegerea dintr-o lista mare cautata pe server (parteneri, angajati): lista arata optiunea curenta si primele rezultate ale
 * serverului (cautarea fara text, incarcata o data la afisarea campului), iar textul tastat in filtru aduce rezultatele
 * potrivite. Valoarea formularului e id-ul numeric sau null.
 */
export function SearchSelectField<T extends FieldValues>({ control, name, label, disabled, className, current, search }: {
  readonly control: Control<T>;
  readonly name: FieldPath<T>;
  readonly label: string;
  readonly disabled?: boolean;
  readonly className?: string;
  /** Optiunea deja aleasa (din fisa), ca valoarea sa se vada inainte de orice cautare. */
  readonly current: Option | undefined;
  readonly search: (text: string) => Promise<Option[]>;
}) {
  const id = `f-${name.replace(/[^A-Za-z0-9]/g, '-')}`;
  // Proprietati stabile (UI-12): lista initiala se schimba doar cu optiunea curenta; `search` trebuie sa fie o functie stabila
  // (definita in afara componentei sau memorata), altfel filtrul se recreeaza la fiecare randare.
  const [first, setFirst] = useState<readonly Option[]>([]);
  useEffect(() => {
    let active = true;
    void search('').then(
      (options) => {
        if (active) {
          setFirst(options);
        }
      },
      () => undefined,
    );
    return () => {
      active = false;
    };
  }, [search]);
  // Lista se schimba doar cand se schimba continutul (optiunea curenta sau primele rezultate), nu la fiecare randare.
  const key = JSON.stringify([current ?? null, first]);
  const initial = useMemo(() => {
    const [selected, options] = JSON.parse(key) as [Option | null, Option[]];
    return selected === null ? options : [selected, ...options.filter((o) => o.value !== selected.value)];
  }, [key]);
  const filtering = useMemo(() => (args: FilteringEventArgs) => {
    args.preventDefaultAction = true;
    const text = args.text.trim();
    void search(text).then(
      (options) => args.updateData(options as unknown as { [key: string]: object }[]),
      () => args.updateData([]),
    );
  }, [search]);
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <div className={className === undefined ? 'field' : `field ${className}`}>
          <label htmlFor={id} id={`${id}-label`}>
            {label}
          </label>
          <DropDownListComponent
            id={id}
            dataSource={initial as unknown as { [key: string]: object }[]}
            fields={{ text: 'text', value: 'value' }}
            value={field.value === null || field.value === undefined ? null : String(field.value)}
            placeholder={t('common.searchSelect')}
            allowFiltering
            filterBarPlaceholder={t('common.searchType')}
            filtering={filtering}
            select={(args: { itemData: { value?: unknown } | null }) => {
              const raw = args.itemData?.value;
              field.onChange(raw === undefined || raw === null || raw === '' ? null : Number(raw));
            }}
            enabled={disabled !== true}
            cssClass={fieldState.error === undefined ? '' : 'e-error'}
            htmlAttributes={{ 'aria-label': label }}
          />
          {fieldState.error !== undefined && (
            <span className="field-error" id={`${id}-error`} role="alert">
              {fieldState.error.message}
            </span>
          )}
        </div>
      )}
    />
  );
}
