// These types are generated from the OpenAPI spec via:
// npx openapi-typescript http://localhost:5000/openapi/v1.json -o src/api/generated/api.ts
// Do NOT edit manually — run the generate-api script instead.

export type InvoiceStatus = 'Draft' | 'Submitted' | 'Approved' | 'Paid' | 'Cancelled'

export interface InvoiceListDto {
  id: string
  invoiceNumber: string
  customerId: string
  currency: string
  status: InvoiceStatus
  dueDate: string
  totalGross: number
  createdAtUtc: string
}

export interface InvoiceLineDto {
  id: string
  description: string
  quantity: number
  unitPrice: number
  vatRate: number
  netAmount: number
  vatAmount: number
  grossAmount: number
}

export interface InvoiceDetailDto extends InvoiceListDto {
  tenantId: string
  totalNet: number
  totalVat: number
  approvedAtUtc?: string
  paidAtUtc?: string
  lines: InvoiceLineDto[]
}

export interface InvoiceFilters {
  status?: InvoiceStatus
  customerId?: string
  dueDateFrom?: string
  dueDateTo?: string
  page?: number
  pageSize?: number
}

export interface CreateInvoiceLineRequest {
  description: string
  quantity: number
  unitPrice: number
  vatRate: number
}

export interface CreateInvoiceRequest {
  customerId: string
  currency: string
  dueDate: string
  lines: CreateInvoiceLineRequest[]
}
