import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '../../../lib/axios'
import type {
  AnafLookupDto,
  CreatePartnerRequest,
  PagedResult,
  PartnerDetailDto,
  PartnerListItemDto,
  UpdatePartnerRequest,
  UpsertAddressRequest,
  UpsertBankAccountRequest,
  UpsertContactRequest,
} from './types'

const BASE = '/administration/partners'

export const partnerKeys = {
  all: ['partners'] as const,
  list: (search?: string, page?: number, pageSize?: number) =>
    [...partnerKeys.all, 'list', { search, page, pageSize }] as const,
  detail: (id: string) => [...partnerKeys.all, 'detail', id] as const,
}

export function usePartnersList(search?: string, page = 1, pageSize = 50) {
  return useQuery({
    queryKey: partnerKeys.list(search, page, pageSize),
    queryFn: async () => {
      const { data } = await apiClient.get<PagedResult<PartnerListItemDto>>(BASE, {
        params: { search, page, pageSize },
      })
      return data
    },
    staleTime: 30_000,
  })
}

export function usePartner(id: string | null) {
  return useQuery({
    queryKey: partnerKeys.detail(id ?? ''),
    queryFn: async () => {
      const { data } = await apiClient.get<PartnerDetailDto>(`${BASE}/${id}`)
      return data
    },
    enabled: !!id,
  })
}

export function useCreatePartner() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: CreatePartnerRequest) => {
      const { data } = await apiClient.post<string>(BASE, request)
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.all })
    },
  })
}

export function useUpdatePartner(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: UpdatePartnerRequest) => {
      await apiClient.put(`${BASE}/${id}`, request)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.all })
    },
  })
}

export function useUpsertAddress(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: UpsertAddressRequest) => {
      await apiClient.post(`${BASE}/${partnerId}/addresses`, request)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) })
    },
  })
}

export function useDeleteAddress(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (addressId: number) => {
      await apiClient.delete(`${BASE}/${partnerId}/addresses/${addressId}`)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) })
    },
  })
}

export function useUpsertContact(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: UpsertContactRequest) => {
      await apiClient.post(`${BASE}/${partnerId}/contacts`, request)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) })
    },
  })
}

export function useDeleteContact(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (contactId: number) => {
      await apiClient.delete(`${BASE}/${partnerId}/contacts/${contactId}`)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) })
    },
  })
}

export function useUpsertBankAccount(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: UpsertBankAccountRequest) => {
      await apiClient.post(`${BASE}/${partnerId}/bank-accounts`, request)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) })
    },
  })
}

export function useDeleteBankAccount(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (bankAccountId: number) => {
      await apiClient.delete(`${BASE}/${partnerId}/bank-accounts/${bankAccountId}`)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) })
    },
  })
}
export function useVerifyAnaf(partnerId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      await apiClient.post(`${BASE}/${partnerId}/anaf-verify`)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: partnerKeys.detail(partnerId) })
    },
  })
}

export function useAnafLookup() {
  return useMutation({
    mutationFn: async (cui: string) => {
      const { data } = await apiClient.get<AnafLookupDto>(`${BASE}/anaf-lookup`, {
        params: { cui },
      })
      return data
    },
  })
}

export function useNextPartnerCode() {
  return useQuery({
    queryKey: [...partnerKeys.all, 'next-code'] as const,
    queryFn: async () => {
      const { data } = await apiClient.get<string>(`${BASE}/next-code`)
      return data
    },
    staleTime: 0,
  })
}
