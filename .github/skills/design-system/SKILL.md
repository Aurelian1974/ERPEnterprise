---
name: design-system
description: >-
  Paleta de culori și design tokens pentru ERP. Primary light blue #1E88D0,
  light mode default, culoare funcțională nu decorativă. Tailwind config,
  CSS variables, token-uri pentru status, suprafețe, text, borduri.
  Folosit la generare componente React, clase Tailwind, CSS, teme shadcn/ui.
---

# Design System — ERP Color Palette

## Filosofie

```
Culoarea e funcțională, nu decorativă.
Utilizatorul petrece 8h/zi în interfață — oboselă vizuală = productivitate scăzută.
Culorile vii sunt rezervate exclusiv pentru semantică: status, alerte, acțiuni critice.
Light mode default — dark mode opțional.
Griurile domină suprafețele — culoarea iese în evidență doar unde contează.
```

---

## Paleta completă

### Primary — Light Blue

| Token | Hex | Utilizare |
|---|---|---|
| `primary-50` | `#EBF5FF` | Background hover subtil, badge outline |
| `primary-100` | `#C5E2FA` | Background selected row, chip background |
| `primary-200` | `#8EC8F5` | Border focus ring, divider activ |
| `primary-300` | `#4AAAE8` | Icon secondary, link hover |
| `primary-400` | `#2596D9` | Link default, icon primary |
| `primary-500` | `#1E88D0` | **Primary — butoane principale, navbar, accente** |
| `primary-600` | `#1670B0` | Button hover, link visited |
| `primary-700` | `#0E5A8A` | Button active/pressed |
| `primary-800` | `#094A72` | Text pe fundal primary-100 |
| `primary-900` | `#063550` | Text foarte închis, rar folosit |

### Suprafețe (Surfaces)

| Token | Hex | Utilizare |
|---|---|---|
| `surface-base` | `#FFFFFF` | Fundal pagină principal |
| `surface-subtle` | `#F8F9FB` | Fundal sidebar, panel secundar |
| `surface-muted` | `#F1F3F6` | Card, modal background |
| `surface-emphasis` | `#E4E8EE` | Divider vizibil, header tabel |
| `surface-overlay` | `rgba(0,0,0,0.04)` | Row hover în tabel |
| `surface-inverse` | `#1A2B3C` | Navbar dark, tooltip |

### Text

| Token | Hex | Utilizare |
|---|---|---|
| `text-primary` | `#111827` | Text principal, headings |
| `text-secondary` | `#4B5563` | Label, text ajutător |
| `text-muted` | `#9CA3AF` | Placeholder, text dezactivat |
| `text-inverse` | `#FFFFFF` | Text pe fundal dark/primary |
| `text-link` | `#1E88D0` | Link default (= primary-500) |
| `text-link-hover` | `#1670B0` | Link hover (= primary-600) |

### Borduri

| Token | Hex | Utilizare |
|---|---|---|
| `border-default` | `#E5E7EB` | Border card, input default |
| `border-subtle` | `#F3F4F6` | Separatoare subtile între rânduri tabel |
| `border-strong` | `#D1D5DB` | Border input focus-ready, modal |
| `border-focus` | `#1E88D0` | Focus ring input (= primary-500) |

### Semantice — Status

| Token | Hex | Utilizare |
|---|---|---|
| `success-bg` | `#F0FDF4` | Background badge success |
| `success-border` | `#BBF7D0` | Border badge success |
| `success-text` | `#166534` | Text badge success |
| `success-icon` | `#22C55E` | Iconiță check, progres complet |
| `warning-bg` | `#FFFBEB` | Background badge warning |
| `warning-border` | `#FDE68A` | Border badge warning |
| `warning-text` | `#92400E` | Text badge warning |
| `warning-icon` | `#F59E0B` | Iconiță warning, deadline aproape |
| `danger-bg` | `#FFF1F2` | Background badge danger/error |
| `danger-border` | `#FECDD3` | Border badge danger |
| `danger-text` | `#9F1239` | Text badge danger |
| `danger-icon` | `#EF4444` | Iconiță eroare, acțiune distructivă |
| `info-bg` | `#EFF6FF` | Background badge info |
| `info-border` | `#BFDBFE` | Border badge info |
| `info-text` | `#1E40AF` | Text badge info |
| `info-icon` | `#3B82F6` | Iconiță informație |

### Status ERP specific (pentru badge-uri și coloane de status)

