import { createFileRoute } from '@tanstack/react-router'
import ListePretPage from './-page'

export const Route = createFileRoute('/_auth/administration/liste-pret/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <ListePretPage />
}