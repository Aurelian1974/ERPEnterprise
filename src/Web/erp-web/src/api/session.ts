import { useQuery } from '@tanstack/react-query';
import { api } from './client';
import type { components } from './schema';

export type CurrentUser = components['schemas']['CurrentUserDto'];

/** Utilizatorul curent: permisiuni, module active, starea licentei (antetul si meniul, UI-07). */
export function useCurrentUser() {
  return useQuery({
    queryKey: ['core', 'me'],
    queryFn: async ({ signal }): Promise<CurrentUser> => {
      const { data, response } = await api.GET('/api/core/me', { signal });
      if (data === undefined) {
        throw new Error(`GET /api/core/me: ${response.status}`);
      }
      return data;
    },
    staleTime: 5 * 60_000,
    retry: false,
  });
}