| Status | Background | Text | Border |
|---|---|---|---|
| Draft | `#F3F4F6` | `#374151` | `#D1D5DB` |
| În procesare | `#EFF6FF` | `#1E40AF` | `#BFDBFE` |
| Aprobat | `#F0FDF4` | `#166534` | `#BBF7D0` |
| Respins | `#FFF1F2` | `#9F1239` | `#FECDD3` |
| Anulat | `#F9FAFB` | `#6B7280` | `#E5E7EB` |
| Finalizat | `#F0FDF4` | `#166534` | `#BBF7D0` |
| Întârziat | `#FFFBEB` | `#92400E` | `#FDE68A` |
| Blocat | `#FFF1F2` | `#9F1239` | `#FECDD3` |

---

## Implementare

### CSS Variables (`:root`)

```css
:root {
  /* Primary */
  --color-primary-50:  #EBF5FF;
  --color-primary-100: #C5E2FA;
  --color-primary-200: #8EC8F5;
  --color-primary-300: #4AAAE8;
  --color-primary-400: #2596D9;
  --color-primary-500: #1E88D0;
  --color-primary-600: #1670B0;
  --color-primary-700: #0E5A8A;
  --color-primary-800: #094A72;
  --color-primary-900: #063550;

  /* Surfaces */
  --color-surface-base:     #FFFFFF;
  --color-surface-subtle:   #F8F9FB;
  --color-surface-muted:    #F1F3F6;
  --color-surface-emphasis: #E4E8EE;
  --color-surface-inverse:  #1A2B3C;

  /* Text */
  --color-text-primary:   #111827;
  --color-text-secondary: #4B5563;
  --color-text-muted:     #9CA3AF;
  --color-text-inverse:   #FFFFFF;
  --color-text-link:      #1E88D0;

  /* Borders */
  --color-border-default: #E5E7EB;
  --color-border-subtle:  #F3F4F6;
  --color-border-strong:  #D1D5DB;
  --color-border-focus:   #1E88D0;

  /* Semantic */
  --color-success-bg:     #F0FDF4;
  --color-success-border: #BBF7D0;
  --color-success-text:   #166534;
  --color-success-icon:   #22C55E;

  --color-warning-bg:     #FFFBEB;
  --color-warning-border: #FDE68A;
  --color-warning-text:   #92400E;
  --color-warning-icon:   #F59E0B;

  --color-danger-bg:      #FFF1F2;
  --color-danger-border:  #FECDD3;
  --color-danger-text:    #9F1239;
  --color-danger-icon:    #EF4444;

  --color-info-bg:        #EFF6FF;
  --color-info-border:    #BFDBFE;
  --color-info-text:      #1E40AF;
  --color-info-icon:      #3B82F6;
}
```

### Tailwind Config (`tailwind.config.ts`)

```typescript
import type { Config } from 'tailwindcss';

export default {
  content: ['./src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        primary: {
          50:  '#EBF5FF',
          100: '#C5E2FA',
          200: '#8EC8F5',
          300: '#4AAAE8',
          400: '#2596D9',
          500: '#1E88D0',   // DEFAULT
          600: '#1670B0',
          700: '#0E5A8A',
          800: '#094A72',
          900: '#063550',
          DEFAULT: '#1E88D0',
        },
        surface: {
          base:     '#FFFFFF',
          subtle:   '#F8F9FB',
          muted:    '#F1F3F6',
          emphasis: '#E4E8EE',
          inverse:  '#1A2B3C',
        },
        'text-primary':   '#111827',
        'text-secondary': '#4B5563',
        'text-muted':     '#9CA3AF',
        'text-inverse':   '#FFFFFF',
        success: {
          bg:     '#F0FDF4',
          border: '#BBF7D0',
          text:   '#166534',
          icon:   '#22C55E',
        },
        warning: {
          bg:     '#FFFBEB',
          border: '#FDE68A',
          text:   '#92400E',
          icon:   '#F59E0B',
        },
        danger: {
          bg:     '#FFF1F2',
          border: '#FECDD3',
          text:   '#9F1239',
          icon:   '#EF4444',
        },
        info: {
          bg:     '#EFF6FF',
          border: '#BFDBFE',
          text:   '#1E40AF',
          icon:   '#3B82F6',
        },
      },
      borderRadius: {
        sm:  '4px',
        md:  '6px',
        lg:  '8px',
        xl:  '12px',
      },
      fontSize: {
        xs:   ['11px', { lineHeight: '16px' }],
        sm:   ['12px', { lineHeight: '18px' }],
        base: ['14px', { lineHeight: '20px' }],   // ERP — text mai mic, densitate mai mare
        lg:   ['16px', { lineHeight: '24px' }],
        xl:   ['18px', { lineHeight: '28px' }],
        '2xl':['22px', { lineHeight: '32px' }],
      },
      spacing: {
        // Grid de 4px — consistent cu Tailwind default
      },
      boxShadow: {
        sm:  '0 1px 2px 0 rgba(0,0,0,0.05)',
        md:  '0 2px 8px 0 rgba(0,0,0,0.08)',
        lg:  '0 4px 16px 0 rgba(0,0,0,0.10)',
        // Fără umbre dramatice în ERP
      },
    },
  },
  plugins: [],
} satisfies Config;
```

