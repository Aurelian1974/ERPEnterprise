---
name: erp-installer
description: 'Implementeaza sau verifica installer-ul ERP, kitul, bootstrap-ul si recuperarea upgrade-ului conform sarcinilor T1-T16; nu autorizeaza instalarea pe un server real.'
---
# Implementarea installer-ului ERP

## Conditie de pornire

Aplica [AGENTS.md](../../../AGENTS.md) si [instructiunile installer](../../instructions/installer.instructions.md).

**Inainte de cod, citeste integral [specificatia installer-ului](../../../Docs/arhitectura/08-instalare-actualizare/INSTALARE_ACTUALIZARE.md)**, conform sectiunii 0. Citeste in intervale succesive, fara trunchiere sau omiterea sectiunilor; nu o reciti daca versiunea curenta este deja integral in context. Aceasta este exceptia justificata de la citirea selectiva, nu permisiunea de a incarca toate celelalte documente.

## Procedura

1. Identifica sarcina T1-T16 ceruta (sectiunea 15), prerequisitele implementate si criteriile IA asociate. Lucreaza numai la sarcina ceruta; semnaleaza lipsurile, nu construi automat intregul installer.
2. Refoloseste structura sectiunii 5, `IInstallStep`, orchestratorul si ajutoarele comune pentru parole, licente/configurare. Respecta R1-R10 si exceptiile limitate din sectiunea 17.
3. Pastreaza contractele CLI, codurile de iesire, fisierele de stare si pasii C/I/U/R conform specificatiei. Trateaza distinct reluarea unui `install` si refuzul reluarii unui `upgrade` intrerupt.
4. La modificari SQL/Identity valideaza: baza CS pe server CI, DbUp ordonat/idempotent, toate schemele, exact un administrator initial, niciun utilizator modificat la upgrade/repair, credentiale de migrare dezactivate inclusiv la esec.
5. La upgrade demonstreaza mentenanta si backup-ul verificat inainte de migrare, coerenta binare/schema si health-check inainte de U-15. Injecteaza esecuri de migrare/comutare/health-check si testeaza recuperarea aplicabila, inclusiv esecul recuperarii si codurile 70/71/73.
6. La kit/secrete verifica semnaturi, hash-uri, DPAPI/ACL, redactarea jurnalelor/diagnosticului. Nu folosi secrete reale in teste; nu schimba politica de licenta sau autentificare.
7. Ruleaza testele IA relevante din sectiunea 14. Unitare/integrari SQL nu substituie E2E Windows pe VM dedicata. Operatiuni administrative numai intr-un mediu autorizat explicit; nu folosi implicit masina curenta.
8. Verifica idempotenta si conservarea bazei/backup-urilor; actualizeaza doar documentatia si testele sarcinii. Nu declara livrabil kitul fara verificarile de release necesare.

## Rezultat

Raporteaza T/IA acoperite, fisiere schimbate, comenzile de validare si rezultatele, inclusiv E2E nerulate. Nu executa deploy, restore sau uninstall ca efect implicit al incarcarii skill-ului.

Exemplu: „Implementeaza T14” -> upgrade/rollback si teste IA-09-IA-12, nu rularea unui upgrade pe o instalare existenta.
