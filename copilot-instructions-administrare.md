# Copilot Instructions — Meniu Administrare (ERP)

## Context

Aplicația ERP are o structură de navigare cu două niveluri:
- **Header** — navigare globală (module principale); la click pe un modul cu submodule se deschide un dropdown.
- **Sidebar** — navigare locală, se populează cu intrările modulului activ selectat în header.

---

## Obiectiv

Adaugă un nou modul **Administrare** în header și sidebar, cu structura de navigare descrisă mai jos.

---

## Structura completă a modulului Administrare

```
Administrare
├── Parteneri
│   ├── Parteneri
│   ├── Tipuri Parteneri
│   └── Ierarhie Parteneri
├── Personal
│   ├── Persoane
│   └── Utilizatori
└── Articole
    ├── Articole
    ├── Tipuri de articole
    ├── Catalog articole
    └── Liste de preț
```

---

## Modificări necesare

### 1. Header — nav item cu dropdown

Adaugă `Administrare` ca ultim item în lista de module principale din header (înainte de `Setări` dacă există, sau la final).

**Comportament:**
- La hover sau click pe `Administrare` se deschide un dropdown cu cele 3 submodule: `Parteneri`, `Personal`, `Articole`.
- Dropdown-ul se închide la click în afara lui sau la selectarea unui submodul.
- Selectarea unui submodul din dropdown setează modulul activ și populează sidebar-ul cu submodulul respectiv deschis.
- Iconița recomandată: `ti-settings-2` sau `ti-shield-cog` (Tabler outline).

**Structura HTML/JSX dropdown (model):**
```html
<div class="nav-item has-dropdown" data-module="administrare">
  <i class="ti ti-settings-2"></i>
  Administrare
  <i class="ti ti-chevron-down dropdown-arrow"></i>

  <div class="nav-dropdown">
    <a class="dropdown-item" data-submodule="parteneri">
      <i class="ti ti-building"></i> Parteneri
    </a>
    <a class="dropdown-item" data-submodule="personal">
      <i class="ti ti-users"></i> Personal
    </a>
    <a class="dropdown-item" data-submodule="articole">
      <i class="ti ti-box"></i> Articole
    </a>
  </div>
</div>
```

**CSS dropdown (model):**
```css
.nav-item.has-dropdown { position: relative; }
.nav-dropdown {
  display: none;
  position: absolute;
  top: 100%;
  left: 0;
  min-width: 180px;
  background: #fff;
  border: 0.5px solid #DDE8F0;
  border-radius: 6px;
  box-shadow: 0 4px 12px rgba(30,136,208,0.1);
  z-index: 100;
  padding: 4px 0;
}
.nav-item.has-dropdown:hover .nav-dropdown,
.nav-item.has-dropdown.open .nav-dropdown { display: block; }
.dropdown-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  font-size: 13px;
  color: #2C4A6E;
  cursor: pointer;
  white-space: nowrap;
}
.dropdown-item:hover { background: #EBF5FF; color: #1E88D0; }
.dropdown-item i { font-size: 15px; color: #4A80B5; }
```

---

### 2. Sidebar — secțiune Administrare cu 3 grupuri colapsabile

Când modulul activ din header este `Administrare` (sau oricare din submodulele sale), sidebar-ul afișează structura completă a modulului.

Fiecare din cele 3 submodule (Parteneri, Personal, Articole) este un **grup colapsabil** în sidebar, cu copiii săi ca itemi de navigare simpli.

**Comportament:**
- Grupurile sunt expandate implicit când modulul este activ.
- Click pe header-ul unui grup îl colapsează/expandează.
- Itemul activ curent este marcat cu `border-left: 3px solid #1E88D0` și `background: #fff`.
- Badge-urile cu numere se adaugă ulterior; deocamdată nu sunt necesare.

