# ERP · 02.13 Raportare și BI · BI5 Bugetare și plan vs realizat

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P7, P8, P19). Ultima din cele 5 părți ale modulului 02.13.

Plaja de erori BI5: `62600–62749`. Documente părinte:

- [BI1](BI1-CONFIGURARE-CATALOG.md)–[BI4](BI4-TABLOURI-KPI-ALERTE.md);
- [Finanțe C1](../02-finante-contabilitate/C1-PLANURI-DE-CONTURI.md): planurile de conturi;
- [C5](../02-finante-contabilitate/C5-RAPOARTE.md): `IBalanceQuery` și rapoartele pe rânduri;
- [F5](../02-finante-contabilitate/F5-PLANIFICARE-TREZORERIE.md): prognoza de trezorerie;
- [Vânzări V1](../03-vanzari/V1-CONFIGURARE.md) și [V7](../03-vanzari/V7-RAPOARTE.md);
- [Achiziții A3](../04-achizitii/A3-COMENZI.md): comenzile deschise, pentru angajamente;
- [Nucleu](../01-nucleu/NUCLEU.md): dimensiunile, tranzacțiile §3.6, fluxul de aprobare;
- [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- În celelalte documente, bugetul a fost lăsat explicit pentru 02.13: F5 („bugetul de venituri și cheltuieli, ulterior”), V7 și CR3 („bugetul de vânzări, V7 / 02.13”), M3 („bugetul și analizele, 02.13”). BI5 îl definește.
- **Bugetul de venituri și cheltuieli (BVC)** se face pe **conturi de venituri și cheltuieli** (clasele 6 și 7, cu analiticele lor), pe **centru de cost** (și opțional proiect sau șantier, din dimensiunile Nucleu) și pe **lună**. Realizatul vine din balanță, prin `IBalanceQuery` (C5), pe aceleași chei.
- **Bugetul de vânzări** se face pe agent, categorie de articol și lună, în valoare și opțional în cantitate. Realizatul vine din `sales.vw_SalesFact`.
- Bugetul e un **document** cu versiuni (inițial, rectificat, prognoză), aprobare, tranzacții (Nucleu §3.6) și istoric. Nu are efect contabil.
- Bugetul aprobat devine **țintă** pentru KPI-uri (BI4), pentru agenți (`SalesAgentTarget`) și pentru prognoza CRM.

## 1. Scop și perimetru

| Zonă | Ce face BI5 | Ce NU face |
| --- | --- | --- |
| Structura | Șabloane de buget (ce conturi, ce dimensiuni, ce granularitate), responsabilii pe centru de cost | Planurile de conturi (C1) |
| Întocmirea | Introducerea pe grilă, copierea din anul anterior cu procent, repartizarea anuală pe luni (egal, după sezonalitatea anului anterior, manual), importul din Excel | — |
| Versiuni și aprobare | Ciornă → Trimis → Aprobat; rectificări ca versiuni noi; prognoza de an (realizat + rest bugetat sau reestimat) | — |
| Plan vs realizat | Abaterea pe orice nivel, cu desfășurare până la nota contabilă și documentul sursă; angajamentele din comenzile de achiziție | Notele contabile (C2) |
| Control bugetar | Avertisment la comanda de achiziție și la factura de furnizor care depășesc bugetul rămas al centrului de cost (setare) | Blocarea (decizia 6) |
| Legături | Țintele BI4, `SalesAgentTarget` (V1), prognoza F5 (opțional, cheltuielile bugetate ca plăți estimate) | — |

## 2. Model de domeniu

```
 rpt.BudgetTemplate (Kind = BVC | VANZARI, AccountSet, Dimensions, Granularity = lună)
 rpt.Budget (BUG 2026/01 …) ── document Nucleu §3.6, tranzacții BUG-BVC · BUG-VNZ · BUG-REV
   ├─ CompanyId · Year · TemplateId · VersionKind (Inițial | Rectificat | Prognoză) · VersionNo · ParentBudgetId
   ├─ Status: 1 Ciornă · 2 Trimis · 3 Aprobat · 4 Înlocuit · 5 Anulat
   ├─ rpt.BudgetLine × N (AccountId | ItemCategoryId, CostCenterId?, ProjectId?, SalesAgentId?, Month 1–12, Amount, Quantity?)
   ├─ rpt.BudgetSection × N (CostCenterId, ResponsibleUserId, SectionStatus)   (fiecare responsabil își completează partea)
   └─ rpt.BudgetComment × N
 rpt.vw_BudgetFact (BI2) · rpt.vw_BudgetVsActual (realizat din IBalanceQuery / vw_GlLineFact și vw_SalesFact; angajat din purch.vw_OrderCommitment)
```

Reguli de business:

1. **Un singur buget aprobat activ** pe firmă, an și șablon. Aprobarea unei rectificări trece versiunea anterioară în „Înlocuit” (`62601`). Comparațiile pot folosi orice versiune („față de inițial”, „față de rectificat”).
2. **Liniile de BVC folosesc doar conturi de venituri sau cheltuieli** (clasele 6 și 7, după planul C1 al firmei) și conturi analitice active (`62602`). Bugetarea pe conturi sintetice e permisă: realizatul se adună atunci din toate analiticele.
3. **Realizatul nu se copiază în buget.** Se citește de fiecare dată din contabilitate (`IBalanceQuery`), pe lunile închise și pe luna curentă (marcată „provizoriu”).
4. **Prognoza de an** = realizatul lunilor închise + bugetul (sau reestimarea) lunilor rămase. Se salvează ca versiune „Prognoză”, fără să înlocuiască bugetul aprobat.
5. **Secțiunile:** bugetul se împarte pe centre de cost, cu un responsabil (`DimensionValue.ResponsibleUserId`, Nucleu). Responsabilul își completează și trimite secțiunea. Bugetul se trimite la aprobare doar cu toate secțiunile trimise (`62610`).
6. **Aprobarea** urmează fluxul de aprobare Nucleu: Director economic, apoi Director general, peste un prag setabil. Bugetul aprobat nu se modifică (`62620`); orice schimbare e o rectificare (`BUG-REV`).
7. **Angajamentele** (comenzile de achiziție confirmate și nefacturate, pe centru de cost și cont de cheltuială, din A3) apar separat de realizat: „Realizat + Angajat ≤ Buget” e controlul bugetar.

## 3. Fluxuri detaliate

### 3.1 Întocmirea

1. Ion Popescu (Director economic) creează **BUG 2027/01**, BVC 2027 pentru Exemplu Distribuție, din șablonul „BVC pe centre de cost”, cu „copiază realizatul 2026 (prognoza de an) + 5 %” și repartizarea „după sezonalitatea 2026”.
2. Secțiunile pleacă la responsabili, care primesc notificarea:
   - CC-VNZ-CJ, Radu Stan;
   - CC-LOG, Gheorghe Ilie;
   - CC-ADM, Adrian Barbu;
   - CC-FIN-CJ, Ion Popescu.
3. Fiecare responsabil lucrează pe grilă (conturi × luni):
   - modifică celule;
   - aplică procente pe rânduri;
   - lasă comentarii pe celule;
   - poate exporta în Excel și reimporta (aceeași structură, validată la import, `62630` pe rândurile greșite).
4. Bugetul de vânzări (`BUG-VNZ`) se întocmește pe agent și categorie. La aprobare, totalul lui trebuie să fie egal cu veniturile din vânzări din BVC (contul 707 și analiticele lui), cu toleranța setabilă (implicit 0,5 %, `62640` avertisment).

### 3.2 Plan vs realizat (P8, P19)

Raportul `rpt.BudgetVsActual` are o grilă pe ierarhia:

- centru de cost → cont sintetic → cont analitic, pe rânduri;
- luna, trimestrul sau cumulatul anului, pe coloane.

Măsurile sunt Buget, Realizat, Angajat, Abatere, Abatere % și Prognoză de an. Abaterile se colorează după natura contului:

- la cheltuieli, depășirea e roșie;
- la venituri, sub buget e roșu.

**Desfășurarea abaterii:**

1. Clicul pe celulă deschide liniile de notă din `fin.vw_GlLineFact`, grupate pe partener și document.
2. Clicul pe document deschide factura de furnizor (A5), bonul de consum (S2) sau nota (C2).

Pentru bugetul de vânzări, desfășurarea merge pe client și pe factură.

Coloanele de buget se pot folosi și în **rapoartele financiare pe rânduri ale C5** (`fin.ReportDefinition`), prin tipul de coloană nou „Buget” (versiunea aleasă). Așa se obține contul de profit și pierdere „Realizat vs Buget” în formatul firmei (precizare în C5, P7).

### 3.3 Controlul bugetar (setarea `BudgetControlMode`)

| Valoare | Efect |
| --- | --- |
| 0 Fără | — |
| 1 Avertisment (implicit) | La confirmarea comenzii de achiziție (A3) și la validarea facturii de furnizor fără comandă (A5), dacă „Realizat + Angajat + documentul” depășește bugetul anului pe centrul de cost și cont, apare avertismentul cu suma depășirii. Documentul se poate salva cu motiv, iar motivul apare în raportul de depășiri |
| 2 Aprobare | Depășirea trimite documentul în fluxul de aprobare Nucleu, la responsabilul centrului de cost |

Blocarea fără aprobare nu există (decizia 6): documentele fiscale (factura primită) trebuie înregistrate oricum.

### 3.4 Legăturile

- **BI4:** bugetul aprobat generează ținte (`KpiTarget`, `Source = buget`) pentru Cifra de afaceri, Cheltuieli și Rezultat, pe firmă și lună.
- **V1:** bugetul de vânzări aprobat scrie `sales.SalesAgentTarget` pe agent și lună. Țintele introduse manual de șeful de vânzări se suprascriu doar cu confirmare.
- **F5 (opțional, setarea `BudgetToTreasury`):** cheltuielile bugetate ale lunilor viitoare, fără documente încă (chirii, utilități), apar în prognoza de trezorerie ca „plăți estimate din buget”, cu decalajul de plată al contului.

## 4. Exemplu: BVC 2026 al Exemplu Distribuție, situația la 31.10.2026

Bugetul **BUG 2026/02** e rectificarea din iulie 2026 a BUG 2026/01, aprobată pe 15.07.2026. Cumulat ianuarie–octombrie, la nivel de centru de cost:

| Centru de cost | Cont | Buget | Realizat | Angajat | Abatere | Abatere % |
| --- | --- | --- | --- | --- | --- | --- |
| CC-VNZ-CJ | 707 Venituri din vânzarea mărfurilor | 31.200.000 | 30.418.600 | — | −781.400 | −2,5 % |
| CC-VNZ-CJ | 607 Cheltuieli cu mărfurile | 23.400.000 | 22.873.900 | — | −526.100 | −2,2 % |
| CC-LOG | 624 Transport | 980.000 | 1.098.600 | — | +118.600 | **+12,1 %** |
| CC-LOG | 6022 Combustibil | 410.000 | 398.300 | — | −11.700 | −2,9 % |
| CC-ADM | 612 Chirii | 720.000 | 720.000 | — | 0 | 0,0 % |
| CC-ADM | 641 Salarii | 3.150.000 | 3.096.400 | — | −53.600 | −1,7 % |
| CC-FIN-CJ | 627 Servicii bancare | 86.000 | 91.300 | — | +5.300 | +6,2 % |

Desfășurarea abaterii la transport arată unde e diferența:

- facturile Translog Cargo SRL (transport pe rutele Cluj–Timișoara) au crescut din august, cu +2.900 lei pe lună față de contract;
- 6 transporturi urgente au costat 28.400 lei, legate de retururile din cazurile CRM.

Controlul bugetar a avertizat la comanda de transport CF 2412 din 03.11, pentru 41.200 lei. Bugetul anual al contului 624 pe CC-LOG e 1.120.000 lei (BUG 2026/02 a redus noiembrie și decembrie la 70.000 lei fiecare). Cu comanda, „Realizat + Angajat + documentul” ajungea la 1.098.600 + 0 + 41.200 = 1.139.800 lei, deci depășirea anuală era de 19.800 lei. Gheorghe Ilie, responsabilul CC-LOG, a salvat-o cu motivul „sezon de vârf, aprobat verbal de directorul general”. De atunci, comanda apare la „Angajat” până la factură.

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `BugetAprobat` v1 | Publicat | Bugetul, versiunea, firma, anul | BI4 (ținte), V1 (`SalesAgentTarget`), F5 (opțional) |
| `DepasireBugetConfirmata` v1 | Publicat | Documentul, centrul de cost, contul, suma, motivul | Raportul de depășiri, BI4 (alertă) |
| `LunaInchisa` v1 | Consumat (C3) | — | Realizatul lunii devine definitiv în plan vs realizat |

## 6. Contracte publice

```csharp
public interface IBudgetCheck              // folosit de A3 (confirmarea comenzii) și A5 (factura fără comandă)
{
    Task<BudgetCheckResult> CheckAsync(int companyId, int costCenterId, int accountId, DateOnly date, decimal amountRon, CancellationToken ct);
}
public sealed record BudgetCheckResult(bool HasBudget, decimal Budget, decimal Actual, decimal Committed, decimal Remaining, decimal Overrun, int Mode);
```

Achizițiile cheamă `IBudgetCheck` printr-un port opțional. Fără pachetul BI, portul răspunde „fără buget” și nu apare niciun avertisment.

## 7. Schema SQL (partea BI5)

| Tabel | Cheie primară | Chei unice / indexuri |
| --- | --- | --- |
| `rpt.BudgetTemplate` | `Id int` | UX `(CompanyGroupId, Code)` |
| `rpt.Budget` | `Id int` | UX `(CompanyId, DocumentNumber)`; UX `(CompanyId, Year, TemplateId) WHERE Status = 3`; FK `ParentBudgetId` |
| `rpt.BudgetLine` | `Id bigint` | UX `(BudgetId, AccountId, CostCenterId, ProjectId, SalesAgentId, ItemCategoryId, Month)` (coloanele NULL tratate prin coloane calculate cu 0); columnstore neclusterizat |
| `rpt.BudgetSection`, `rpt.BudgetComment` | `Id int` | FK `BudgetId` |
| `rpt.BudgetOverrun` | `Id bigint` | Depășirile confirmate, cu documentul și motivul |
| `purch.vw_OrderCommitment` (nou, A3) | Vedere | Comenzile confirmate nefacturate pe centru de cost și cont de cheltuială |

Procedurile:

- `rpt.usp_Budget_Save`;
- `rpt.usp_Budget_CopyFrom` (din alt buget sau din realizat, cu procent și repartizare);
- `rpt.usp_Budget_Submit`, `rpt.usp_Budget_Approve`;
- `rpt.usp_Budget_VsActual`;
- `rpt.usp_Budget_Check`.

**Erorile BI5** (`62600–62749`):

- `62601` `RPT_BUDGET_ACTIVE_EXISTS`
- `62602` `RPT_BUDGET_ACCOUNT_INVALID`
- `62610` `RPT_BUDGET_SECTIONS_PENDING`
- `62620` `RPT_BUDGET_APPROVED_READONLY`
- `62630` `RPT_BUDGET_IMPORT_ROW_INVALID`
- `62640` `RPT_BUDGET_SALES_MISMATCH` (avertisment)
- `62650` `RPT_BUDGET_OVERRUN`: avertisment sau aprobare, după `BudgetControlMode`

## 8. Permisiuni

- `Rpt.Budget.View`;
- `Rpt.Budget.Edit` (secțiunile proprii; responsabilul centrului de cost);
- `Rpt.Budget.Manage` (tot bugetul; Director economic, Responsabil buget);
- `Rpt.Budget.Approve`;
- `Rpt.Budget.ControlOverride` (salvarea cu depășire).

## 9. Precizări în alte module (aplicate pe 27.11.2026)

| Modul | Ce se adaugă |
| --- | --- |
| C5 | Tipul de coloană „Buget” (versiunea) în `fin.ReportColumn`, pentru rapoartele pe rânduri „Realizat vs Buget” |
| F5 | Rândul opțional „plăți estimate din buget” (`BudgetToTreasury`) |
| A3 | Vederea `purch.vw_OrderCommitment`; apelul `IBudgetCheck` la confirmare, cu avertisment sau aprobare |
| A5 | Apelul `IBudgetCheck` la factura fără comandă |
| V1 | `sales.SalesAgentTarget` se poate scrie din bugetul de vânzări aprobat (cu confirmare peste țintele manuale) |
| Nucleu | Tipul de document `Budget` (seria `BUG`), tranzacțiile `BUG-BVC`, `BUG-VNZ`, `BUG-REV`; rolul Responsabil buget |

## 10. Rapoarte

- `rpt.BudgetVsActual`: plan vs realizat, cu desfășurare;
- `rpt.BudgetForecast`: prognoza de an;
- `rpt.BudgetOverruns`: depășirile confirmate, cu motivele;
- `rpt.SalesBudgetVsActual`: pe agent, categorie și lună.

## 11. Ecrane principale

Machetele sunt în [ecrane/bi5-bugetare/](ecrane/bi5-bugetare/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Bugetul (principal) | BUG 2027/01 în întocmire: grila conturi × luni pentru CC-LOG, secțiunile și starea lor, comentariile | `bi5-01-buget-intocmire` |
| Plan vs realizat | BUG 2026/02 la 31.10: tabelul din §4, cu abaterile și desfășurarea la transport | `bi5-02-plan-vs-realizat` |
| Avertismentul de control bugetar | Comanda de transport din 03.11 cu depășirea și motivul | `bi5-03-control-bugetar` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| BI5-A1 | Realizatul pe CC-LOG / 624 la 31.10 dă 1.098.600, la fel ca balanța C5 pe același filtru | Test de integrare |
| BI5-A2 | Aprobarea BUG 2026/02 trece BUG 2026/01 în „Înlocuit” | Test de integrare |
| BI5-A3 | Bugetul nu se trimite cu o secțiune netrimisă (`62610`) | Test de integrare |
| BI5-A4 | Comanda de 41.200 pe CC-LOG / 624 produce avertismentul cu depășirea corectă | Test de integrare pe `IBudgetCheck` |
| BI5-A5 | Fără pachetul BI, A3 confirmă comenzile fără niciun avertisment | Test de integrare |
| BI5-A6 | Importul Excel cu un cont de bilanț e refuzat pe rând (`62630`) | Test de integrare |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Unde stă bugetarea | În 02.13 (BI5), cerută de F5, V7, CR3 și M3 | ✓ Aprobat autonom (27.11.2026) |
| 2 | Pe ce se bugetează | BVC pe cont × centru de cost (× proiect) × lună; vânzările pe agent × categorie × lună | ✓ Aprobat autonom |
| 3 | Realizatul | Citit mereu din contabilitate (`IBalanceQuery`) și din vânzări, niciodată copiat | ✓ Aprobat autonom |
| 4 | Versiunile | Inițial, rectificări, prognoză de an; un singur aprobat activ | ✓ Aprobat autonom |
| 5 | Întocmirea | Pe secțiuni, pe responsabilii centrelor de cost, cu aprobare în fluxul Nucleu | ✓ Aprobat autonom |
| 6 | Controlul bugetar | Avertisment implicit, aprobare opțional, fără blocare | ✓ Aprobat autonom |
| 7 | Legăturile | Țintele BI4, `SalesAgentTarget`, prognoza F5 opțional | ✓ Aprobat autonom |
