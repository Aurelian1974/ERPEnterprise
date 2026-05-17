import { createFileRoute } from '@tanstack/react-router'
import CatalogArticolePage from './-page'

export const Route = createFileRoute('/_auth/administration/catalog-articole/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <CatalogArticolePage />
}