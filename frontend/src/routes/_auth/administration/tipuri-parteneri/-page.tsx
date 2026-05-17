import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Plus, Pencil, Trash2, CheckCircle2, XCircle, Shield } from 'lucide-react'
import { AppModal, AppModalFooter } from '../../../../components/ui/AppModal'
import {
  usePartnerTypes,
  useCreatePartnerType,
  useUpdatePartnerType,
  useDeletePartnerType,
} from '../../../../features/administration/partner-types/api'
import type { PartnerTypeDto } from '../../../../features/administration/partner-types/types'
import type { UpsertPartnerTypeFormValues } from '../../../../features/administration/partner-types/schemas'
import { upsertPartnerTypeSchema } from '../../../../features/administration/partner-types/schemas'

// ─── PartnerTypeFormModal ─────────────────────────────────────────────────────

interface PartnerTypeFormModalProps {
  partnerType: PartnerTypeDto | null
  onClose: () => void
}

function PartnerTypeFormModal({ partnerType, onClose }: PartnerTypeFormModalProps) {
  const isEdit = partnerType !== null
  const isSystem = partnerType?.isSystem ?? false
  const createMutation = useCreatePartnerType()
  const updateMutation = useUpdatePartnerType()
  const isPending = createMutation.isPending || updateMutation.isPending

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<UpsertPartnerTypeFormValues>({
    defaultValues: {
      code: partnerType?.code ?? '',
      name: partnerType?.name ?? '',
      description: partnerType?.description ?? '',
      isActive: partnerType?.isActive ?? true,
      affectsIssuedInvoices: partnerType?.affectsIssuedInvoices ?? false,
      affectsReceivedInvoices: partnerType?.affectsReceivedInvoices ?? false,
      sortOrder: partnerType?.sortOrder ?? 0,
    },
    resolver: async (values) => {
      const result = upsertPartnerTypeSchema.safeParse(values)
      if (result.success) return { values: result.data, errors: {} }
      return {
        values: {},
        errors: Object.fromEntries(
          Object.entries(result.error.flatten().fieldErrors).map(([key, msgs]) => [
            key,
            { type: 'validate', message: msgs?.[0] ?? 'Invalid' },
          ]),
        ),
      }
    },
  })

  const onSubmit = async (values: UpsertPartnerTypeFormValues) => {
    if (isEdit && partnerType) {
      await updateMutation.mutateAsync({ id: partnerType.partnerTypeId, request: values })
    } else {
      await createMutation.mutateAsync(values)
    }
    onClose()
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
    <AppModal
      title={isEdit ? 'Editare tip partener' : 'Tip partener nou'}
      subtitle={isEdit ? partnerType.name : undefined}
      icon={isEdit ? <Pencil size={14} className="text-[#1E88D0]" /> : <Plus size={15} className="text-[#1E88D0]" />}
      size="lg"
      onClose={onClose}
      footer={
        <AppModalFooter
          onClose={onClose}
          pending={isPending}
          submitLabel={isEdit ? 'Salvează modificările' : 'Creează tip'}
          subtle
        />
      }
    >
      <div className="space-y-5 px-6 py-5">
            {/* Cod + Denumire */}
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="mb-1.5 block text-xs font-medium uppercase tracking-wide text-[#4B5563]">
                  Cod <span className="normal-case text-red-500">*</span>
                </label>
                <input
                  {...register('code')}
                  type="text"
                  disabled={isSystem}
                  placeholder="ex: CLIENT"
                  className="w-full rounded-lg border border-[#E5E7EB] bg-white px-3 py-2.5 text-sm text-[#111827] placeholder:text-[#9CA3AF] transition-colors focus:border-[#1E88D0] focus:outline-none focus:ring-2 focus:ring-[#1E88D0]/20 disabled:bg-[#F8F9FB] disabled:text-[#9CA3AF]"
                />
                {errors.code && (
                  <p className="mt-1 text-xs text-red-600">{errors.code.message}</p>
                )}
              </div>
              <div>
                <label className="mb-1.5 block text-xs font-medium uppercase tracking-wide text-[#4B5563]">
                  Denumire <span className="normal-case text-red-500">*</span>
                </label>
                <input
                  {...register('name')}
                  type="text"
                  placeholder="ex: Client"
                  className="w-full rounded-lg border border-[#E5E7EB] bg-white px-3 py-2.5 text-sm text-[#111827] placeholder:text-[#9CA3AF] transition-colors focus:border-[#1E88D0] focus:outline-none focus:ring-2 focus:ring-[#1E88D0]/20"
                />
                {errors.name && (
                  <p className="mt-1 text-xs text-red-600">{errors.name.message}</p>
                )}
              </div>
            </div>

            {/* Descriere */}
            <div>
              <label className="mb-1.5 block text-xs font-medium uppercase tracking-wide text-[#4B5563]">Descriere</label>
              <textarea
                {...register('description')}
                rows={2}
                placeholder="Descriere opțională..."
                className="w-full resize-none rounded-lg border border-[#E5E7EB] bg-white px-3 py-2.5 text-sm text-[#111827] placeholder:text-[#9CA3AF] transition-colors focus:border-[#1E88D0] focus:outline-none focus:ring-2 focus:ring-[#1E88D0]/20"
              />
              {errors.description && (
                <p className="mt-1 text-xs text-red-600">{errors.description.message}</p>
              )}
            </div>

            {/* Ordine sortare */}
            <div>
              <label className="mb-1.5 block text-xs font-medium uppercase tracking-wide text-[#4B5563]">Ordine sortare</label>
              <input
                {...register('sortOrder', { valueAsNumber: true })}
                type="number"
                min={0}
                max={9999}
                className="w-28 rounded-lg border border-[#E5E7EB] bg-white px-3 py-2.5 text-sm text-[#111827] transition-colors focus:border-[#1E88D0] focus:outline-none focus:ring-2 focus:ring-[#1E88D0]/20"
              />
              {errors.sortOrder && (
                <p className="mt-1 text-xs text-red-600">{errors.sortOrder.message}</p>
              )}
            </div>

            {/* Configurare flags */}
            <div className="rounded-lg border border-[#E5E7EB] bg-[#F8F9FB]">
              <p className="border-b border-[#E5E7EB] px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-[#4B5563]">Configurare</p>
              <div className="divide-y divide-[#E5E7EB] px-4">
                <label className="flex cursor-pointer items-center justify-between py-3">
                  <div>
                    <span className="text-sm font-medium text-[#111827]">Activ</span>
                    <p className="text-xs text-[#9CA3AF]">Disponibil pentru selecție</p>
                  </div>
                  <input
                    {...register('isActive')}
                    type="checkbox"
                    className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]"
                  />
                </label>
                <label className={`flex cursor-pointer items-center justify-between py-3 ${isSystem ? 'opacity-50' : ''}`}>
                  <div>
                    <span className="text-sm font-medium text-[#111827]">Client</span>
                    <p className="text-xs text-[#9CA3AF]">Afectează facturile emise</p>
                  </div>
                  <input
                    {...register('affectsIssuedInvoices')}
                    type="checkbox"
                    disabled={isSystem}
                    className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]"
                  />
                </label>
                <label className={`flex cursor-pointer items-center justify-between py-3 ${isSystem ? 'opacity-50' : ''}`}>
                  <div>
                    <span className="text-sm font-medium text-[#111827]">Furnizor</span>
                    <p className="text-xs text-[#9CA3AF]">Afectează facturile primite</p>
                  </div>
                  <input
                    {...register('affectsReceivedInvoices')}
                    type="checkbox"
                    disabled={isSystem}
                    className="h-4 w-4 rounded border-gray-300 accent-[#1E88D0]"
                  />
                </label>
              </div>
            </div>

            {isSystem && (
              <div className="flex items-start gap-2.5 rounded-lg border border-[#FDE68A] bg-[#FFFBEB] px-3.5 py-3">
                <Shield size={14} className="mt-0.5 shrink-0 text-[#F59E0B]" />
                <p className="text-xs text-[#92400E]">
                  Tip sistem — codul și flagurile de facturare nu pot fi modificate.
                </p>
              </div>
            )}
          </div>
      </AppModal>
    </form>
  )
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export default function TipuriParteneriPage() {
  const [includeInactive, setIncludeInactive] = useState(false)
  const [modalOpen, setModalOpen] = useState(false)
  const [editTarget, setEditTarget] = useState<PartnerTypeDto | null>(null)

  const { data: types = [], isLoading, isError } = usePartnerTypes(includeInactive)
  const deleteMutation = useDeletePartnerType()

  const openCreate = () => {
    setEditTarget(null)
    setModalOpen(true)
  }

  const openEdit = (pt: PartnerTypeDto) => {
    setEditTarget(pt)
    setModalOpen(true)
  }

  const handleDelete = (pt: PartnerTypeDto) => {
    if (pt.isSystem) return
    if (!window.confirm(`Ștergeți tipul "${pt.name}"?`)) return
    deleteMutation.mutate(pt.partnerTypeId)
  }

  return (
    <div className="flex flex-col gap-6 p-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-gray-900">Tipuri Parteneri</h1>
          <p className="mt-0.5 text-sm text-gray-500">Gestionare tipuri de parteneri.</p>
        </div>
        <div className="flex items-center gap-3">
          <label className="flex cursor-pointer items-center gap-2 text-sm text-gray-600">
            <input
              type="checkbox"
              checked={includeInactive}
              onChange={(e) => setIncludeInactive(e.target.checked)}
              className="h-4 w-4 rounded"
            />
            Afișează inactive
          </label>
          <button
            type="button"
            onClick={openCreate}
            className="flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            <Plus size={15} />
            Adaugă tip
          </button>
        </div>
      </div>

      {/* Table */}
      {isLoading ? (
        <div className="py-12 text-center text-sm text-gray-500">Se încarcă...</div>
      ) : isError ? (
        <div className="py-12 text-center text-sm text-red-600">
          Eroare la încărcarea datelor.
        </div>
      ) : types.length === 0 ? (
        <div className="py-12 text-center text-sm text-gray-500">
          Nu există tipuri de parteneri.
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-gray-200 bg-white">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Cod</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Denumire</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Descriere</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Sistem</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Activ</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Client</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Furnizor</th>
                <th className="px-4 py-3 text-center font-medium text-gray-600">Ordine</th>
                <th className="px-4 py-3 text-right font-medium text-gray-600">Acțiuni</th>
              </tr>
            </thead>
            <tbody>
              {types.map((pt) => (
                <tr key={pt.partnerTypeId} className="odd:bg-white even:bg-[#F8F9FB] hover:bg-[#EBF5FF] transition-colors">
                  <td className="px-4 py-3 font-mono text-xs font-medium text-gray-800">
                    {pt.code}
                  </td>
                  <td className="px-4 py-3 font-medium text-gray-900">{pt.name}</td>
                  <td className="max-w-xs truncate px-4 py-3 text-gray-500">
                    {pt.description ?? '—'}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {pt.isSystem ? (
                      <Shield size={15} className="mx-auto text-amber-500" />
                    ) : (
                      <span className="text-gray-300">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {pt.isActive ? (
                      <CheckCircle2 size={15} className="mx-auto text-green-500" />
                    ) : (
                      <XCircle size={15} className="mx-auto text-gray-400" />
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {pt.affectsIssuedInvoices ? (
                      <CheckCircle2 size={15} className="mx-auto text-green-500" />
                    ) : (
                      <span className="text-gray-300">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {pt.affectsReceivedInvoices ? (
                      <CheckCircle2 size={15} className="mx-auto text-green-500" />
                    ) : (
                      <span className="text-gray-300">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center text-gray-600">{pt.sortOrder}</td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-1">
                      <button
                        type="button"
                        onClick={() => openEdit(pt)}
                        className="rounded p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600"
                        title="Editează"
                      >
                        <Pencil size={14} />
                      </button>
                      {!pt.isSystem && (
                        <button
                          type="button"
                          onClick={() => handleDelete(pt)}
                          disabled={deleteMutation.isPending}
                          className="rounded p-1.5 text-gray-400 hover:bg-red-50 hover:text-red-600 disabled:opacity-50"
                          title="Șterge"
                        >
                          <Trash2 size={14} />
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Modal */}
      {modalOpen && (
        <PartnerTypeFormModal
          partnerType={editTarget}
          onClose={() => setModalOpen(false)}
        />
      )}
    </div>
  )
}
