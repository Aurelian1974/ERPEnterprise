import { createFileRoute, redirect } from '@tanstack/react-router'
import { useAuthStore } from '../store/auth.store'

export const Route = createFileRoute('/login')({
  beforeLoad() {
    const token = useAuthStore.getState().token
    if (token) throw redirect({ to: '/' })
  },
  component: RouteComponent,
})

function RouteComponent(): React.ReactElement {
  return (
    <div className="flex min-h-screen items-center justify-center bg-muted/40">
      <div className="w-full max-w-sm rounded-lg border bg-card p-8 shadow-sm">
        <h1 className="mb-6 text-2xl font-semibold tracking-tight">ERP Enterprise</h1>
        <p className="text-sm text-muted-foreground">Authentication coming soon.</p>
      </div>
    </div>
  )
}
