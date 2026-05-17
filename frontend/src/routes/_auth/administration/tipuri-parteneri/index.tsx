import { createFileRoute } from '@tanstack/react-router'
import TipuriParteneriPage from './-page'

export const Route = createFileRoute('/_auth/administration/tipuri-parteneri/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <TipuriParteneriPage />
}