### shadcn/ui Theme Override (`src/styles/globals.css`)

```css
@layer base {
  :root {
    --background:       0 0% 100%;           /* #FFFFFF */
    --foreground:       220 13% 13%;         /* #111827 */

    --card:             220 20% 97%;         /* #F8F9FB */
    --card-foreground:  220 13% 13%;

    --popover:          0 0% 100%;
    --popover-foreground: 220 13% 13%;

    --primary:          204 74% 46%;         /* #1E88D0 */
    --primary-foreground: 0 0% 100%;

    --secondary:        214 32% 91%;         /* #E4E8EE */
    --secondary-foreground: 220 13% 13%;

    --muted:            216 20% 95%;         /* #F1F3F6 */
    --muted-foreground: 220 9% 46%;          /* #6B7280 */

    --accent:           204 74% 46%;
    --accent-foreground: 0 0% 100%;

    --destructive:      355 100% 65%;        /* #EF4444 */
    --destructive-foreground: 0 0% 100%;

    --border:           220 13% 91%;         /* #E5E7EB */
    --input:            220 13% 91%;
    --ring:             204 74% 46%;         /* #1E88D0 — focus ring */

    --radius: 0.375rem;                      /* 6px — conservator pentru ERP */
  }
}
```

---

## Componente — clase Tailwind de referință

### Buton principal
```tsx
<button className="bg-primary-500 hover:bg-primary-600 active:bg-primary-700
                   text-white text-sm font-medium
                   px-4 py-2 rounded-md
                   transition-colors duration-150
                   focus-visible:outline-none focus-visible:ring-2
                   focus-visible:ring-primary-500 focus-visible:ring-offset-2
                   disabled:opacity-50 disabled:cursor-not-allowed">
  Salvează
</button>
```

### Buton secundar
```tsx
<button className="bg-white hover:bg-surface-subtle
                   text-text-primary text-sm font-medium
                   border border-border-default hover:border-border-strong
                   px-4 py-2 rounded-md transition-colors duration-150">
  Anulează
</button>
```

### Badge status
```tsx
// Draft
<span className="bg-[#F3F4F6] text-[#374151] border border-[#D1D5DB]
                 text-xs font-medium px-2 py-0.5 rounded-full">
  Draft
</span>

// Aprobat
<span className="bg-success-bg text-success-text border border-success-border
                 text-xs font-medium px-2 py-0.5 rounded-full">
  Aprobat
</span>

// Întârziat
<span className="bg-warning-bg text-warning-text border border-warning-border
                 text-xs font-medium px-2 py-0.5 rounded-full">
  Întârziat
</span>
```

### Rând tabel alternant
```tsx
<tr className="border-b border-border-subtle
               hover:bg-surface-overlay
               even:bg-surface-subtle">
```

### Input
```tsx
<input className="w-full text-sm text-text-primary
                  bg-white border border-border-default rounded-md
                  px-3 py-2
                  placeholder:text-text-muted
                  focus:outline-none focus:ring-2 focus:ring-primary-500
                  focus:border-primary-500
                  disabled:bg-surface-subtle disabled:text-text-muted" />
```

### Card
```tsx
<div className="bg-white border border-border-default rounded-lg shadow-sm p-6">
```

### Navbar
```tsx
<nav className="bg-surface-inverse text-text-inverse h-14 px-6
                flex items-center justify-between">
  <span className="text-primary-300 font-semibold">ERP</span>
</nav>
```

---

## Reguli de utilizare

```
Primary (#1E88D0)  — butoane principale, link-uri, focus ring, navbar accent
                     NICIODATĂ pe suprafețe mari (fundal pagină, card background)

Griuri             — domină suprafețele: fundaluri, carduri, tabele
Semantice          — exclusiv pentru status și feedback (success, warning, danger, info)

Font size base     — 14px în ERP (nu 16px) — densitate de date mai mare
Border radius      — maxim 8px — ERP arată profesional, nu playful
Umbre              — subtile (shadow-sm, shadow-md) — niciodată dramatice
Animații           — maxim 150ms transition-colors — nu bounce, nu spring
Gradiente          — INTERZISE pe suprafețe și butoane principale
Dark mode          — opțional, nu default
```
