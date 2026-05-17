---
mode: ask
description: Explică o decizie arhitecturală sau un pattern din proiect cu context ERP specific.
---

Explică decizia arhitecturală sau pattern-ul întrebat în contextul acestui proiect ERP, referențiind:

- `ERP_Architecture.md` pentru context general
- `.github/copilot-instructions.md` pentru convenții
- Skill-ul relevant din `.github/skills/` dacă există

**Contextul proiectului:**
- Modular Monolith + Clean Architecture + Vertical Slice Architecture
- SQL Server 2025, Dapper, zero SQL inline în C#, SP-uri pentru tot accesul la date
- Multi-tenant cu `tenant_id` pe orice tabel și orice SP
- Result<T> pentru erori business, excepții doar pentru erori tehnice
- UUIDv7 pentru aggregate roots, BIGINT IDENTITY pentru child tables

Răspunde cu:
1. **Ce este** — definiție scurtă
2. **De ce îl folosim** — motivul specific pentru ERP-ul nostru
3. **Cum se implementează** — exemplu de cod din contextul proiectului
4. **Ce să eviți** — greșelile frecvente
