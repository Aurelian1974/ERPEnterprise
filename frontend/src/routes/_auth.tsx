import { createFileRoute, Outlet } from '@tanstack/react-router'
import AppShell from '../components/layout/AppShell'

export const Route = createFileRoute('/_auth')({
  // TODO: re-enable auth guard after authentication is implemented
  // beforeLoad({ location }) {
  //   const token = useAuthStore.getState().token
  //   if (!token) {
  //     throw redirect({ to: '/login', search: { redirect: location.href } })
  //   }
  // },
  component: RouteComponent,
})

function RouteComponent(): React.ReactElement {
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  )
}
