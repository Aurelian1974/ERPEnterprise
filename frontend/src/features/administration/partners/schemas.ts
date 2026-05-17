import { z } from 'zod'

export const partnerSchema = z.object({
  code: z.string().min(1, 'Codul este obligatoriu').max(20, 'Codul poate avea maxim 20 caractere'),
  name: z.string().min(1, 'Denumirea este obligatorie').max(200),
  cui: z.string().max(20).nullable().optional(),
  registrationNumber: z.string().max(30).nullable().optional(),
  legalForm: z.string().max(50).nullable().optional(),
  partnerTypeId: z.number().int().positive().nullable().optional(),
  isVatPayer: z.boolean().default(false),
  phone: z.string().max(30).nullable().optional(),
  email: z.string().email('Email invalid').max(100).nullable().optional().or(z.literal('')),
  isActive: z.boolean().default(true),
  notes: z.string().max(2000).nullable().optional(),
})

export type PartnerFormValues = z.infer<typeof partnerSchema>

export const addressSchema = z.object({
  id: z.number().nullable().optional(),
  addressType: z.string().min(1, 'Tipul adresei este obligatoriu').max(50),
  street: z.string().min(1, 'Strada este obligatorie').max(200),
  city: z.string().min(1, 'Localitatea este obligatorie').max(100),
  county: z.string().max(50).nullable().optional(),
  postalCode: z.string().max(10).nullable().optional(),
  country: z.string().min(1).max(50).default('România'),
  isPrimary: z.boolean().default(false),
})

export type AddressFormValues = z.infer<typeof addressSchema>

export const contactSchema = z.object({
  id: z.number().nullable().optional(),
  fullName: z.string().min(1, 'Numele este obligatoriu').max(100),
  position: z.string().max(100).nullable().optional(),
  phone: z.string().max(30).nullable().optional(),
  email: z.string().email('Email invalid').max(100).nullable().optional().or(z.literal('')),
  isPrimary: z.boolean().default(false),
})

export type ContactFormValues = z.infer<typeof contactSchema>

export const bankAccountSchema = z.object({
  id: z.number().nullable().optional(),
  iban: z.string().min(1, 'IBAN-ul este obligatoriu').max(34),
  bankName: z.string().min(1, 'Banca este obligatorie').max(100),
  currency: z.string().length(3).default('RON'),
  isDefault: z.boolean().default(false),
})

export type BankAccountFormValues = z.infer<typeof bankAccountSchema>
