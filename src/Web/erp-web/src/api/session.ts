import { useQuery } from '@tanstack/react-query';
import { api } from './client';
import type { components } from './schema';
import { companyStore } from '../app/company';
import { unwrap } from './problem';

export type CurrentUser = components['schemas']['CurrentUserDto'];

/** Utilizatorul curent: permisiuni, module active, starea licentei, firmele si firma curenta (antetul si meniul, UI-07). */
export function useCurrentUser() {
  return useQuery({
    queryKey: ['core', 'me'],
    queryFn: async ({ signal }): Promise<CurrentUser> => {
      const user = unwrap(await api.GET('/api/core/me', { signal }));
      // Serverul accepta firma ceruta doar daca utilizatorul are rol pe ea; firma efectiva devine firma curenta.
      companyStore.set(user.companyId > 0 ? user.companyId : undefined);
      return user;
    },
    staleTime: 5 * 60_000,
    retry: false,
  });
}
