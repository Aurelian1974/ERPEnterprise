// ParteneriPage — /administration/parteneri
// Design reference: inspiration/parteneri_page_v3.html

import { zodResolver } from '@hookform/resolvers/zod'
import { ChevronDown, CircleCheck, CreditCard, Info, Loader2, MapPin, Pencil, Plus, Search, User, X } from 'lucide-react'
import { AppModal, AppModalFooter } from '../../../../components/ui/AppModal'
import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { Controller, useForm } from 'react-hook-form'
import { apiClient } from '../../../../lib/axios'
import { usePartnerTypes } from '../../../../features/administration/partner-types/api'
import {
  useAnafLookup,
  useCreatePartner,
  useNextPartnerCode,
  usePartner,
  usePartnersList,
  useUpdatePartner,
  useUpsertAddress,
  useUpsertBankAccount,
  useUpsertContact,
  useVerifyAnaf,
} from '../../../../features/administration/partners/api'
import {
  addressSchema,
  type AddressFormValues,
  bankAccountSchema,
  type BankAccountFormValues,
  contactSchema,
  type ContactFormValues,
  partnerSchema,
  type PartnerFormValues,
} from '../../../../features/administration/partners/schemas'
import { usePartnersUiStore } from '../../../../features/administration/partners/store'
import type {
  AnafAdresaSediuSocialDto,
  PartnerAddressDto,
  PartnerBankAccountDto,
  PartnerContactDto,
  PartnerDetailDto,
} from '../../../../features/administration/partners/types'

// ─── Design tokens ────────────────────────────────────────────────────────────

const C = {
  border:    '#E5E7EB',
  bg:        '#FFFFFF',
  bgSubtle:  '#F8F9FB',
  bgMuted:   '#F1F3F6',
  text:      '#111827',
  textSec:   '#4B5563',
  textMut:   '#9CA3AF',
  primary:   '#1E88D0',
  blue:      '#185FA5',
  selected:  '#E6F1FB',
  selHover:  '#dcedf8',
  activeBg:  '#EAF3DE',
  activeTxt: '#3B6D11',
  editBrd:   '#B5D4F4',
} as const

const inputSt: React.CSSProperties = {
  width: '100%', fontSize: 13, padding: '5px 8px',
  borderRadius: 6, border: `0.5px solid ${C.border}`,
  background: C.bg, color: C.text, outline: 'none',
}
const btnSaveSt: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: 5,
  fontSize: 12, padding: '6px 16px',
  borderRadius: 6, border: `0.5px solid ${C.blue}`,
  background: C.blue, color: '#fff', cursor: 'pointer',
}
const btnCancelSt: React.CSSProperties = {
  fontSize: 12, padding: '6px 14px',
  borderRadius: 6, border: `0.5px solid ${C.border}`,
  background: 'transparent', color: C.textSec, cursor: 'pointer',
}

// ─── FLabel ──────────────────────────────────────────────────────────────────

function FLabel({ children }: { children: React.ReactNode }) {
  return (
    <label style={{ display: 'block', fontSize: 11, color: C.textSec, marginBottom: 3 }}>
      {children}
    </label>
  )
}

// ─── InfoItem ─────────────────────────────────────────────────────────────────

function InfoItem({
  label, value, span, mono,
}: { label: string; value: string | null | undefined; span?: boolean; mono?: boolean }) {
  return (
    <div style={span ? { gridColumn: 'span 2' } : undefined}>
      <FLabel>{label}</FLabel>
      <span style={{ fontSize: 13, color: value ? C.text : C.textMut, fontFamily: mono ? 'monospace' : undefined }}>
        {value || '—'}
      </span>
    </div>
  )
}

// ─── EditItem ─────────────────────────────────────────────────────────────────

function EditItem({
  label, error, span, children,
}: { label: string; error?: string; span?: boolean; children: React.ReactNode }) {
  return (
    <div style={span ? { gridColumn: 'span 2' } : undefined}>
      <FLabel>{label}</FLabel>
      {children}
      {error && <p style={{ fontSize: 10, color: '#EF4444', marginTop: 2 }}>{error}</p>}
    </div>
  )
}

// ─── Collapsible ──────────────────────────────────────────────────────────────

function Collapsible({
  icon, title, count, onAdd, children,
}: {
  icon: string
  title: string
  count: number
  onAdd: () => void
  children: React.ReactNode
}) {
  const [open, setOpen] = useState(false)

  return (
    <div style={{ border: `0.5px solid ${C.border}`, borderRadius: 6, overflow: 'hidden', marginBottom: 12 }}>
      <div
        onClick={() => setOpen(!open)}
        style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '9px 12px', background: C.bgSubtle, cursor: 'pointer', userSelect: 'none' }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span className={`ti ${icon}`} style={{ fontSize: 14, color: C.textSec }} aria-hidden="true" />
          <span style={{ fontSize: 12, fontWeight: 500, color: C.text }}>{title}</span>
          <span style={{ background: C.selected, color: C.blue, fontSize: 10, padding: '1px 7px', borderRadius: 20 }}>{count}</span>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onAdd() }}
            style={{ display: 'inline-flex', alignItems: 'center', gap: 3, fontSize: 11, padding: '3px 9px', border: `0.5px solid ${C.border}`, borderRadius: 4, background: 'transparent', color: C.textSec, cursor: 'pointer' }}
          >
            <Plus size={11} />
            Adaugă
          </button>
          <ChevronDown
            size={14}
            color={C.textSec}
            style={{ transition: 'transform 0.2s', transform: open ? 'rotate(180deg)' : undefined }}
          />
        </div>
      </div>
      {open && (
        <div style={{ borderTop: `0.5px solid ${C.border}`, overflowX: 'auto' }}>
          {children}
        </div>
      )}
    </div>
  )
}

// ─── Sub-entity modals ────────────────────────────────────────────────────────

