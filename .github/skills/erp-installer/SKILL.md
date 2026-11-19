---
name: erp-installer
description: 'Implementeaza sau verifica installer-ul ERP (WPF + CLI), generatorul de licente, kitul, bootstrap-ul si recuperarea upgrade-ului conform sarcinilor T1-T13; nu autorizeaza instalarea pe un server real.'
---
# Implementarea installer-ului ERP

## Conditie de pornire

Aplica [AGENTS.md](../../../AGENTS.md) si [instructiunile installer](../../instructions/installer.instructions.md).

**Inainte de cod, citeste integral [specificatia installer-ului](../../../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md)**, conform sectiunii 0. Citeste in intervale succesive, fara trunchiere sau omiterea sectiunilor; nu o reciti daca versiunea curenta este deja integral in context. Aceasta este exceptia justificata de la citirea selectiva, nu permisiunea de a incarca toate celelalte documente.

## Procedura

1. Identifica sarcina T1-T13 ceruta (sectiunea 13.2), prerequisitele implementate si criteriile IA asociate. Lucreaza numai la sarcina ceruta; semnaleaza lipsurile, nu construi automat intregul installer.
2. Pleaca de la codul existent conform tabelului 13.1: pastreaza si adapteaza ce e marcat asa, sterge numai ce e marcat de sters. Refoloseste `IInstallStep`, orchestratorul, `PlanBuilder` si ajutoarele comune. Respecta R1-R14 si abaterile din sectiunea 15.
3. Pastreaza contractele: comenzile CLI si codurile de iesire (6), fisierul de raspunsuri (6.2), `instance.json` (5.7), pasii C/I/U/R. Fereastra si CLI produc acelasi `InstallRequest` (IA-13). Trateaza distinct reluarea unui `install` si refuzul reluarii unui `upgrade` intrerupt.
4. La modificari SQL/Identity valideaza: baza CS pe server CI, DbUp ordonat/idempotent, toate schemele, exact un administrator initial, niciun utilizator modificat la upgrade/repair/recover, loginuri Windows fara `db_owner`.
5. La upgrade demonstreaza mentenanta (`app_offline.htm` + `maintenance.flag`), backup-ul verificat si snapshot-ul inainte de migrare, coerenta binare/schema si health-check inainte de U-10. Injecteaza esecuri de migrare/comutare/health-check si testeaza revenirea din snapshot si din backup, inclusiv esecul revenirii si codurile 70/71/73.
6. La kit/licenta/chei verifica semnaturi, hash-uri, cheile de rezerva, ACL-uri, cheia de recuperare, redactarea jurnalelor/diagnosticului. Nu folosi secrete reale in teste; nu schimba politica de licenta sau autentificare.
7. Ruleaza testele IA relevante din sectiunea 12. Unitare/integrari SQL nu substituie E2E Windows pe VM dedicata. Operatiuni administrative numai intr-un mediu autorizat explicit; nu folosi implicit masina curenta.
8. Verifica idempotenta si conservarea bazei/backup-urilor; actualizeaza doar documentatia si testele sarcinii. Nu declara livrabil kitul fara verificarile de release necesare.

## Rezultat

Raporteaza T/IA acoperite, fisiere schimbate, comenzile de validare si rezultatele, inclusiv E2E nerulate. Nu executa deploy, restore sau uninstall ca efect implicit al incarcarii skill-ului.

Exemplu: „Implementeaza T11” -> upgrade/revenire si testul IA-10, nu rularea unui upgrade pe o instalare existenta.
