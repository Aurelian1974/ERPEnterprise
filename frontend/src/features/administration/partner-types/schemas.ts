import { z } from 'zod'

export const upsertPartnerTypeSchema = z.object({
  code: z
    .string()
    .min(1, 'Codul este obligatoriu')
    .max(50, 'Codul poate avea maxim 50 de caractere')
    .regex(/^[A-Z0-9_]+$/, 'Doar litere mari, cifre și underscore'),
  name: z
    .string()
    .min(1, 'Denumirea este obligatorie')
    .max(100, 'Denumirea poate avea maxim 100 de caractere'),
  description: z
    .string()
    .max(500, 'Descrierea poate avea maxim 500 de caractere')
    .optional(),
  isActive: z.boolean(),
  affectsIssuedInvoices: z.boolean(),
  affectsReceivedInvoices: z.boolean(),
  sortOrder: z.number().int().min(0).max(9999),
})

export type UpsertPartnerTypeFormValues = z.infer<typeof upsertPartnerTypeSchema>
