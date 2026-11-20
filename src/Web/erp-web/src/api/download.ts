import { companyStore } from '../app/company';
import { ApiError, companyHeader } from './problem';

/** Numele fisierului din antetul Content-Disposition (forma RFC 5987 „filename*=UTF-8''…” are prioritate). */
export function fileNameFrom(header: string | null, fallback: string): string {
  if (header === null) {
    return fallback;
  }
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded?.[1] !== undefined) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      return fallback;
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain?.[1] ?? fallback;
}

/**
 * Descarca un fisier generat de server (Excel), pe aceeasi origine, cu firma curenta in antet. Erorile vin ca ProblemDetails,
 * la fel ca la celelalte apeluri (ApiError).
 */
export async function downloadFile(path: string, query: Record<string, string | number | boolean> = {}, fallbackName = 'export.xlsx'): Promise<void> {
  const params = new URLSearchParams(Object.entries(query).map(([key, value]) => [key, String(value)]));
  const url = params.size === 0 ? path : `${path}?${params.toString()}`;
  const companyId = companyStore.get();
  const response = await fetch(url, {
    credentials: 'same-origin',
    headers: companyId === undefined ? {} : { [companyHeader]: String(companyId) },
  });
  if (!response.ok) {
    throw new ApiError(response.status, await response.json().catch(() => undefined));
  }
  const blob = await response.blob();
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = fileNameFrom(response.headers.get('content-disposition'), fallbackName);
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(link.href);
}

/** Continutul unui fisier ales de utilizator, in Base64 (incarcarea importului). */
export async function fileToBase64(file: Blob): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  for (let i = 0; i < bytes.length; i += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(binary);
}
