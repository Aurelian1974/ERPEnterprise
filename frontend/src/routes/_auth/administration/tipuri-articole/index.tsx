import { createFileRoute } from '@tanstack/react-router'
import TipuriArticolePage from './-page'

export const Route = createFileRoute('/_auth/administration/tipuri-articole/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <TipuriArticolePage />
}