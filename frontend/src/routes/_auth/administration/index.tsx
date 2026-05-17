import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/_auth/administration/')({
  component: RouteComponent,
})

function RouteComponent() {
  console.log('AdministrationPage rendered')
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold text-gray-900">Administrare</h1>
      <p className="text-sm text-gray-500">Selectează o secțiune din meniu.</p>
    </div>
  )
}
