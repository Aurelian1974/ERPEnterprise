import { createFileRoute } from '@tanstack/react-router'
import { useInvoice } from '../../../../features/finance/invoices/api'

export const Route = createFileRoute('/_auth/finance/invoices/$invoiceId')({
  component: RouteComponent,
})

function RouteComponent(): React.ReactElement {
  const { invoiceId } = Route.useParams()
  const { data, isLoading, isError } = useInvoice(invoiceId)

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Loading...</div>
  if (isError || !data) return <div className="p-6 text-sm text-destructive">Invoice not found.</div>

  return (
    <div className="p-6">
      <h1 className="text-2xl font-semibold tracking-tight">{data.invoiceNumber}</h1>
      <p className="mt-1 text-sm text-muted-foreground">{data.status} · {data.currency} {data.totalGross.toFixed(2)}</p>
    </div>
  )
}
