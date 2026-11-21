import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type RoleMatrix = components['schemas']['RoleMatrixDto'];
export type Role = components['schemas']['RoleDto'];
export type Permission = components['schemas']['PermissionDto'];
export type MatrixUser = components['schemas']['MatrixUserDto'];
export type PermissionOverride = components['schemas']['PermissionOverrideDto'];
export type PermissionOverrideInput = components['schemas']['PermissionOverrideInput'];
type SaveRole = components['schemas']['SaveRole'];
type Saved = components['schemas']['SavedEntity'];

export const roleKeys = {
  all: ['core', 'roles'] as const,
  matrix: (companyId: number | null) => ['core', 'roles', 'matrix', companyId ?? 'all'] as const,
};

/** Configurare › Roluri: catalogul, rolurile si, pentru firma aleasa (null = toate firmele), utilizatorii si exceptiile lor. */
export function useRoleMatrix(companyId: number | null) {
  return useQuery({
    queryKey: roleKeys.matrix(companyId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/core/roles/matrix', { params: { query: companyId === null ? {} : { CompanyId: companyId } }, signal })),
  });
}

export const roleApi = {
  async save(body: SaveRole): Promise<Saved> {
    return body.id === null
      ? unwrap(await api.POST('/api/core/roles', { body }))
      : unwrap(await api.PUT('/api/core/roles/{id}', { params: { path: { id: body.id } }, body }));
  },
  async remove(id: number, rowVersion: string): Promise<void> {
    unwrap(await api.POST('/api/core/roles/{id}/delete', { params: { path: { id } }, body: { rowVersion } }));
  },
  async saveOverrides(userId: number, companyId: number | null, overrides: PermissionOverrideInput[]): Promise<void> {
    unwrap(await api.PUT('/api/core/users/{id}/permission-overrides', { params: { path: { id: userId } }, body: { companyId, overrides } }));
  },
};
