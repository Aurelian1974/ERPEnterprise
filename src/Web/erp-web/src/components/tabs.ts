/**
 * Tastatura pentru o banda de tab-uri, dupa tiparul WAI-ARIA (UI-09): sagetile stanga/dreapta trec la tab-ul vecin,
 * circular, Home si End la primul si ultimul. Intoarce indexul tab-ului de activat sau undefined pentru alte taste.
 */
export function tabKeyTarget(index: number, key: string, count: number): number | undefined {
  if (count <= 0) {
    return undefined;
  }
  switch (key) {
    case 'ArrowRight':
      return (index + 1) % count;
    case 'ArrowLeft':
      return (index - 1 + count) % count;
    case 'Home':
      return 0;
    case 'End':
      return count - 1;
    default:
      return undefined;
  }
}