function AddressModal({
  partnerId, address, existingAddresses, onClose,
}: { partnerId: string; address?: PartnerAddressDto; existingAddresses?: PartnerAddressDto[]; onClose: () => void }) {
  const upsert = useUpsertAddress(partnerId)
  const { register, handleSubmit, watch, formState: { errors } } = useForm<AddressFormValues>({
    resolver: zodResolver(addressSchema),
    defaultValues: address
      ? { id: address.id, addressType: address.addressType, street: address.street, city: address.city, county: address.county ?? '', postalCode: address.postalCode ?? '', country: address.country, isPrimary: address.isPrimary }
      : { country: 'România', isPrimary: false },
  })

  const isPrimaryChecked = watch('isPrimary')
  const otherPrimary = existingAddresses?.find(a => a.isPrimary && a.id !== address?.id) ?? null
  const primaryConflict = isPrimaryChecked && otherPrimary !== null

  function onSubmit(values: AddressFormValues) {
    if (primaryConflict) return
    upsert.mutate(values, { onSuccess: onClose })
  }

  return createPortal(
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
      <AppModal
        title={address ? 'Editare adresă' : 'Adresă nouă'}
        subtitle={address?.addressType}
        icon={<MapPin size={14} className="text-[#1E88D0]" />}
        size="md"
        onClose={onClose}
        footer={<AppModalFooter onClose={onClose} pending={upsert.isPending} disabled={primaryConflict} cancelLabel="Anulează" />}
      >
        <div className="space-y-4 px-6 py-5">
          <div>
            <label className={labelCls}>Tip adresă <span className="normal-case text-red-500">*</span></label>
            <select {...register('addressType')} className={inputCls}>
              <option value="">— selectați —</option>
              <option value="Sediu social">Sediu social</option>
              <option value="Punct de lucru">Punct de lucru</option>
              <option value="Depozit">Depozit</option>
              <option value="Livrare">Livrare</option>
              <option value="Facturare">Facturare</option>
            </select>
            {errors.addressType && <p className={errCls}>{errors.addressType.message}</p>}
          </div>
          <div>
            <label className={labelCls}>Stradă <span className="normal-case text-red-500">*</span></label>
            <input {...register('street')} className={inputCls} />
            {errors.street && <p className={errCls}>{errors.street.message}</p>}
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={labelCls}>Localitate <span className="normal-case text-red-500">*</span></label>
              <input {...register('city')} className={inputCls} />
              {errors.city && <p className={errCls}>{errors.city.message}</p>}
            </div>
            <div>
              <label className={labelCls}>Județ</label>
              <input {...register('county')} className={inputCls} />
            </div>
            <div>
              <label className={labelCls}>Cod poștal</label>
              <input {...register('postalCode')} className={inputCls} />
            </div>
            <div>
              <label className={labelCls}>Țară</label>
              <input {...register('country')} className={inputCls} />
            </div>
          </div>
          <div className="rounded-lg border border-[#E5E7EB] bg-[#F8F9FB]">
            <label className="flex cursor-pointer items-center justify-between px-4 py-3">
              <div>
                <span className="text-sm font-medium text-[#111827]">Adresă principală</span>
                <p className="text-xs text-[#9CA3AF]">Afișată implicit pe documente</p>
              </div>
              <input type="checkbox" {...register('isPrimary')} className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]" />
            </label>
          </div>
          {primaryConflict && otherPrimary && (
            <div className="flex items-start gap-2 rounded-md border border-[#BFDBFE] bg-[#EFF6FF] px-3 py-2.5">
              <Info size={14} className="mt-0.5 shrink-0 text-[#1E88D0]" />
              <p className="text-xs text-[#1E40AF]">
                Adresa <strong>«{otherPrimary.addressType}»</strong> ({otherPrimary.city}) este deja marcată ca principală.
                Modificați-o mai întâi pentru a putea seta această adresă ca principală.
              </p>
            </div>
          )}
        </div>
      </AppModal>
    </form>
  , document.body)
}

function ContactModal({
  partnerId, contact, onClose,
}: { partnerId: string; contact?: PartnerContactDto; onClose: () => void }) {
  const upsert = useUpsertContact(partnerId)
  const { register, handleSubmit, formState: { errors } } = useForm<ContactFormValues>({
    resolver: zodResolver(contactSchema),
    defaultValues: contact
      ? { id: contact.id, fullName: contact.fullName, position: contact.position ?? '', phone: contact.phone ?? '', email: contact.email ?? '', isPrimary: contact.isPrimary }
      : { isPrimary: false },
  })

  function onSubmit(values: ContactFormValues) {
    upsert.mutate(values, { onSuccess: onClose })
  }

  return createPortal(
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
      <AppModal
        title={contact ? 'Editare persoană contact' : 'Persoană contact nouă'}
        subtitle={contact?.fullName}
        icon={<User size={14} className="text-[#1E88D0]" />}
        size="md"
        onClose={onClose}
        footer={<AppModalFooter onClose={onClose} pending={upsert.isPending} cancelLabel="Anulează" />}
      >
        <div className="space-y-4 px-6 py-5">
          <div className="grid grid-cols-2 gap-4">
            <div className="col-span-2">
              <label className={labelCls}>Nume complet <span className="normal-case text-red-500">*</span></label>
              <input {...register('fullName')} className={inputCls} />
              {errors.fullName && <p className={errCls}>{errors.fullName.message}</p>}
            </div>
            <div>
              <label className={labelCls}>Funcție</label>
              <input {...register('position')} className={inputCls} />
            </div>
            <div>
              <label className={labelCls}>Telefon</label>
              <input {...register('phone')} className={inputCls} />
            </div>
            <div className="col-span-2">
              <label className={labelCls}>Email</label>
              <input {...register('email')} type="email" className={inputCls} />
              {errors.email && <p className={errCls}>{errors.email.message}</p>}
            </div>
          </div>
          <div className="rounded-lg border border-[#E5E7EB] bg-[#F8F9FB]">
            <label className="flex cursor-pointer items-center justify-between px-4 py-3">
              <div>
                <span className="text-sm font-medium text-[#111827]">Contact principal</span>
                <p className="text-xs text-[#9CA3AF]">Afișat implicit pe documente</p>
              </div>
              <input type="checkbox" {...register('isPrimary')} className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]" />
            </label>
          </div>
        </div>
      </AppModal>
    </form>
  , document.body)
}

function BankAccountModal({
  partnerId, account, onClose,
}: { partnerId: string; account?: PartnerBankAccountDto; onClose: () => void }) {
  const upsert = useUpsertBankAccount(partnerId)
  const { register, handleSubmit, formState: { errors } } = useForm<BankAccountFormValues>({
    resolver: zodResolver(bankAccountSchema),
    defaultValues: account
      ? { id: account.id, iban: account.iban, bankName: account.bankName, currency: account.currency, isDefault: account.isDefault }
      : { currency: 'RON', isDefault: false },
  })

  function onSubmit(values: BankAccountFormValues) {
    upsert.mutate(values, { onSuccess: onClose })
  }

  return createPortal(
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
      <AppModal
        title={account ? 'Editare cont bancar' : 'Cont bancar nou'}
        subtitle={account?.iban}
        icon={<CreditCard size={14} className="text-[#1E88D0]" />}
        size="md"
        onClose={onClose}
        footer={<AppModalFooter onClose={onClose} pending={upsert.isPending} cancelLabel="Anulează" />}
      >
        <div className="space-y-4 px-6 py-5">
          <div>
            <label className={labelCls}>IBAN <span className="normal-case text-red-500">*</span></label>
            <input {...register('iban')} placeholder="ex: RO49 AAAA 0000 0000 0000 0000" className={inputCls} />
            {errors.iban && <p className={errCls}>{errors.iban.message}</p>}
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={labelCls}>Bancă <span className="normal-case text-red-500">*</span></label>
              <input {...register('bankName')} placeholder="ex: Banca Transilvania" className={inputCls} />
              {errors.bankName && <p className={errCls}>{errors.bankName.message}</p>}
            </div>
            <div>
              <label className={labelCls}>Monedă</label>
              <select {...register('currency')} className={inputCls}>
                <option>RON</option>
                <option>EUR</option>
                <option>USD</option>
              </select>
            </div>
          </div>
          <div className="rounded-lg border border-[#E5E7EB] bg-[#F8F9FB]">
            <label className="flex cursor-pointer items-center justify-between px-4 py-3">
              <div>
                <span className="text-sm font-medium text-[#111827]">Cont implicit</span>
                <p className="text-xs text-[#9CA3AF]">Folosit automat pe documente</p>
              </div>
              <input type="checkbox" {...register('isDefault')} className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]" />
            </label>
          </div>
        </div>
      </AppModal>
    </form>
  , document.body)
}

