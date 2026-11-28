# ERP · 02.13 Raportare și BI · BI4 Tablouri de bord, KPI, ținte și alerte

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)**: adoptat pe recomandare, după analiza producătorilor ([INSPIRATIE_PRODUCATORI.md](INSPIRATIE_PRODUCATORI.md), P5, P6, P16, P17, P20). A patra din cele 5 părți ale modulului 02.13.

Plaja de erori BI4: `62450–62599`. Documente părinte:

- [BI1](BI1-CONFIGURARE-CATALOG.md): măsurile, cu `GoodDirection`, și securitatea;
- [BI2](BI2-DEPOZIT-DATE.md): `IReportingQueryExecutor` și instantaneele;
- [BI3](BI3-RAPOARTE-ANALIZE.md): analizele salvate;
- [Nucleu](../01-nucleu/NUCLEU.md): ecranul „Acasă” pe rol cu widget-uri contribuite de module, notificările, cheile de dispozitiv;
- [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- Ecranul **„Acasă”** din Nucleu (validat pe 14.11.2026) e pe rol și are widget-uri contribuite de module. BI4 **nu îl înlocuiește**: îi dă widget-urile de tip KPI și grafic, construite pe stratul semantic, și adaugă tablourile de bord complete, cu filtre globale.
- Un **tablou** e o pagină cu widget-uri pe o grilă de 12 coloane. Fiecare widget e o interogare semantică (BI2) sau o analiză salvată (BI3), plus o vizualizare.
- Un **KPI** e o măsură cu o țintă (pe perioadă și, opțional, pe o dimensiune) și cu praguri verde / galben / roșu.
- O **alertă** e o regulă evaluată periodic, de forma „măsura M pe filtrul F trece pragul P”. Trimite o notificare în aplicație și pe e-mail, cu linkul spre analiză.

## 1. Scop și perimetru

| Zonă | Ce face BI4 | Ce NU face |
| --- | --- | --- |
| Tablouri | Crearea, editarea pe grilă, filtrele globale (perioada, firma, dimensiunile), desfășurarea, partajarea, tablourile livrate pe rol | Ecranul „Acasă” (Nucleu; BI4 îi dă widget-uri) |
| Widget-uri | Cartonaș KPI, grafic, tabel, listă „top”, semafor, indicator de progres față de țintă, text | — |
| Ținte | Ținte pe măsură, perioadă și dimensiune; praguri; importul țintelor din buget (BI5) | Bugetul (BI5) |
| Alerte | Reguli pe prag, pe variație și pe „lipsă de date”; frecvență; destinatari; jurnal | Notificările (Nucleu) |
| Ecran mare | Modul kiosk pentru televizoarele din depozit și din biroul de vânzări | — |

## 2. Model de domeniu

```
 rpt.Dashboard (Code, Name, OwnerUserId | IsDelivered, SharedWithRoleIds, GlobalFiltersJson, RefreshSeconds)
   └─ rpt.DashboardWidget × N (Type, GridX, GridY, W, H, QueryJson | ReportViewId, VisualJson, KpiTargetId?)
 rpt.KpiTarget (MeasureCode, PeriodKind, PeriodKey, DimensionFilterJson, TargetValue, GreenFrom, AmberFrom, Source = manual | buget)
 rpt.AlertRule (MeasureCode, FilterJson, Condition, Threshold, Frequency, Recipients, IsActive)
   └─ rpt.AlertEvent × N (EvaluatedAt, Value, Triggered, NotificationIds)
 rpt.KioskScreen (DashboardId, DeviceKeyId, RotateSeconds)
```

Reguli de business:

1. **Fiecare widget rulează cu drepturile celui care se uită** (ca analizele, BI3 regula 5). Un tablou partajat poate arăta cifre diferite pentru utilizatori diferiți. Widget-urile cu măsuri sensibile dispar pentru cine nu are dreptul, iar grila se reorganizează.
2. **Tablourile livrate** (`IsDelivered = 1`) nu se modifică. Utilizatorul le poate **copia** și modifica copia (`62451`). La o actualizare de versiune, tablourile livrate se actualizează, iar copiile rămân neatinse.
3. **Semaforul** folosește `GoodDirection` (BI1): pentru „mai mult e mai bine”, verde de la `GreenFrom` % din țintă, galben de la `AmberFrom` %, roșu sub. Pentru „mai puțin e mai bine”, invers.
4. **Ținta perioadei curente se proratează pe zilele lucrătoare scurse** când KPI-ul e afișat „la zi”. Exemplu: pe 27.11 sunt scurse 19 din 20 de zile lucrătoare ale lunii, deci ținta la zi e 95 % din ținta lunii.
5. **O alertă nu se repetă** pentru aceeași condiție până nu iese din condiție și reintră. Excepție: `RepeatEvery`, setat explicit.
6. **Reîmprospătarea:** implicit la deschidere; în modul kiosk la `RefreshSeconds` (minimum 60, `62460`). Widget-urile folosesc cache-ul BI2.

## 3. Detalii

### 3.1 Tipurile de widget

| Tip | Ce arată | Opțiuni |
| --- | --- | --- |
| Cartonaș KPI | Valoarea, ținta, abaterea, semaforul, o linie mică de tendință pe 12 perioade | Comparația cu anul anterior sau cu perioada anterioară |
| Grafic | Orice grafic din BI3 (ECharts) | Legenda, axa dublă, eticheta valorilor |
| Tabel | Pivot sau listă, cu formatare condiționată | Top N, desfășurare |
| Progres | Bara de realizare față de țintă, pe dimensiune (de exemplu pe agent) | Linia țintei proratate |
| Hartă | Valoarea pe județe | Scala de culori |
| Text | Notă sau titlu de secțiune (Markdown simplu) | — |
| Listă de lucru | Rânduri din rapoartele modulelor cu `SendOnlyIfRows` (de exemplu clienții peste limita de credit) | Linkul spre document |

### 3.2 Tablourile livrate pe rol (P16)

| Tablou | Rol | Widget-uri principale |
| --- | --- | --- |
| Directorul general | Director general | Cifra de afaceri a grupului față de buget și anul anterior, marja, EBITDA (din balanță), disponibilul, creanțele restante, stocul și rotația, pipeline-ul CRM ponderat, primele 5 abateri de la buget |
| Directorul economic | Director economic, Contabil șef | Rezultatul pe lună, cheltuielile pe centre de cost față de buget, DSO și DPO, creanțele pe vechime, TVA de plată, cash-flow-ul prognozat (F5), închiderea lunii (C3) |
| Directorul comercial | Șef vânzări | Vânzările pe agent față de țintă, marja pe categorie, comenzile nelivrate, nivelul de servire, clienții în risc, pipeline-ul (CR5) |
| Achiziții | Șef achiziții | Achizițiile pe furnizor, livrările la timp, comenzile întârziate, prețurile față de ultima achiziție |
| Depozitul | Șef depozit | Stocul pe gestiune, stocul sub minim, stocul fără mișcare, livrările zilei, retururile (variantă kiosk) |
| Grupul | Director general | KPI-urile pe firme, una lângă alta |

### 3.3 Țintele (P20)

Țintele se introduc manual, pe măsură, perioadă (lună, trimestru, an) și dimensiune (firmă, agent, categorie), sau se **preiau din buget** (BI5: bugetul aprobat devine țintă, `Source = buget`). Șeful de vânzări poate introduce ținta lunară pe agent și din V1 (`sales.SalesAgentTarget`, precizarea din CRM), ținta fiind aceeași.

### 3.4 Alertele (P6)

| Condiție | Exemplu |
| --- | --- |
| Prag | Creanțele restante peste 90 de zile > 150.000 lei pe firmă |
| Variație | Cifra de afaceri a săptămânii < 80 % din media ultimelor 8 săptămâni |
| Față de țintă | Realizarea la zi < 85 % din ținta proratată, după ziua 10 a lunii |
| Lipsă de date | Nicio factură emisă azi până la ora 14:00, într-o zi lucrătoare (problemă de operare sau de e-Factura) |

Jobul `RptAlerts` evaluează alertele după frecvența lor (orar, zilnic la o oră, săptămânal), cu drepturile destinatarului. Fiecare destinatar primește doar dacă alerta se declanșează pe datele pe care le vede. Notificarea are valoarea, pragul, linkul spre analiza desfășurată și butonul „Nu mai trimite”, care dezactivează doar pentru acel destinatar.

**Abonarea și suspendarea (P29, adăugat pe 28.11.2026):**

- O alertă marcată „Vizibilă și altora” apare în lista de alerte a celor care au dreptul pe măsură. Ei se pot adăuga singuri cu „Abonează-mă”, fără să o copieze. Proprietarul vede abonații.
- După 3 evaluări eșuate la rând (setul de date nu mai e accesibil, măsura a fost retrasă, proprietarul și-a pierdut dreptul), alerta trece în starea „Suspendată” (`62482`) și proprietarul primește o notificare. „Reia alerta” o reevaluează imediat; dacă reușește, revine la „Activă”.
- Exemplu: alerta lui Radu Stan „Pipeline ponderat < 250.000 lei” s-a suspendat pe 26.11, după ce rolul lui CRM a fost înlocuit temporar.

### 3.5 Ecranul mare (P17)

Un televizor din depozit (un mini-PC cu browser) folosește o **cheie de dispozitiv** din Nucleu (tip „Ecran”, legată de un tablou și de o firmă, doar citire). Pagina `/kiosk/<cod>`:

- rotește tablourile la `RotateSeconds`;
- reîmprospătează datele;
- are scris mare și contrast ridicat;
- nu are meniu și nu permite desfășurarea.

Cheia se revocă din administrare. Accesul se jurnalizează.

### 3.6 KPI urmărite, rezumatul periodic și explicațiile (P27, P28, adăugat pe 28.11.2026)

Orice cartonaș KPI are butonul „Urmărește”. KPI-ul urmărit (măsura + filtrele + ținta) apare în pagina „KPI urmărite” și, la alegere, într-un **rezumat periodic** (zilnic la 07:00 sau luni la 07:00), trimis pe e-mail și în notificări de jobul `RptKpiDigest`. Rezumatul are, pe fiecare KPI: valoarea, variația, semaforul și cea mai importantă explicație. Un utilizator urmărește cel mult 30 de KPI-uri (`62490`).

Panoul **„De ce?”** al KPI-ului are cinci explicații, calculate în SQL pe setul de date, cu drepturile celui care se uită. Textul e fix pe tip de explicație, nu e generat de un model de limbaj:

| Explicație | Calcul | Când apare |
| --- | --- | --- |
| Variația | Față de perioada anterioară și față de aceeași perioadă a anului trecut, la același număr de zile lucrătoare | Mereu |
| Contribuitorii | Primii 3 membri ai dimensiunii principale după contribuția la variație (pozitivă și negativă) | Mereu |
| Valoare neașteptată | Valoarea unui membru e în afara benzii „media ± 2 abateri standard” pe ultimele 12 perioade | Doar dacă există |
| Concentrarea | Cel mult 3 membri fac cel puțin 50 % din valoare | Doar dacă există |
| Ritmul față de țintă | Proiecția la finalul perioadei, după zilele lucrătoare scurse, față de țintă | Doar pentru KPI-urile cu țintă |

Exemplu pe 27.11, George Marinescu urmărește „Cifra de afaceri a grupului” și „Creanțe restante peste 30 de zile”, cu rezumat luni la 07:00:

- **Variația:** 5.602.900 lei, +7,4 % față de 5.218.400 lei în aceeași perioadă din noiembrie 2025.
- **Contribuitorii:** Exemplu Distribuție +241.300 lei, Exemplu Construct +118.900 lei, Exemplu Producție (vânzări externe) +24.300 lei.
- **Valoare neașteptată:** vânzările externe ale Exemplu Producție, 854.100 lei, sunt sub banda obișnuită pe 12 luni (880.000–1.040.000 lei).
- **Concentrarea:** la creanțele peste 30 de zile, Cafe Unirii, Hyper Nord și Hotel Central fac 226.100 lei, adică 54 % din 418.300 lei.
- **Ritmul față de țintă:** în ritmul actual, luna se încheie la 5.897.800 lei, 95,1 % din ținta de 6.200.000 lei (lipsesc 302.200 lei).

### 3.7 Interacțiunea între widget-uri (P30, adăugat pe 28.11.2026)

Pe tablou, clicul pe o bară, o felie sau un rând devine **filtru global temporar** pentru toate widget-urile care au acea dimensiune. Filtrul apare în bara de filtre, iar al doilea clic sau „×” îl scoate. Widget-urile fără dimensiunea respectivă rămân neschimbate și primesc nota „nefiltrat”. Desfășurarea până la document rămâne în meniul contextual („Arată documentele”). Exemplu: George apasă pe bara „Construct” și vede tot tabloul doar pentru Exemplu Construct. Abaterile de la buget arată atunci „Materiale șantier” și „Subcontractori”.

## 4. Exemplu: tabloul Directorului general, 27.11.2026

George Marinescu, directorul general al Grupului Exemplu, deschide la 07:30 tabloul livrat, cu filtrul global „Grup · noiembrie 2026 · la zi”. Situația pe 27.11:

| KPI | Valoare | Ținta la zi | Semafor |
| --- | --- | --- | --- |
| Cifra de afaceri a grupului | 5.602.900 lei (fără tranzacțiile din grup) | 5.890.000 = 95 % din 6.200.000 (19 din 20 de zile lucrătoare) | 95,1 %: galben (verde de la 98 %, galben de la 90 %) |
| Marja brută | 24,8 % | 25,5 % | Galben |
| Creanțe restante peste 30 de zile | 418.300 lei | maximum 350.000 | Roșu |
| Disponibil | 2.946.700 lei | — | — |
| Stoc | 4.812.000 lei, rotație 41 de zile | maximum 45 de zile | Verde |
| Pipeline CRM ponderat (Exemplu Distribuție) | 321.095 lei | — | — |

Primele abateri de la buget (BI5): Exemplu Construct, „Materiale șantier”, +18,4 %; Exemplu Distribuție, „Transport”, +12,1 %.

**Alerta „Creanțe restante > 150.000 lei pe firmă”** s-a declanșat pe 26.11 pentru Exemplu Distribuție (236.900 lei). Ion Popescu și George Marinescu au primit notificarea, cu linkul spre creanțele pe vechime desfășurate pe client. Primul din listă e Cafe Unirii, cu 41 de zile.

## 5. Evenimente

| Eveniment | Direcție | Folosire |
| --- | --- | --- |
| `BugetAprobat` v1 | Consumat (BI5) | Generează sau actualizează țintele cu `Source = buget` |
| `AlertaDeclansata` v1 | Publicat (intern, spre notificările Nucleu) | Notificarea și e-mailul |

## 6. Contracte publice

```csharp
public interface IHomeWidgetProvider        // contractul Nucleu pentru ecranul „Acasă”; BI4 îl implementează pentru widget-urile KPI
{
    IEnumerable<HomeWidgetDescriptor> GetWidgets(int userId);
}
public interface IKpiTargetQuery            // folosit de CR3 (prognoza) și de V7 pentru țintele comune
{
    Task<decimal?> GetTargetAsync(string measureCode, string periodKey, string dimensionFilterJson, CancellationToken ct);
}
```

## 7. Schema SQL (partea BI4)

| Tabel | Cheie primară | Chei unice / indexuri |
| --- | --- | --- |
| `rpt.Dashboard` | `Id int` | UX `(Code)` pentru cele livrate; IX `(OwnerUserId)` |
| `rpt.DashboardWidget` | `Id int` | FK `DashboardId`; `CHECK (W BETWEEN 1 AND 12)` |
| `rpt.KpiTarget` | `Id int` | UX `(MeasureCode, PeriodKind, PeriodKey, DimensionFilterHash)` |
| `rpt.AlertRule`, `rpt.AlertRecipient`, `rpt.AlertEvent` | `Id int` / `bigint` | IX `(IsActive, NextEvalAt)` |
| `rpt.KioskScreen` | `Id int` | FK `DeviceKeyId` (Nucleu, cheie API de tip „Ecran”) |
| `rpt.KpiFollow` (nou, 28.11.2026) | `Id int` | UX `(UserId, MeasureCode, FilterHash)`; coloanele `FilterJson`, `TargetId`, `Digest` (0 fără, 1 zilnic, 2 săptămânal), `LastSentAt` |

`rpt.AlertRule` primește `Status` (1 Activă, 2 Suspendată), `FailCount`, `IsShared`; `rpt.AlertRecipient` primește `IsSubscriber` (abonat singur).

**Erorile BI4** (`62450–62599`):

- `62451` `RPT_DELIVERED_DASHBOARD_READONLY`
- `62452` `RPT_WIDGET_MEASURE_FORBIDDEN`: widget ascuns
- `62460` `RPT_KIOSK_REFRESH_TOO_LOW`
- `62470` `RPT_TARGET_DUPLICATE`
- `62480` `RPT_ALERT_RECIPIENT_FORBIDDEN`
- `62481` `RPT_ALERT_EVAL_FAILED`: notifică autorul alertei
- `62482` `RPT_ALERT_SUSPENDED`: după 3 eșecuri la rând
- `62490` `RPT_KPI_FOLLOW_LIMIT`: cel mult 30 de KPI urmărite

## 8. Permisiuni

- `Rpt.Dashboard.View` (tablourile livrate rolului);
- `Rpt.Dashboard.Create` și `.Share` (pachetul BI);
- `Rpt.Target.Manage`;
- `Rpt.Alert.Manage`;
- `Rpt.Kiosk.Manage` (administratorul).

## 9. Integrări

- **Nucleu:**
  - widget-urile pe „Acasă” (`IHomeWidgetProvider`);
  - cheia de dispozitiv de tip „Ecran”;
  - notificările.
- **V1 / CRM:** țintele agenților.
- **BI5:** țintele din buget.

## 10. Rapoarte

„Alerte declanșate” (pe regulă, destinatar, perioadă), „Utilizarea tablourilor”.

## 11. Ecrane principale

Machetele sunt în [ecrane/bi4-tablouri/](ecrane/bi4-tablouri/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Tabloul Directorului general (principal) | Situația din §4: KPI-urile cu semafor, cifra de afaceri pe firme, tendința, abaterile, creanțele | `bi4-01-tablou-director-general` |
| Editarea tabloului | Grila, biblioteca de widget-uri, proprietățile widget-ului KPI | `bi4-02-editare-tablou` |
| Alerte | Regulile, ultimele declanșări, definiția alertei de creanțe | `bi4-03-alerte` |
| Ecranul mare din depozit | Tabloul „Depozitul” în modul kiosk | `bi4-04-ecran-depozit` |
| KPI urmărit (nou, 28.11.2026) | „Cifra de afaceri a grupului” urmărită de George Marinescu: panoul „De ce?” cu cele cinci explicații din §3.6, setările rezumatului | `bi4-05-kpi-urmarit` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| BI4-A1 | Ținta proratată pe 27.11 e 95 % din ținta lunii (19 din 20 de zile lucrătoare) | Test unitar pe calendar |
| BI4-A2 | Semaforul „mai puțin e mai bine” (creanțe) e roșu peste țintă | Test unitar |
| BI4-A3 | Tabloul partajat arată agentului doar clienții lui; widget-ul de marjă dispare fără drept | Test de integrare |
| BI4-A4 | Alerta nu se retrimite cât timp condiția rămâne îndeplinită | Test de integrare |
| BI4-A5 | Cheia de ecran revocată oprește pagina kiosk la următoarea reîmprospătare | Test de integrare |
| BI4-A6 | Tabloul livrat nu se poate modifica, doar copia (`62451`) | Test de integrare |
| BI4-A7 | Proiecția pe 27.11 a cifrei de afaceri a grupului e 5.897.800 lei (5.602.900 / 19 × 20) | Test unitar |
| BI4-A8 | Alerta cu 3 evaluări eșuate la rând trece în „Suspendată” și „Reia alerta” o reactivează | Test de integrare |
| BI4-A9 | Explicația „Concentrarea” apare doar când cel mult 3 membri fac cel puțin 50 % | Test unitar |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | Relația cu „Acasă” | „Acasă” rămâne în Nucleu; BI4 îi dă widget-uri și are tablourile complete | ✓ Aprobat autonom (27.11.2026) |
| 2 | Tablourile livrate | Pe rol, nemodificabile, copiabile | ✓ Aprobat autonom |
| 3 | Țintele | Manuale sau din buget; proratate pe zile lucrătoare | ✓ Aprobat autonom |
| 4 | Alertele | Prag, variație, față de țintă, lipsă de date; evaluate cu drepturile destinatarului | ✓ Aprobat autonom |
| 5 | Ecranul mare | Mod kiosk cu cheie de dispozitiv, doar citire | ✓ Aprobat autonom |
| 6 | KPI urmărite (Tableau Pulse) | Da, cu rezumat zilnic sau săptămânal pe e-mail și în notificări | ✓ Aprobat autonom (28.11.2026) |
| 7 | Explicațiile „De ce?” | Cinci explicații calculate în SQL, cu text fix; fără model de limbaj | ✓ Aprobat autonom (28.11.2026) |
| 8 | Abonarea și suspendarea alertelor (Tableau) | „Abonează-mă” pe alertele vizibile; suspendare după 3 eșecuri, reluare de proprietar | ✓ Aprobat autonom (28.11.2026) |
| 9 | Clicul în widget (Tableau) | Filtru global temporar; desfășurarea rămâne în meniul contextual | ✓ Aprobat autonom (28.11.2026) |
