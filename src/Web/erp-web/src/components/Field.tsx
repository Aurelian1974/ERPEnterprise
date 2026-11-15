import { useMemo, type ReactNode } from 'react';
import { Controller, type Control, type FieldPath, type FieldValues } from 'react-hook-form';
import { NumericTextBoxComponent, TextBoxComponent } from '@syncfusion/ej2-react-inputs';
import { DropDownListComponent } from '@syncfusion/ej2-react-dropdowns';
import { SwitchComponent } from '@syncfusion/ej2-react-buttons';
import { t } from '../i18n';

// Campurile comune ale formularelor ERP (ST4: componente Syncfusion folosite prin componente proprii): eticheta,
// mesajul de eroare legat prin aria-describedby, starea dezactivata. Valorile sunt cele ale formularului (React Hook Form).

export interface Option {
  readonly value: string;
  readonly text: string;
}

interface FieldProps<T extends FieldValues> {
  readonly control: Control<T>;
  readonly name: FieldPath<T>;
  readonly label: string;
  readonly disabled?: boolean;
  readonly className?: string;
}

function Shell({ id, label, error, className, children }: { id: string; label: string; error?: string | undefined; className?: string | undefined; children: ReactNode }) {
  return (
    <div className={className === undefined ? 'field' : `field ${className}`}>
      <label htmlFor={id} id={`${id}-label`}>
        {label}
      </label>
      {children}
      {error !== undefined && (
        <span className="field-error" id={`${id}-error`} role="alert">
          {error}
        </span>
      )}
    </div>
  );
}

const fieldId = (name: string) => `f-${name.replace(/[^A-Za-z0-9]/g, '-')}`;

export function TextField<T extends FieldValues>({ control, name, label, disabled, className, upper, hint }: FieldProps<T> & { readonly upper?: boolean; readonly hint?: ReactNode }) {
  const id = fieldId(name);
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Shell id={id} label={label} error={fieldState.error?.message} className={className}>
          <div className="field-row">
            <TextBoxComponent
              id={id}
              value={(field.value as string | null | undefined) ?? ''}
              input={(args: { value: string }) => field.onChange(upper === true ? args.value.toUpperCase() : args.value)}
              blur={field.onBlur}
              enabled={disabled !== true}
              cssClass={fieldState.error === undefined ? '' : 'e-error'}
              htmlAttributes={{
                // Syncfusion pune aria-label="textbox"; eticheta vizibila are prioritate prin aria-labelledby.
                'aria-labelledby': `${id}-label`,
                'aria-invalid': fieldState.error === undefined ? 'false' : 'true',
                ...(fieldState.error === undefined ? {} : { 'aria-describedby': `${id}-error` }),
              }}
            />
            {hint}
          </div>
        </Shell>
      )}
    />
  );
}

export function NumberField<T extends FieldValues>({ control, name, label, disabled, className, min, max, grouping = true }: FieldProps<T> & {
  readonly min?: number;
  readonly max?: number;
  /** Separatorul de mii; fara el pentru ani si coduri numerice. */
  readonly grouping?: boolean;
}) {
  const id = fieldId(name);
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Shell id={id} label={label} error={fieldState.error?.message} className={className}>
          <NumericTextBoxComponent
            id={id}
            value={field.value as number}
            format={grouping ? 'n0' : '0'}
            decimals={0}
            validateDecimalOnType
            showSpinButton={false}
            {...(min === undefined ? {} : { min })}
            {...(max === undefined ? {} : { max })}
            change={(args: { value: number | null }) => field.onChange(args.value ?? 0)}
            enabled={disabled !== true}
            cssClass={fieldState.error === undefined ? '' : 'e-error'}
            htmlAttributes={{ 'aria-labelledby': `${id}-label` }}
          />
        </Shell>
      )}
    />
  );
}

export function SelectField<T extends FieldValues>({ control, name, label, disabled, className, options, allowEmpty, numeric }: FieldProps<T> & {
  readonly options: readonly Option[];
  readonly allowEmpty?: string;
  readonly numeric?: boolean;
}) {
  const id = fieldId(name);
  // Lista stabila intre randari (aceeasi referinta cat timp continutul nu se schimba): o lista noua face DropDownList sa
  // se relege si sa piarda valoarea aleasa in aceeasi randare.
  const key = JSON.stringify([allowEmpty, options]);
  const data = useMemo(() => {
    const [empty, list] = JSON.parse(key) as [string | null, Option[]];
    return empty === null ? list : [{ value: '', text: empty }, ...list];
  }, [key]);
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Shell id={id} label={label} error={fieldState.error?.message} className={className}>
          <DropDownListComponent
            id={id}
            dataSource={data as unknown as { [key: string]: object }[]}
            fields={{ text: 'text', value: 'value' }}
            value={field.value === null || field.value === undefined ? '' : String(field.value)}
            placeholder={t('common.select')}
            allowFiltering={data.length > 10}
            select={(args: { itemData: { value?: unknown } | null }) => {
              // „select” vine doar din alegerea utilizatorului (mouse sau tastatura); „change” apare si la relegarea listei.
              const raw = args.itemData?.value;
              const value = raw === undefined || raw === null || raw === '' ? null : String(raw);
              field.onChange(numeric === true && value !== null ? Number(value) : value);
            }}
            enabled={disabled !== true}
            cssClass={fieldState.error === undefined ? '' : 'e-error'}
            htmlAttributes={{ 'aria-label': label }}
          />
        </Shell>
      )}
    />
  );
}

export function SwitchField<T extends FieldValues>({ control, name, label, disabled, className, onText }: FieldProps<T> & { readonly onText?: string }) {
  const id = fieldId(name);
  return (
    <Controller
      control={control}
      name={name}
      render={({ field }) => (
        <Shell id={id} label={label} className={className}>
          <div className="switch-row">
            <SwitchComponent
              id={id}
              checked={field.value === true}
              change={(args: { checked: boolean }) => field.onChange(args.checked)}
              disabled={disabled === true}
              htmlAttributes={{ 'aria-label': label }}
            />
            <span aria-hidden="true">{field.value === true ? (onText ?? t('common.yes')) : t('common.no')}</span>
          </div>
        </Shell>
      )}
    />
  );
}

/** Mesajul unei erori de salvare sau incarcare (ProblemDetails.title). */
export function ErrorBanner({ message }: { readonly message: string | undefined }) {
  return message === undefined ? null : (
    <div className="banner error" role="alert">
      {message}
    </div>
  );
}