// ─── Sub-entity tables ────────────────────────────────────────────────────────

const thSt: React.CSSProperties = {
  fontSize: 11, fontWeight: 500, color: C.textSec,
  textAlign: 'left', padding: '6px 10px',
}
const tdSt: React.CSSProperties = {
  fontSize: 12, padding: '7px 10px',
  borderTop: `0.5px solid ${C.border}`, color: C.text,
}
const monoSt: React.CSSProperties = { fontFamily: 'monospace', fontSize: 11, color: C.textSec }

function AddressMiniTable({ addresses, partnerId }: { addresses: PartnerAddressDto[]; partnerId: string }) {
  const [editItem, setEditItem] = useState<PartnerAddressDto | null>(null)
  const [addOpen, setAddOpen] = useState(false)

  if (addresses.length === 0) {
    return (
      <>
        <p style={{ padding: '10px 12px', fontSize: 12, color: C.textMut }}>Nicio adresă înregistrată.</p>
        {addOpen && <AddressModal partnerId={partnerId} existingAddresses={addresses} onClose={() => setAddOpen(false)} />}
      </>
    )
  }

  return (
    <>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={thSt}>Tip</th>
            <th style={thSt}>Adresă</th>
            <th style={thSt}>Localitate</th>
            <th style={thSt}>Județ</th>
            <th style={{ ...thSt, width: 36 }} />
          </tr>
        </thead>
        <tbody>
          {addresses.map((a) => (
            <tr key={a.id}>
              <td style={tdSt}>
                <span style={{ display: 'inline-block', fontSize: 10, padding: '1px 7px', borderRadius: 20, background: a.isPrimary ? C.selected : C.bgMuted, color: a.isPrimary ? C.blue : C.textSec, border: a.isPrimary ? 'none' : `0.5px solid ${C.border}` }}>
                  {a.addressType}
                </span>
              </td>
              <td style={tdSt}>{a.street}</td>
              <td style={tdSt}>{a.city}</td>
              <td style={tdSt}>{a.county ?? '—'}</td>
              <td style={{ ...tdSt, textAlign: 'center' }}>
                <button type="button" onClick={() => setEditItem(a)} style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: C.textSec, display: 'inline-flex', padding: 2 }}>
                  <Pencil size={13} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editItem && <AddressModal partnerId={partnerId} address={editItem} existingAddresses={addresses} onClose={() => setEditItem(null)} />}
      {addOpen && <AddressModal partnerId={partnerId} existingAddresses={addresses} onClose={() => setAddOpen(false)} />}
    </>
  )
}

function ContactMiniTable({ contacts, partnerId }: { contacts: PartnerContactDto[]; partnerId: string }) {
  const [editItem, setEditItem] = useState<PartnerContactDto | null>(null)

  if (contacts.length === 0) {
    return <p style={{ padding: '10px 12px', fontSize: 12, color: C.textMut }}>Nicio persoană de contact înregistrată.</p>
  }

  return (
    <>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={thSt}>Nume</th>
            <th style={thSt}>Funcție</th>
            <th style={{ ...thSt, ...monoSt }}>Telefon</th>
            <th style={{ ...thSt, ...monoSt }}>Email</th>
            <th style={{ ...thSt, width: 36 }} />
          </tr>
        </thead>
        <tbody>
          {contacts.map((c) => (
            <tr key={c.id}>
              <td style={{ ...tdSt, fontWeight: 500 }}>{c.fullName}</td>
              <td style={tdSt}>{c.position ?? '—'}</td>
              <td style={{ ...tdSt, ...monoSt }}>{c.phone ?? '—'}</td>
              <td style={{ ...tdSt, ...monoSt }}>{c.email ?? '—'}</td>
              <td style={{ ...tdSt, textAlign: 'center' }}>
                <button type="button" onClick={() => setEditItem(c)} style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: C.textSec, display: 'inline-flex', padding: 2 }}>
                  <Pencil size={13} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editItem && <ContactModal partnerId={partnerId} contact={editItem} onClose={() => setEditItem(null)} />}
    </>
  )
}

function BankAccountMiniTable({ accounts, partnerId }: { accounts: PartnerBankAccountDto[]; partnerId: string }) {
  const [editItem, setEditItem] = useState<PartnerBankAccountDto | null>(null)

  if (accounts.length === 0) {
    return <p style={{ padding: '10px 12px', fontSize: 12, color: C.textMut }}>Niciun cont bancar înregistrat.</p>
  }

  return (
    <>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={{ ...thSt, ...monoSt }}>IBAN</th>
            <th style={thSt}>Bancă</th>
            <th style={thSt}>Monedă</th>
            <th style={thSt}>Implicit</th>
            <th style={{ ...thSt, width: 36 }} />
          </tr>
        </thead>
        <tbody>
          {accounts.map((a) => (
            <tr key={a.id}>
              <td style={{ ...tdSt, ...monoSt }}>{a.iban}</td>
              <td style={tdSt}>{a.bankName}</td>
              <td style={tdSt}>{a.currency}</td>
              <td style={{ ...tdSt, textAlign: 'center' }}>
                {a.isDefault && <CircleCheck size={14} color={C.activeTxt} />}
              </td>
              <td style={{ ...tdSt, textAlign: 'center' }}>
                <button type="button" onClick={() => setEditItem(a)} style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: C.textSec, display: 'inline-flex', padding: 2 }}>
                  <Pencil size={13} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editItem && <BankAccountModal partnerId={partnerId} account={editItem} onClose={() => setEditItem(null)} />}
    </>
  )
}

// ─── Full tab tables ──────────────────────────────────────────────────────────