**Structura HTML/JSX sidebar (model):**
```html
<div class="sidebar-section">
  <div class="sidebar-label">Administrare</div>

  <!-- Grup: Parteneri -->
  <div class="sidebar-group">
    <div class="sidebar-group-header" data-group="parteneri">
      <i class="ti ti-building"></i>
      Parteneri
      <i class="ti ti-chevron-down group-arrow"></i>
    </div>
    <div class="sidebar-group-items">
      <div class="sidebar-item sidebar-subitem" data-route="/admin/parteneri">
        <i class="ti ti-building-community"></i> Parteneri
      </div>
      <div class="sidebar-item sidebar-subitem" data-route="/admin/tipuri-parteneri">
        <i class="ti ti-tag"></i> Tipuri Parteneri
      </div>
      <div class="sidebar-item sidebar-subitem" data-route="/admin/ierarhie-parteneri">
        <i class="ti ti-hierarchy"></i> Ierarhie Parteneri
      </div>
    </div>
  </div>

  <!-- Grup: Personal -->
  <div class="sidebar-group">
    <div class="sidebar-group-header" data-group="personal">
      <i class="ti ti-users"></i>
      Personal
      <i class="ti ti-chevron-down group-arrow"></i>
    </div>
    <div class="sidebar-group-items">
      <div class="sidebar-item sidebar-subitem" data-route="/admin/persoane">
        <i class="ti ti-user"></i> Persoane
      </div>
      <div class="sidebar-item sidebar-subitem" data-route="/admin/utilizatori">
        <i class="ti ti-user-shield"></i> Utilizatori
      </div>
    </div>
  </div>

  <!-- Grup: Articole -->
  <div class="sidebar-group">
    <div class="sidebar-group-header" data-group="articole">
      <i class="ti ti-box"></i>
      Articole
      <i class="ti ti-chevron-down group-arrow"></i>
    </div>
    <div class="sidebar-group-items">
      <div class="sidebar-item sidebar-subitem" data-route="/admin/articole">
        <i class="ti ti-package"></i> Articole
      </div>
      <div class="sidebar-item sidebar-subitem" data-route="/admin/tipuri-articole">
        <i class="ti ti-tags"></i> Tipuri de articole
      </div>
      <div class="sidebar-item sidebar-subitem" data-route="/admin/catalog">
        <i class="ti ti-book"></i> Catalog articole
      </div>
      <div class="sidebar-item sidebar-subitem" data-route="/admin/liste-pret">
        <i class="ti ti-currency-dollar"></i> Liste de preț
      </div>
    </div>
  </div>
</div>
```

**CSS sidebar groups (model):**
```css
.sidebar-group-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  font-size: 12.5px;
  font-weight: 500;
  color: #2C4A6E;
  cursor: pointer;
  user-select: none;
}
.sidebar-group-header:hover { background: rgba(255,255,255,0.5); }
.sidebar-group-header i { font-size: 16px; color: #4A80B5; }
.group-arrow { margin-left: auto; font-size: 13px; transition: transform 0.15s; }
.sidebar-group.collapsed .group-arrow { transform: rotate(-90deg); }
.sidebar-group.collapsed .sidebar-group-items { display: none; }
.sidebar-subitem {
  padding-left: 36px; /* indent față de group header */
  font-size: 12px;
  color: #3A5A7A;
}
.sidebar-subitem i { font-size: 14px; color: #6B8FAF; }
```

---

## Rute recomandate

| Pagină | Rută |
|---|---|
| Parteneri | `/admin/parteneri` |
| Tipuri Parteneri | `/admin/tipuri-parteneri` |
| Ierarhie Parteneri | `/admin/ierarhie-parteneri` |
| Persoane | `/admin/persoane` |
| Utilizatori | `/admin/utilizatori` |
| Articole | `/admin/articole` |
| Tipuri de articole | `/admin/tipuri-articole` |
| Catalog articole | `/admin/catalog-articole` |
| Liste de preț | `/admin/liste-pret` |

---

## Iconițe Tabler recomandate (outline only)

| Element | Icoană |
|---|---|
| Administrare (modul) | `ti-settings-2` |
| Parteneri (grup) | `ti-building` |
| Parteneri (item) | `ti-building-community` |
| Tipuri Parteneri | `ti-tag` |
| Ierarhie Parteneri | `ti-hierarchy` |
| Personal (grup) | `ti-users` |
| Persoane | `ti-user` |
| Utilizatori | `ti-user-shield` |
| Articole (grup) | `ti-box` |
| Articole (item) | `ti-package` |
| Tipuri de articole | `ti-tags` |
| Catalog articole | `ti-book` |
| Liste de preț | `ti-currency-dollar` |

---

## Note pentru Copilot

- Folosește **Tabler Icons outline** exclusiv — fără sufixe `-filled`.
- Paleta de culori: primary `#1E88D0`, sidebar bg `#E4EFF8`, active bg `#fff`, active border `#1E88D0`, text `#2C4A6E`.
- Dropdown-ul din header trebuie să aibă `z-index` suficient de mare (≥100) pentru a nu fi acoperit de alte elemente.
- Grupurile din sidebar colapsabile se gestionează prin adăugarea/eliminarea clasei `collapsed` pe `.sidebar-group` via JavaScript.
- Itemul activ se marchează adăugând clasa `active` pe `.sidebar-item`; resetează `active` pe toate itemele înainte de a seta cel nou.
- Dacă proiectul folosește React/Vue/Angular, adaptează structura HTML de mai sus la componentele corespunzătoare, păstrând logica de state descrisă.
