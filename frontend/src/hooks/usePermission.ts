import { useAuthStore } from '../store/auth.store'

export function usePermission(permission: string): boolean {
  const permissions = useAuthStore((s) => s.permissions)
  return permissions.includes(permission) || permissions.includes('*')
}
