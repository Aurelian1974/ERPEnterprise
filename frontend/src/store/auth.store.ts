import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  token: string | null
  userId: string | null
  email: string | null
  permissions: string[]
  setAuth: (token: string, userId: string, email: string, permissions: string[]) => void
  clearAuth: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      userId: null,
      email: null,
      permissions: [],
      setAuth: (token, userId, email, permissions) =>
        set({ token, userId, email, permissions }),
      clearAuth: () =>
        set({ token: null, userId: null, email: null, permissions: [] }),
    }),
    {
      name: 'erp-auth',
    },
  ),
)