function TabHeader({ icon, title, count, onAdd }: { icon: string; title: string; count: number; onAdd: () => void }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 18px', borderBottom: `0.5px solid ${C.border}`, flexShrink: 0 }}>
      <span style={{ fontSize: 13, fontWeight: 500, color: C.text, display: 'flex', alignItems: 'center', gap: 6 }}>
        <span className={`ti ${icon}`} style={{ fontSize: 14, verticalAlign: -2 }} aria-hidden="true" />
        {title}
        <span style={{ background: C.selected, color: C.blue, fontSize: 10, padding: '1px 7px', borderRadius: 20, marginLeft: 2 }}>{count}</span>
      </span>
      <button
        type="button"
        onClick={onAdd}
        style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 12, padding: '5px 12px', border: `0.5px solid ${C.border}`, borderRadius: 6, background: 'transparent', color: C.textSec, cursor: 'pointer' }}
      >
        <Plus size={13} />
        Adaugă
      </button>
    </div>
  )
}

function AddressesFullTab({ addresses, partnerId }: { addresses: PartnerAddressDto[]; partnerId: string }) {
  const [addOpen, setAddOpen] = useState(false)
  const [editItem, setEditItem] = useState<PartnerAddressDto | null>(null)

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <TabHeader icon="ti-map-pin" title="Adrese" count={addresses.length} onAdd={() => setAddOpen(true)} />
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {addresses.length === 0
          ? <p style={{ padding: '16px 18px', fontSize: 13, color: C.textMut }}>Nicio adresă înregistrată.</p>
          : (
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  <th style={thSt}>Tip</th>
                  <th style={thSt}>Adresă</th>
                  <th style={thSt}>Localitate</th>
                  <th style={thSt}>Județ</th>
                  <th style={thSt}>Cod poștal</th>
                  <th style={{ ...thSt, width: 36 }} />
                </tr>
              </thead>
              <tbody>
                {addresses.map((a) => (
                  <tr key={a.id} style={{ background: C.bg }}>
                    <td style={tdSt}>
                      <span style={{ display: 'inline-block', fontSize: 10, padding: '1px 7px', borderRadius: 20, background: a.isPrimary ? C.selected : C.bgMuted, color: a.isPrimary ? C.blue : C.textSec, border: a.isPrimary ? 'none' : `0.5px solid ${C.border}` }}>
                        {a.addressType}
                      </span>
                    </td>
                    <td style={tdSt}>{a.street}</td>
                    <td style={tdSt}>{a.city}</td>
                    <td style={tdSt}>{a.county ?? '—'}</td>
                    <td style={{ ...tdSt, ...monoSt }}>{a.postalCode ?? '—'}</td>
                    <td style={{ ...tdSt, textAlign: 'center' }}>
                      <button type="button" onClick={() => setEditItem(a)} style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: C.textSec, display: 'inline-flex', padding: 2 }}>
                        <Pencil size={13} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </div>
        {addOpen && <AddressModal partnerId={partnerId} existingAddresses={addresses} onClose={() => setAddOpen(false)} />}
      {editItem && <AddressModal partnerId={partnerId} address={editItem} existingAddresses={addresses} onClose={() => setEditItem(null)} />}
    </div>
  )
}

function ContactsFullTab({ contacts, partnerId }: { contacts: PartnerContactDto[]; partnerId: string }) {
  const [addOpen, setAddOpen] = useState(false)
  const [editItem, setEditItem] = useState<PartnerContactDto | null>(null)

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <TabHeader icon="ti-users" title="Persoane de contact" count={contacts.length} onAdd={() => setAddOpen(true)} />
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {contacts.length === 0
          ? <p style={{ padding: '16px 18px', fontSize: 13, color: C.textMut }}>Nicio persoană de contact înregistrată.</p>
          : (
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  <th style={thSt}>Nume</th>
                  <th style={thSt}>Funcție</th>
                  <th style={{ ...thSt, ...monoSt }}>Telefon</th>
                  <th style={{ ...thSt, ...monoSt }}>Email</th>
                  <th style={{ ...thSt, width: 36 }} />
                </tr>
              </thead>
              <tbody>
                {contacts.map((c) => (
                  <tr key={c.id}>
                    <td style={{ ...tdSt, fontWeight: 500 }}>{c.fullName}</td>
                    <td style={tdSt}>{c.position ?? '—'}</td>
                    <td style={{ ...tdSt, ...monoSt }}>{c.phone ?? '—'}</td>
                    <td style={{ ...tdSt, ...monoSt }}>{c.email ?? '—'}</td>
                    <td style={{ ...tdSt, textAlign: 'center' }}>
                      <button type="button" onClick={() => setEditItem(c)} style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: C.textSec, display: 'inline-flex', padding: 2 }}>
                        <Pencil size={13} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </div>
      {addOpen && <ContactModal partnerId={partnerId} onClose={() => setAddOpen(false)} />}
      {editItem && <ContactModal partnerId={partnerId} contact={editItem} onClose={() => setEditItem(null)} />}
    </div>
  )
}

function BankAccountsFullTab({ accounts, partnerId }: { accounts: PartnerBankAccountDto[]; partnerId: string }) {
  const [addOpen, setAddOpen] = useState(false)
  const [editItem, setEditItem] = useState<PartnerBankAccountDto | null>(null)

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <TabHeader icon="ti-building-bank" title="Conturi bancare" count={accounts.length} onAdd={() => setAddOpen(true)} />
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {accounts.length === 0
          ? <p style={{ padding: '16px 18px', fontSize: 13, color: C.textMut }}>Niciun cont bancar înregistrat.</p>
          : (
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  <th style={{ ...thSt, ...monoSt }}>IBAN</th>
                  <th style={thSt}>Bancă</th>
                  <th style={thSt}>Monedă</th>
                  <th style={thSt}>Implicit</th>
                  <th style={{ ...thSt, width: 36 }} />
                </tr>
              </thead>
              <tbody>
                {accounts.map((a) => (
                  <tr key={a.id}>
                    <td style={{ ...tdSt, ...monoSt }}>{a.iban}</td>
                    <td style={tdSt}>{a.bankName}</td>
                    <td style={tdSt}>{a.currency}</td>
                    <td style={{ ...tdSt, textAlign: 'center' }}>
                      {a.isDefault && <CircleCheck size={14} color={C.activeTxt} />}
                    </td>
                    <td style={{ ...tdSt, textAlign: 'center' }}>
                      <button type="button" onClick={() => setEditItem(a)} style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: C.textSec, display: 'inline-flex', padding: 2 }}>
                        <Pencil size={13} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </div>
      {addOpen && <BankAccountModal partnerId={partnerId} onClose={() => setAddOpen(false)} />}
      {editItem && <BankAccountModal partnerId={partnerId} account={editItem} onClose={() => setEditItem(null)} />}
    </div>
  )
}

// ─── General Tab ──────────────────────────────────────────────────────────────

function GeneralTab({
  partner, isEditing, partnerFormId,
}: { partner: PartnerDetailDto; isEditing: boolean; partnerFormId: string }) {
  const updatePartner = useUpdatePartner(partner.id)
  const { cancelEditing } = usePartnersUiStore()
  const { data: partnerTypes } = usePartnerTypes()
  const [addAddress, setAddAddress] = useState(false)
  const [addContact, setAddContact] = useState(false)
  const [addAccount, setAddAccount] = useState(false)

  const { register, handleSubmit, control, reset, formState: { errors } } = useForm<PartnerFormValues>({
    resolver: zodResolver(partnerSchema),
    defaultValues: {
      code: partner.code, name: partner.name,
      cui: partner.cui ?? '', registrationNumber: partner.registrationNumber ?? '',
      legalForm: partner.legalForm ?? '', partnerTypeId: partner.partnerTypeId,
      isVatPayer: partner.isVatPayer, phone: partner.phone ?? '',
      email: partner.email ?? '', isActive: partner.isActive, notes: partner.notes ?? '',
    },
  })

  useEffect(() => {
    reset({
      code: partner.code, name: partner.name,
      cui: partner.cui ?? '', registrationNumber: partner.registrationNumber ?? '',
      legalForm: partner.legalForm ?? '', partnerTypeId: partner.partnerTypeId,
      isVatPayer: partner.isVatPayer, phone: partner.phone ?? '',
      email: partner.email ?? '', isActive: partner.isActive, notes: partner.notes ?? '',
    })
  }, [partner.id, reset])

  function onSubmit(values: PartnerFormValues) {
    updatePartner.mutate(values, { onSuccess: () => cancelEditing() })
  }

  const gridSt: React.CSSProperties = { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, marginBottom: 18 }

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      {/* Edit notice */}
      {isEditing && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 11, color: C.blue, background: C.selected, padding: '5px 18px', borderBottom: `0.5px solid ${C.editBrd}`, flexShrink: 0 }}>
          <Pencil size={13} />
          Mod editare activ — modifică câmpurile și salvează
        </div>
      )}

      {/* Scrollable body */}
      <form id={partnerFormId} onSubmit={handleSubmit(onSubmit)} style={{ flex: 1, overflowY: 'auto', padding: '16px 18px' }}>

        {/* Identificare (only in edit mode) */}
        {isEditing && (
          <>
            <p style={{ fontSize: 11, fontWeight: 500, color: C.textSec, letterSpacing: '0.04em', textTransform: 'uppercase', marginBottom: 10 }}>Identificare</p>
            <div style={{ ...gridSt }}>
              <EditItem label="Cod *" error={errors.code?.message}>
                <input {...register('code')} style={inputSt} />
              </EditItem>
              <EditItem label="Denumire *" error={errors.name?.message}>
                <input {...register('name')} style={inputSt} />
              </EditItem>
            </div>
          </>
        )}

        {/* Informații fiscale */}
        <p style={{ fontSize: 11, fontWeight: 500, color: C.textSec, letterSpacing: '0.04em', textTransform: 'uppercase', marginBottom: 10 }}>Informații fiscale</p>
        <div style={gridSt}>
          {isEditing ? (
            <>
              <EditItem label="CUI / CIF" error={errors.cui?.message}>
                <input {...register('cui')} style={inputSt} placeholder="ex: RO12345678" />
              </EditItem>
              <EditItem label="Reg. Comerțului">
                <input {...register('registrationNumber')} style={inputSt} />
              </EditItem>
              <EditItem label="Formă juridică">
                <select {...register('legalForm')} style={inputSt}>
                  <option value="">—</option>
                  <option>SRL</option><option>SA</option><option>PFA</option>
                  <option>RA</option><option>Persoană fizică</option>
                </select>
              </EditItem>
              <EditItem label="Tip partener">
                <select {...register('partnerTypeId', { setValueAs: (v: string) => v === '' ? null : parseInt(v, 10) })} style={inputSt}>
                  <option value="">—</option>
                  {partnerTypes?.map((pt) => (
                    <option key={pt.partnerTypeId} value={pt.partnerTypeId}>{pt.name}</option>
                  ))}
                </select>
              </EditItem>
              <EditItem label="Plătitor TVA">
                <Controller
                  control={control}
                  name="isVatPayer"
                  render={({ field }) => (
                    <select value={field.value ? 'true' : 'false'} onChange={(e) => field.onChange(e.target.value === 'true')} style={inputSt}>
                      <option value="true">Da</option>
                      <option value="false">Nu</option>
                    </select>
                  )}
                />
              </EditItem>
              <EditItem label="Telefon">
                <input {...register('phone')} style={inputSt} />
              </EditItem>
              <EditItem label="Email facturare" error={errors.email?.message} span>
                <input {...register('email')} type="email" style={inputSt} />
              </EditItem>
            </>
          ) : (
            <>
              <InfoItem label="CUI / CIF" value={partner.cui} mono />
              <InfoItem label="Reg. Comerțului" value={partner.registrationNumber} mono />
              <InfoItem label="Formă juridică" value={partner.legalForm} />
              <InfoItem label="Tip partener" value={partner.partnerTypeName} />
              <InfoItem label="Plătitor TVA" value={partner.isVatPayer ? 'Da' : 'Nu'} />
              <InfoItem label="Telefon" value={partner.phone} />
              <InfoItem label="Email facturare" value={partner.email} span />
              {partner.anafVerifiedAt && (
                <InfoItem
                  label="Verificat ANAF"
                  value={new Date(partner.anafVerifiedAt).toLocaleDateString('ro-RO', { day: '2-digit', month: '2-digit', year: 'numeric' })}
                  span
                />
              )}
            </>
          )}
        </div>

        {/* Collapsibles */}
        <Collapsible icon="ti-map-pin" title="Adrese" count={partner.addresses.length} onAdd={() => setAddAddress(true)}>
          <AddressMiniTable addresses={partner.addresses} partnerId={partner.id} />
        </Collapsible>
        <Collapsible icon="ti-users" title="Persoane de contact" count={partner.contacts.length} onAdd={() => setAddContact(true)}>
          <ContactMiniTable contacts={partner.contacts} partnerId={partner.id} />
        </Collapsible>
        <Collapsible icon="ti-building-bank" title="Conturi bancare" count={partner.bankAccounts.length} onAdd={() => setAddAccount(true)}>
          <BankAccountMiniTable accounts={partner.bankAccounts} partnerId={partner.id} />
        </Collapsible>
      </form>

      {addAddress && <AddressModal partnerId={partner.id} onClose={() => setAddAddress(false)} />}
      {addContact && <ContactModal partnerId={partner.id} onClose={() => setAddContact(false)} />}
      {addAccount && <BankAccountModal partnerId={partner.id} onClose={() => setAddAccount(false)} />}
    </div>
  )
}

// ─── Partner Detail ───────────────────────────────────────────────────────────

type TabKey = 'general' | 'adrese' | 'contacte' | 'conturi'

const TAB_LABELS: Record<TabKey, string> = {
  general: 'Date generale',
  adrese: 'Adrese',
  contacte: 'Persoane contact',
  conturi: 'Conturi bancare',
}

const FORM_ID = 'partner-general-form'

function PartnerDetail({ partnerId }: { partnerId: string }) {
  const { data: partner, isLoading } = usePartner(partnerId)
  const { isEditing, startEditing, cancelEditing } = usePartnersUiStore()
  const [tab, setTab] = useState<TabKey>('general')
  const verifyAnaf = useVerifyAnaf(partnerId)

  if (isLoading || !partner) {
    return (
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <p style={{ fontSize: 13, color: C.textSec }}>Se încarcă…</p>
      </div>
    )
  }

  const initials = partner.name
    .split(' ')
    .slice(0, 2)
    .map((w) => w[0] ?? '')
    .join('')
    .toUpperCase()

  function handleToggleEdit() {
    if (isEditing) {
      cancelEditing()
    } else {
      startEditing()
    }
  }

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>

      {/* Header */}
      <div style={{ padding: '14px 18px 0', borderBottom: `0.5px solid ${C.border}`, flexShrink: 0, background: C.bg }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
          {/* Avatar */}
          <div style={{ width: 36, height: 36, borderRadius: '50%', background: C.selected, display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, fontWeight: 500, color: C.blue, flexShrink: 0 }}>
            {initials}
          </div>
          {/* Name + meta */}
          <div style={{ flex: 1 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
              <span style={{ fontSize: 15, fontWeight: 500, color: C.text }}>{partner.name}</span>
              <span style={{ display: 'inline-block', fontSize: 11, padding: '2px 8px', borderRadius: 4, background: partner.isActive ? C.activeBg : C.bgMuted, color: partner.isActive ? C.activeTxt : C.textSec }}>
                {partner.isActive ? 'Activ' : 'Inactiv'}
              </span>
            </div>
            <div style={{ fontSize: 12, color: C.textSec, marginTop: 1 }}>
              {[partner.cui, partner.legalForm, partner.partnerTypeName].filter(Boolean).join(' · ')}
            </div>
          </div>
          {/* Acțiuni header */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexShrink: 0 }}>
            {partner.cui && (
              <button
                type="button"
                onClick={() => verifyAnaf.mutate()}
                disabled={verifyAnaf.isPending}
                title={partner.anafVerifiedAt
                  ? `Ultima verificare: ${new Date(partner.anafVerifiedAt).toLocaleDateString('ro-RO')}`
                  : 'Verifică date fiscale la ANAF'}
                style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 12, padding: '6px 12px', border: `0.5px solid ${C.border}`, borderRadius: 6, background: 'transparent', color: verifyAnaf.isError ? '#DC2626' : C.textSec, cursor: verifyAnaf.isPending ? 'wait' : 'pointer', opacity: verifyAnaf.isPending ? 0.6 : 1 }}
              >
                <span className="ti ti-building-bank" style={{ fontSize: 13 }} aria-hidden="true" />
                {verifyAnaf.isPending ? 'Verificare…' : 'Verifică ANAF'}
              </button>
            )}
            {/* Modifică / Închide */}
            <button
              type="button"
              onClick={handleToggleEdit}
              style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 12, padding: '6px 14px', border: `0.5px solid ${C.border}`, borderRadius: 6, background: 'transparent', color: C.textSec, cursor: 'pointer' }}
            >
              {isEditing ? <X size={14} /> : <Pencil size={14} />}
              {isEditing ? 'Închide' : 'Modifică'}
            </button>
          </div>
        </div>

        {/* Tabs */}
        <div style={{ display: 'flex', gap: 0 }}>
          {(Object.keys(TAB_LABELS) as TabKey[]).map((t) => (
            <button
              key={t}
              type="button"
              onClick={() => setTab(t)}
              style={{
                fontSize: 12, padding: '7px 14px', border: 'none', background: 'transparent',
                color: tab === t ? C.blue : C.textSec, cursor: 'pointer',
                borderBottom: tab === t ? `2px solid ${C.blue}` : '2px solid transparent',
                marginBottom: '-0.5px', userSelect: 'none',
                fontWeight: tab === t ? 500 : undefined,
              }}
            >
              {TAB_LABELS[t]}
            </button>
          ))}
        </div>
      </div>

      {/* Tab content */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        {tab === 'general' && <GeneralTab partner={partner} isEditing={isEditing} partnerFormId={FORM_ID} />}
        {tab === 'adrese' && <AddressesFullTab addresses={partner.addresses} partnerId={partner.id} />}
        {tab === 'contacte' && <ContactsFullTab contacts={partner.contacts} partnerId={partner.id} />}
        {tab === 'conturi' && <BankAccountsFullTab accounts={partner.bankAccounts} partnerId={partner.id} />}
      </div>

      {/* Action row */}
      {isEditing && tab === 'general' && (
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, padding: '10px 18px', borderTop: `0.5px solid ${C.border}`, flexShrink: 0, background: C.bg }}>
          <button type="button" onClick={cancelEditing} style={btnCancelSt}>Anulează</button>
          <button form={FORM_ID} type="submit" style={btnSaveSt}>
            <span className="ti ti-device-floppy" style={{ fontSize: 13 }} aria-hidden="true" />
            Salvează
          </button>
        </div>
      )}
    </div>
  )
}

