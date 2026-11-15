// Firma curenta (selectorul din antet, 02.01 sectiunea 11): memorata in browser si trimisa in antetul X-Company-Id.
// Serverul o accepta doar daca utilizatorul are un rol pe ea; altfel alege el firma (raspunsul /api/core/me).
const storageKey = 'erp.companyId';
const listeners = new Set<() => void>();

function read(): number | undefined {
  const value = Number(localStorage.getItem(storageKey));
  return Number.isInteger(value) && value > 0 ? value : undefined;
}

let current = read();

export const companyStore = {
  get(): number | undefined {
    return current;
  },
  set(companyId: number | undefined): void {
    if (companyId === current) {
      return;
    }
    current = companyId;
    if (companyId === undefined) {
      localStorage.removeItem(storageKey);
    } else {
      localStorage.setItem(storageKey, String(companyId));
    }
    listeners.forEach((listener) => listener());
  },
  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
};
