import { createFileRoute } from '@tanstack/react-router'
import UtilizatoriPage from './-page'

export const Route = createFileRoute('/_auth/administration/utilizatori/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <UtilizatoriPage />
}