const inputCls = 'w-full rounded-lg border border-[#E5E7EB] bg-white px-3 py-2.5 text-sm text-[#111827] placeholder:text-[#9CA3AF] transition-colors focus:border-[#1E88D0] focus:outline-none focus:ring-2 focus:ring-[#1E88D0]/20'
const labelCls = 'mb-1.5 block text-xs font-medium uppercase tracking-wide text-[#4B5563]'
const errCls = 'mt-1 text-xs text-red-600'

// ─── New Partner Modal ────────────────────────────────────────────────────────

function NewPartnerModal({ onClose }: { onClose: () => void }) {
  const createPartner = useCreatePartner()
  const anafLookup = useAnafLookup()
  const nextCode = useNextPartnerCode()
  const { selectPartner } = usePartnersUiStore()
  const { data: partnerTypes } = usePartnerTypes()

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    control,
    formState: { errors },
  } = useForm<PartnerFormValues>({
    resolver: zodResolver(partnerSchema),
    defaultValues: { isVatPayer: false, isActive: true },
  })

  useEffect(() => {
    if (nextCode.data) {
      setValue('code', nextCode.data)
    }
  }, [nextCode.data, setValue])

  const cuiValue = watch('cui')
  const [anafError, setAnafError] = useState<string | null>(null)
  const [anafName, setAnafName] = useState<string | null>(null)
  const [anafAdresa, setAnafAdresa] = useState<string | null>(null)
  const [anafSediuSocial, setAnafSediuSocial] = useState<AnafAdresaSediuSocialDto | null>(null)
  const [anafVerifiedAt, setAnafVerifiedAt] = useState<string | null>(null)

  async function handleAnafLookup() {
    if (!cuiValue?.trim()) return
    setAnafError(null)
    setAnafName(null)
    setAnafAdresa(null)
    setAnafSediuSocial(null)
    setAnafVerifiedAt(null)
    try {
      const data = await anafLookup.mutateAsync(cuiValue.trim())
      setValue('name', data.denumire, { shouldValidate: true, shouldDirty: true, shouldTouch: true })
      setValue('isVatPayer', data.isVatPayer, { shouldValidate: true, shouldDirty: true })
      if (data.nrRegCom) {
        setValue('registrationNumber', data.nrRegCom, { shouldValidate: true, shouldDirty: true })
      }
      if (data.telefon) {
        setValue('phone', data.telefon, { shouldValidate: true, shouldDirty: true })
      }
      if (data.formaJuridica) {
        setValue('legalForm', data.formaJuridica, { shouldValidate: true, shouldDirty: true })
      }
      setAnafName(data.denumire)
      setAnafAdresa(data.adresa ?? null)
      setAnafSediuSocial(data.adresaSediuSocial ?? null)
      setAnafVerifiedAt(new Date().toISOString())
    } catch {
      setAnafError('Verificarea ANAF a eșuat. Verificați CUI-ul introdus.')
    }
  }

  function onSubmit(values: PartnerFormValues) {
    createPartner.mutate(
      { ...values, anafVerifiedAt },
      {
        onSuccess: async (id) => {
          if (anafSediuSocial?.localitate) {
            const street = [anafSediuSocial.strada, anafSediuSocial.numar].filter(Boolean).join(' ') || '—'
            try {
              await apiClient.post(`/administration/partners/${id}/addresses`, {
                addressType: 'Sediu social',
                street,
                city: anafSediuSocial.localitate,
                county: anafSediuSocial.judet ?? null,
                postalCode: anafSediuSocial.codPostal ?? null,
                country: anafSediuSocial.tara || 'România',
                isPrimary: true,
              })
            } catch {
              // auto-creare adresă opțională — nu blochează fluxul
            }
          }
          onClose()
          selectPartner(id)
        },
      }
    )
  }



  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
    <AppModal
      title="Partener nou"
      icon={<Plus size={15} className="text-[#1E88D0]" />}
      size="xl"
      scrollable
      onClose={onClose}
      footer={
        <AppModalFooter
          onClose={onClose}
          pending={createPartner.isPending}
          submitLabel="Salvează partener"
        />
      }
    >
        <div className="space-y-5 px-6 py-5">

            {/* Identificare */}
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className={labelCls}>Cod <span className="normal-case text-red-500">*</span></label>
                <input {...register('code')} placeholder="ex: CLI001" className={inputCls} />
                {errors.code && <p className={errCls}>{errors.code.message}</p>}
              </div>
              <div>
                <label className={labelCls}>Tip partener</label>
                <select
                  {...register('partnerTypeId', { setValueAs: (v: string) => (v === '' ? null : Number(v)) })}
                  className={inputCls}
                >
                  <option value="">— selectează —</option>
                  {partnerTypes?.filter((t) => t.isActive).map((t) => (
                    <option key={t.partnerTypeId} value={t.partnerTypeId}>{t.name}</option>
                  ))}
                </select>
              </div>
            </div>

            {/* CUI + ANAF */}
            <div>
              <label className={labelCls}>CUI</label>
              <div className="flex gap-2">
                <input {...register('cui')} placeholder="ex: RO12345678" className={inputCls} />
                <button
                  type="button"
                  onClick={handleAnafLookup}
                  disabled={!cuiValue?.trim() || anafLookup.isPending}
                  className="flex shrink-0 items-center gap-1.5 rounded-lg border border-[#1E88D0] px-3 py-2 text-xs font-medium text-[#1E88D0] transition-colors hover:bg-[#EBF5FF] disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {anafLookup.isPending
                    ? <Loader2 size={12} className="animate-spin" />
                    : <Search size={12} />}
                  Verifică ANAF
                </button>
              </div>
              {errors.cui && <p className={errCls}>{errors.cui.message}</p>}

              {/* ANAF result */}
              {anafName && (
                <div className="mt-2 flex items-start gap-2 rounded-lg border border-green-200 bg-green-50 px-3 py-2">
                  <CircleCheck size={14} className="mt-0.5 shrink-0 text-green-600" />
                  <div>
                    <p className="text-xs text-green-700">
                      Date preluate ANAF: <span className="font-semibold">{anafName}</span>
                    </p>
                    {anafAdresa && (
                      <p className="mt-0.5 text-xs text-green-600">
                        <span className="font-medium">Adresă:</span> {anafAdresa}
                      </p>
                    )}
                    {anafSediuSocial?.localitate && (
                      <p className="mt-0.5 text-xs text-green-600">
                        Sediul social va fi adăugat automat după salvare.
                      </p>
                    )}
                  </div>
                </div>
              )}
              {anafError && (
                <div className="mt-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2">
                  <p className="text-xs text-red-600">{anafError}</p>
                </div>
              )}
            </div>

            {/* Denumire + Reg. Comerț */}
            <div className="grid grid-cols-2 gap-4">
              <div className="col-span-2">
                <label className={labelCls}>Denumire <span className="normal-case text-red-500">*</span></label>
                <Controller
                  control={control}
                  name="name"
                  render={({ field }) => (
                    <input {...field} value={field.value ?? ''} placeholder="ex: MedPharma SRL" className={inputCls} />
                  )}
                />
                {errors.name && <p className={errCls}>{errors.name.message}</p>}
              </div>
              <div>
                <label className={labelCls}>Reg. Comerțului</label>
                <Controller
                  control={control}
                  name="registrationNumber"
                  render={({ field }) => (
                    <input {...field} value={field.value ?? ''} placeholder="ex: J08/1234/2024" className={inputCls} />
                  )}
                />
              </div>
              <div>
                <label className={labelCls}>Formă juridică</label>
                <input {...register('legalForm')} placeholder="ex: SRL" className={inputCls} />
              </div>
            </div>

            {/* Contact */}
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className={labelCls}>Telefon</label>
                <input {...register('phone')} placeholder="ex: 0721 000 000" className={inputCls} />
              </div>
              <div>
                <label className={labelCls}>Email</label>
                <input {...register('email')} type="email" placeholder="ex: contact@firma.ro" className={inputCls} />
                {errors.email && <p className={errCls}>{errors.email.message}</p>}
              </div>
            </div>

            {/* Configurare flags */}
            <div className="rounded-lg border border-[#E5E7EB] bg-[#F8F9FB]">
              <p className="border-b border-[#E5E7EB] px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-[#4B5563]">Configurare</p>
              <div className="divide-y divide-[#E5E7EB] px-4">
                <label className="flex cursor-pointer items-center justify-between py-3">
                  <div>
                    <span className="text-sm font-medium text-[#111827]">Plătitor TVA</span>
                    <p className="text-xs text-[#9CA3AF]">Activat automat la verificarea ANAF</p>
                  </div>
                  <Controller
                    control={control}
                    name="isVatPayer"
                    render={({ field }) => (
                      <input
                        type="checkbox"
                        checked={field.value ?? false}
                        onChange={(e) => field.onChange(e.target.checked)}
                        className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]"
                      />
                    )}
                  />
                </label>
                <label className="flex cursor-pointer items-center justify-between py-3">
                  <div>
                    <span className="text-sm font-medium text-[#111827]">Activ</span>
                    <p className="text-xs text-[#9CA3AF]">Disponibil pentru selecție în documente</p>
                  </div>
                  <input
                    {...register('isActive')}
                    type="checkbox"
                    defaultChecked
                    className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]"
                  />
                </label>
              </div>
            </div>

            {createPartner.isError && (
              <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-600">
                A apărut o eroare la salvare. Verificați datele și reîncercați.
              </p>
            )}
          </div>
    </AppModal>
    </form>
  )
}

