import { createFileRoute } from '@tanstack/react-router'
import PersoancePage from './-page'

export const Route = createFileRoute('/_auth/administration/persoane/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <PersoancePage />
}