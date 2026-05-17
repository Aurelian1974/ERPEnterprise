import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '../../../lib/axios'
import type { InvoiceDetailDto, InvoiceListDto, InvoiceFilters, CreateInvoiceRequest } from './types'

export const invoiceKeys = {
  all: ['invoices'] as const,
  list: (f: InvoiceFilters) => [...invoiceKeys.all, 'list', f] as const,
  detail: (id: string) => [...invoiceKeys.all, 'detail', id] as const,
}

export function useInvoices(filters: InvoiceFilters): ReturnType<typeof useQuery<InvoiceListDto[]>> {
  return useQuery({
    queryKey: invoiceKeys.list(filters),
    queryFn: async () => {
      const { data } = await apiClient.get<InvoiceListDto[]>('/finance/invoices', {
        params: filters,
      })
      return data
    },
  })
}

export function useInvoice(id: string): ReturnType<typeof useQuery<InvoiceDetailDto>> {
  return useQuery({
    queryKey: invoiceKeys.detail(id),
    queryFn: async () => {
      const { data } = await apiClient.get<InvoiceDetailDto>(`/finance/invoices/${id}`)
      return data
    },
    enabled: Boolean(id),
  })
}

export function useCreateInvoice(): ReturnType<typeof useMutation<string, Error, CreateInvoiceRequest>> {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (request: CreateInvoiceRequest) => {
      const { data } = await apiClient.post<string>('/finance/invoices', request)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: invoiceKeys.all })
    },
  })
}

export function useApproveInvoice(): ReturnType<typeof useMutation<void, Error, string>> {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.post(`/finance/invoices/${id}/approve`)
    },
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: invoiceKeys.detail(id) })
      queryClient.invalidateQueries({ queryKey: invoiceKeys.all })
    },
  })
}
