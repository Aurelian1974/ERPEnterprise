import { createFileRoute } from '@tanstack/react-router'
import IerarhieParteneriPage from './-page'

export const Route = createFileRoute('/_auth/administration/ierarhie-parteneri/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <IerarhieParteneriPage />
}