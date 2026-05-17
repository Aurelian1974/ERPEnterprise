import { z } from 'zod'

export const createInvoiceLineSchema = z.object({
  description: z.string().min(1, 'Description is required').max(500),
  quantity: z.number().positive('Quantity must be positive'),
  unitPrice: z.number().min(0, 'Unit price cannot be negative'),
  vatRate: z.number().min(0).max(100),
})

export const createInvoiceSchema = z.object({
  customerId: z.string().uuid('Invalid customer ID'),
  currency: z
    .string()
    .length(3, 'Currency must be 3 letters')
    .regex(/^[A-Z]{3}$/, 'Currency must be uppercase (e.g. RON, EUR)'),
  dueDate: z.string().min(1, 'Due date is required'),
  lines: z
    .array(createInvoiceLineSchema)
    .min(1, 'At least one line is required'),
})

export type CreateInvoiceFormValues = z.infer<typeof createInvoiceSchema>
