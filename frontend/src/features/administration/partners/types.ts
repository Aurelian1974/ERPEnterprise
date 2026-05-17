export interface PartnerListItemDto {
  id: string
  code: string
  name: string
  cui: string | null
  isActive: boolean
  totalCount: number
}

export interface PartnerAddressDto {
  id: number
  addressType: string
  street: string
  city: string
  county: string | null
  postalCode: string | null
  country: string
  isPrimary: boolean
}

export interface PartnerContactDto {
  id: number
  fullName: string
  position: string | null
  phone: string | null
  email: string | null
  isPrimary: boolean
}

export interface PartnerBankAccountDto {
  id: number
  iban: string
  bankName: string
  currency: string
  isDefault: boolean
}

export interface PartnerDetailDto {
  id: string
  code: string
  name: string
  cui: string | null
  registrationNumber: string | null
  legalForm: string | null
  partnerTypeId: number | null
  partnerTypeName: string | null
  isVatPayer: boolean
  phone: string | null
  email: string | null
  isActive: boolean
  notes: string | null
  createdAt: string
  updatedAt: string | null
  anafVerifiedAt: string | null
  addresses: PartnerAddressDto[]
  contacts: PartnerContactDto[]
  bankAccounts: PartnerBankAccountDto[]
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}

export interface CreatePartnerRequest {
  code: string
  name: string
  cui?: string | null
  registrationNumber?: string | null
  legalForm?: string | null
  partnerTypeId?: number | null
  isVatPayer: boolean
  phone?: string | null
  email?: string | null
  isActive: boolean
  notes?: string | null
  anafVerifiedAt?: string | null
}

export interface UpdatePartnerRequest {
  code: string
  name: string
  cui?: string | null
  registrationNumber?: string | null
  legalForm?: string | null
  partnerTypeId?: number | null
  isVatPayer: boolean
  phone?: string | null
  email?: string | null
  isActive: boolean
  notes?: string | null
}

export interface UpsertAddressRequest {
  id?: number | null
  addressType: string
  street: string
  city: string
  county?: string | null
  postalCode?: string | null
  country: string
  isPrimary: boolean
}

export interface UpsertContactRequest {
  id?: number | null
  fullName: string
  position?: string | null
  phone?: string | null
  email?: string | null
  isPrimary: boolean
}

export interface UpsertBankAccountRequest {
  id?: number | null
  iban: string
  bankName: string
  currency: string
  isDefault: boolean
}

export interface AnafAdresaSediuSocialDto {
  strada: string | null
  numar: string | null
  localitate: string | null
  judet: string | null
  codPostal: string | null
  tara: string | null
}

export interface AnafLookupDto {
  denumire: string
  isVatPayer: boolean
  nrRegCom: string | null
  stareInregistrare: string | null
  adresa: string | null
  telefon: string | null
  formaJuridica: string | null
  adresaSediuSocial: AnafAdresaSediuSocialDto | null
}
