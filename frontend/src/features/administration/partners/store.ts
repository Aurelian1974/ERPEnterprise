import { create } from 'zustand'

interface PartnersUiState {
  selectedId: string | null
  isEditing: boolean
  selectPartner: (id: string | null) => void
  startEditing: () => void
  cancelEditing: () => void
}

export const usePartnersUiStore = create<PartnersUiState>((set) => ({
  selectedId: null,
  isEditing: false,
  selectPartner: (id) => set({ selectedId: id, isEditing: false }),
  startEditing: () => set({ isEditing: true }),
  cancelEditing: () => set({ isEditing: false }),
}))
