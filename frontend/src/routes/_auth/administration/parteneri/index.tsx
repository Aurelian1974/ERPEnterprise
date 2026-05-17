import { createFileRoute } from '@tanstack/react-router'
import ParteneriPage from './-page'

export const Route = createFileRoute('/_auth/administration/parteneri/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <ParteneriPage />
}