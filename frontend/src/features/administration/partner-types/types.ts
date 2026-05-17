export interface PartnerTypeDto {
  partnerTypeId: number
  code: string
  name: string
  description?: string
  isSystem: boolean
  isActive: boolean
  affectsIssuedInvoices: boolean
  affectsReceivedInvoices: boolean
  sortOrder: number
  createdAt: string
  createdBy: string
  updatedAt: string
  updatedBy: string
}

export interface UpsertPartnerTypeRequest {
  partnerTypeId?: number
  code: string
  name: string
  description?: string
  isActive: boolean
  affectsIssuedInvoices: boolean
  affectsReceivedInvoices: boolean
  sortOrder: number
}
