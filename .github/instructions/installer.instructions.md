---
description: 'Reguli pentru installer .NET, impachetare, migrari si recuperarea upgrade-ului.'
applyTo: 'src/Setup/**/*,build/**/*,Docs/arhitectura/08-instalare-actualizare/**/*'
---
# Installer

Sursa: [Instalare si actualizare](../../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md). Pentru implementare foloseste skill-ul [erp-installer](../skills/erp-installer/SKILL.md).

- Respecta R1-R10. Inainte de codul installer-ului citeste integral specificatia (sectiunea 0); nu simplifica tacut. Nu este obligatorie recitirea integrala pentru o simpla editare de documentatie.
- `ErpSetup.exe`: consola .NET 10, self-contained, single-file, `win-x64`; nu MSI/WiX/InstallShield/PowerShell ca logica principala. Pastreaza structura sectiunii 5.
- DbUp, Dapper, store-uri Identity fara EF. Toate schemele instalate indiferent de licenta. Collation fix, RCSI, recovery FULL, compatibilitate 160.
- `install`: exact un administrator initial, cu parola temporara, schimbare obligatorie si 2FA. `upgrade`/`repair`: nu crea/modifica utilizatori.
- Pasi idempotenti, stare persistata, oprire la esec explicit; nu suprascrie baza existenta la instalare si nu sterge baza/backup-urile la dezinstalare.
- Verifica semnatura/hash-urile kitului inainte de modificari. Secrete protejate, niciodata argumente CLI/jurnale; numai afisarea controlata a parolei initiale din 8.8.
- Upgrade: mentenanta, backup verificat, migratii forward-only, comutare binare, health-check cu versiunea/schema asteptate, abia apoi acces utilizatori.
- Rollback automat numai conform U-07-U-14/R-01-R-07; dupa U-15 nu restaura automat date vechi. Pastreaza codurile 70/71/73 si mentenanta la esecul recuperarii.
- Credentiale de migrare separate si dezactivate inclusiv la esec; contul aplicatiei fara `sysadmin`/`db_owner`.
- Nu executa instalare, restore, modificari IIS/servicii/firewall sau dezinstalare pe infrastructura reala fara autorizare explicita; testele cu efecte administrative cer mediu dedicat.
