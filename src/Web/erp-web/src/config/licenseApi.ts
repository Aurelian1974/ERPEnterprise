import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { unwrap } from '../api/problem';
import type { components } from '../api/schema';

export type License = components['schemas']['LicenseDto'];
export type LicenseHistoryItem = components['schemas']['LicenseHistoryItem'];

export const licenseKeys = { current: ['core', 'license'] as const };

/** Licenta instalarii si istoricul (08, 4.1); permisiunea Core.License.Manage. */
export function useLicense() {
  return useQuery({ queryKey: licenseKeys.current, queryFn: async ({ signal }) => unwrap(await api.GET('/api/core/license', { signal })) });
}

/** Continutul fisierului .lic in Base64, cum il asteapta API-ul. */
export async function fileToBase64(file: Blob): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary);
}

export const licenseApi = {
  upload: async (file: Blob) => unwrap(await api.POST('/api/core/license', { body: { fileBase64: await fileToBase64(file) } })),
};
