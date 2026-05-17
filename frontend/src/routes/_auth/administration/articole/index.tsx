import { createFileRoute } from '@tanstack/react-router'
import ArticolePage from './-page'

export const Route = createFileRoute('/_auth/administration/articole/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <ArticolePage />
}