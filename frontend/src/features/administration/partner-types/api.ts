import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '../../../lib/axios'
import type { PartnerTypeDto, UpsertPartnerTypeRequest } from './types'

const BASE = '/administration/partner-types'

export const partnerTypeKeys = {
  all: ['partner-types'] as const,
  list: (includeInactive: boolean) => [...partnerTypeKeys.all, 'list', { includeInactive }] as const,
  detail: (id: number) => [...partnerTypeKeys.all, 'detail', id] as const,
}

export function usePartnerTypes(
  includeInactive = false,
): ReturnType<typeof useQuery<PartnerTypeDto[]>> {
  return useQuery({
    queryKey: partnerTypeKeys.list(includeInactive),
    queryFn: async () => {
      const { data } = await apiClient.get<PartnerTypeDto[]>(BASE, {
        params: { includeInactive },
      })
      return data
    },
    staleTime: 5 * 60 * 1000,
  })
}

export function usePartnerType(
  id: number,
): ReturnType<typeof useQuery<PartnerTypeDto>> {
  return useQuery({
    queryKey: partnerTypeKeys.detail(id),
    queryFn: async () => {
      const { data } = await apiClient.get<PartnerTypeDto>(`${BASE}/${id}`)
      return data
    },
    enabled: id > 0,
  })
}

export function useCreatePartnerType(): ReturnType<
  typeof useMutation<number, Error, UpsertPartnerTypeRequest>
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (request: UpsertPartnerTypeRequest) => {
      const { data } = await apiClient.post<number>(BASE, request)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: partnerTypeKeys.all })
    },
  })
}

export function useUpdatePartnerType(): ReturnType<
  typeof useMutation<void, Error, { id: number; request: UpsertPartnerTypeRequest }>
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, request }) => {
      await apiClient.put(`${BASE}/${id}`, request)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: partnerTypeKeys.all })
    },
  })
}

export function useDeletePartnerType(): ReturnType<typeof useMutation<void, Error, number>> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      await apiClient.delete(`${BASE}/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: partnerTypeKeys.all })
    },
  })
}