// ─── Left Panel ───────────────────────────────────────────────────────────────

function LeftPanel() {
  const { selectedId, selectPartner } = usePartnersUiStore()
  const [search, setSearch] = useState('')
  const [newOpen, setNewOpen] = useState(false)
  const { data, isLoading } = usePartnersList(search || undefined)
  const partners = data?.items ?? []

  return (
    <>
      <div style={{ width: 340, minWidth: 260, borderRight: `0.5px solid ${C.border}`, display: 'flex', flexDirection: 'column', height: '100%', flexShrink: 0 }}>
        {/* Header */}
        <div style={{ padding: '12px 14px', borderBottom: `0.5px solid ${C.border}`, display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
          <h2 style={{ fontSize: 14, fontWeight: 500, color: C.text, display: 'flex', alignItems: 'center', gap: 6 }}>
            <span className="ti ti-building" style={{ fontSize: 15, verticalAlign: -2 }} aria-hidden="true" />
            Parteneri
          </h2>
          <button
            type="button"
            onClick={() => setNewOpen(true)}
            style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 12, padding: '5px 12px', border: `0.5px solid ${C.border}`, borderRadius: 6, background: 'transparent', color: C.textSec, cursor: 'pointer' }}
          >
            <Plus size={13} />
            Nou
          </button>
        </div>

        {/* Search */}
        <div style={{ position: 'relative', padding: '8px 10px', borderBottom: `0.5px solid ${C.border}`, flexShrink: 0 }}>
          <span className="ti ti-search" style={{ position: 'absolute', left: 18, top: '50%', transform: 'translateY(-50%)', fontSize: 14, color: C.textMut }} aria-hidden="true" />
          <input
            type="text"
            placeholder="Caută cod, denumire, CUI…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{ width: '100%', fontSize: 13, padding: '6px 10px 6px 30px', borderRadius: 6, border: `0.5px solid ${C.border}`, background: C.bgSubtle, color: C.text, outline: 'none' }}
          />
        </div>

        {/* Table header */}
        <div style={{ flexShrink: 0 }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th style={{ width: 60, fontSize: 11, fontWeight: 500, color: C.textSec, textAlign: 'left', padding: '7px 10px', background: C.bgSubtle, borderBottom: `0.5px solid ${C.border}` }}>Cod</th>
                <th style={{ fontSize: 11, fontWeight: 500, color: C.textSec, textAlign: 'left', padding: '7px 10px', background: C.bgSubtle, borderBottom: `0.5px solid ${C.border}` }}>Denumire</th>
                <th style={{ width: 88, fontSize: 11, fontWeight: 500, color: C.textSec, textAlign: 'left', padding: '7px 10px', background: C.bgSubtle, borderBottom: `0.5px solid ${C.border}` }}>CUI</th>
              </tr>
            </thead>
          </table>
        </div>

        {/* Scrollable rows */}
        <div style={{ flex: 1, overflowY: 'auto' }}>
          {isLoading ? (
            <p style={{ padding: '12px 10px', fontSize: 12, color: C.textSec, textAlign: 'center' }}>Se încarcă…</p>
          ) : partners.length === 0 ? (
            <p style={{ padding: '12px 10px', fontSize: 12, color: C.textSec, textAlign: 'center' }}>Niciun partener găsit</p>
          ) : (
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <tbody>
                {partners.map((p) => {
                  const active = p.id === selectedId
                  return (
                    <tr
                      key={p.id}
                      onClick={() => selectPartner(p.id)}
                      style={{ cursor: 'pointer', background: active ? C.selected : undefined }}
                      onMouseEnter={(e) => { if (!active) (e.currentTarget as HTMLTableRowElement).style.background = C.bgSubtle }}
                      onMouseLeave={(e) => { (e.currentTarget as HTMLTableRowElement).style.background = active ? C.selected : '' }}
                    >
                      <td style={{ width: 60, fontSize: 11, fontFamily: 'monospace', padding: '8px 10px', color: C.textSec, borderBottom: `0.5px solid ${C.border}`, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.code}</td>
                      <td style={{ fontSize: 12, padding: '8px 10px', color: C.text, fontWeight: active ? 500 : undefined, borderBottom: `0.5px solid ${C.border}`, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.name}</td>
                      <td style={{ width: 88, fontSize: 11, fontFamily: 'monospace', padding: '8px 10px', color: C.textSec, borderBottom: `0.5px solid ${C.border}`, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.cui ?? ''}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {newOpen && <NewPartnerModal onClose={() => setNewOpen(false)} />}
    </>
  )
}

// ─── Right Panel ──────────────────────────────────────────────────────────────

function RightPanel() {
  const { selectedId } = usePartnersUiStore()

  if (!selectedId) {
    return (
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 8 }}>
        <span className="ti ti-building" style={{ fontSize: 36, color: C.textMut }} aria-hidden="true" />
        <p style={{ fontSize: 13, color: C.textSec }}>Selectați un partener din listă</p>
      </div>
    )
  }

  return <PartnerDetail partnerId={selectedId} />
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function ParteneriPage() {
  return (
    <div style={{ flex: '1 1 0', minHeight: 0, display: 'flex', overflow: 'hidden', border: `0.5px solid ${C.border}`, borderRadius: 8, background: C.bgMuted }}>
      <LeftPanel />
      <RightPanel />
    </div>
  )
}


