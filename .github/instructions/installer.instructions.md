---
description: 'Reguli pentru installer (WPF + CLI), generatorul de licente, impachetare, migrari si recuperarea upgrade-ului.'
applyTo: 'src/Setup/**/*,src/Tools/Erp.LicenseGen*/**/*,src/Shared/Erp.Licensing/**/*,build/**/*,Docs/arhitectura/08-instalare-actualizare/**/*'
---
# Installer

Sursa: [Instalare si actualizare](../../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md), versiunea 3. Pentru implementare foloseste skill-ul [erp-installer](../skills/erp-installer/SKILL.md).

- Respecta R1-R14 (sectiunea 0). Inainte de codul installer-ului, al generatorului sau al licentei citeste integral specificatia; nu simplifica tacut. Nu este obligatorie recitirea integrala pentru o simpla editare de documentatie.
- Straturi (5.2): logica si ViewModel-urile in proiecte `net10.0`; numai View-urile WPF in `net10.0-windows` cu `EnableWindowsTargeting`. `ErpSetup.exe` (WPF) si `ErpSetup.Cli.exe` construiesc acelasi `InstallRequest` si acelasi plan; fara optiuni care sar verificarile in Release. Nu MSI/WiX/PowerShell ca logica principala.
- DbUp, Dapper, store-uri Identity fara EF. Toate schemele instalate indiferent de licenta. Collation fix, RCSI, recovery FULL, compatibilitate 160.
- SQL numai cu Windows authentication: fara parole SQL, fara fisiere de secrete, fara login separat de migrare. Operatorul are `sysadmin` doar pe durata operatiei; aplicatia fara `sysadmin`/`db_owner`; gMSA recomandat pe SQL la distanta.
- `install`: exact un administrator initial. `upgrade`/`repair`/`recover`: nu crea/modifica utilizatori; doar `reset-admin` schimba accesul administratorului.
- Pasi idempotenti, stare persistata (`instance.json`); nu suprascrie baza cu date si nu sterge baza/backup-urile; singura baza stearsa e snapshot-ul propriu de upgrade.
- Verifica `manifest.sig` si hash-urile kitului inainte de modificari. Secrete niciodata in argumente CLI sau jurnale; exceptii numai parola initiala (ecranul 6; `initial-admin.txt` la `--unattended`, 5.10).
- Licenta (3, 4): format binar AES-GCM + ECDSA, chei active si de rezerva; licenta sta in baza, nu pe disc.
- Cheile Data Protection in baza, protejate cu certificatul instalarii; cheia de recuperare obligatorie la instalare; `repair` nu recreeaza certificatul (5.12).
- Upgrade: U-01-U-10, revenire R-01-R-06 din snapshot sau backup; dupa U-10 nu restaura automat. Pastreaza codurile 70/71/73 si mentenanta la esecul revenirii.
- Nu executa instalare, restore, modificari IIS/servicii/firewall sau dezinstalare pe infrastructura reala fara autorizare explicita; testele cu efecte administrative cer mediu dedicat.
