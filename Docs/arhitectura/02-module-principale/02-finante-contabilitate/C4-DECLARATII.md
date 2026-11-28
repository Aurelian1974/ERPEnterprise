# ERP · 02.02 Contabilitate · C4 Declarații

15 noiembrie 2026 · Aurelian · **✓ Validat (15.11.2026)**, cu ecranele și toate cele 7 decizii. Pornește de la deciziile deja luate în C1–C3: TVA-ul are cota și categoria pe fiecare componentă contată (C2), conturile au contul standard SAF-T (C1), iar nota de regularizare TVA de la închiderea lunii trebuie să fie egală cu D300 calculat aici (C3).

A patra din cele 5 sub-părți ale părții contabile a modulului 02.02 Finanțe și contabilitate (schema SQL `fin`, prefix de cod `Erp.Finance`). Acoperă registrul de TVA și jurnalele de vânzări și cumpărări, decontul de TVA (D300) și comparația cu decontul precompletat RO e-TVA (P300), declarațiile informative D394 și D390, fișierul SAF-T (D406), impozitul pe profit (fișa de calcul, D100 trimestrial, D101 anual), impozitul microîntreprinderii, calendarul declarațiilor, rectificativele și legătura cu plățile. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md), partea financiară [F1](F1-CASIERIE.md)–[F5](F5-PLANIFICARE-TREZORERIE.md), [C1](C1-PLANURI-DE-CONTURI.md), [C2](C2-MODELE-DE-CONTARE-SI-NOTE.md), [C3](C3-INCHIDERI.md). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- Declarațiile sunt **pe firmă**. Periodicitatea (lunar, trimestrial) și regimul (plătitor de TVA, TVA la încasare, impozit pe profit sau microîntreprindere) vin din datele firmei în Nucleu, cu dată de la care se aplică.
- **C4 calculează și generează; modulul Integrări (02.14) vorbește cu ANAF.** C4 produce datele declarației și cere Integrărilor fișierul XML după schema ANAF, validarea și, unde există, descărcarea din SPV (P300). Schemele XML și versiunile formularelor se livrează cu actualizările produsului.
- Formularele ANAF se schimbă des (rânduri noi, cote noi). Tot ce ține de un formular (rânduri, reguli de calcul, schema) e **versionat pe dată**: o declarație se calculează cu versiunea formularului în vigoare pentru perioada ei (P6).
- Cotele de TVA din exemple sunt cele în vigoare în 2026: 21% standard și 11% redusă.
- Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`): sumele din declarații se rotunjesc la leu întreg doar la generarea formularului, după regula ANAF; registrul de TVA rămâne la 2 zecimale.
- **Ce NU face C4**: nu face notele contabile (C2), nu închide lunile (C3), nu face balanța și situațiile financiare (C5), nu calculează salariile și declarația D112 (HR).

## 1. Scop și perimetru

| Zonă | Ce face C4 | Ce NU face (și cine face) |
| --- | --- | --- |
| Registrul de TVA | Un rând pe document, cotă și tip de operațiune, scris la contare; sursa unică pentru jurnalele de TVA și toate declarațiile de TVA | Calculul TVA-ului pe document (modulul sursă) |
| Jurnale de TVA | Jurnalul de vânzări și jurnalul de cumpărări, pe lună, în forma cerută de Codul fiscal | — |
| D300 și RO e-TVA | Decontul de TVA; preluarea decontului precompletat P300 și explicarea diferențelor | Descărcarea P300 din SPV (Integrări) |
| D394, D390 | Declarația informativă pentru operațiunile din țară și declarația recapitulativă pentru cele intracomunitare | — |
| D406 SAF-T | Fișierul lunar sau trimestrial (registru, parteneri, facturi, plăți), cel anual pentru active și cel la cerere pentru stocuri | Datele de active (Mijloace fixe) și de stoc (Stocuri), cerute prin contract |
| Impozit pe profit | Fișa de calcul trimestrială și anuală, nota de impozit, D100 trimestrial, D101 anual | Repartizarea profitului (C3 și C2) |
| Impozitul microîntreprinderii | Baza (veniturile), cota, D100 | — |
| Calendar și depunere | Termenele fiecărei firme, starea fiecărei declarații, fișierele, recipisele, rectificativele, obligațiile de plată trimise spre Planificare (F5) | Semnarea și depunerea pe portalul ANAF (utilizatorul, decizia 1) |

## 2. Model de domeniu

```
 Eveniment contat de C2 (componente Net și Vat, cu cotă și categorie)
          │  în aceeași tranzacție cu nota
          ▼
 VatEntry (document · cotă · tip de operațiune · bază · TVA · dată de exigibilitate)
          │                         ▲
          │                         └─ încasări și plăți cu TVA la încasare (F3) → rânduri de exigibilitate
          ▼
 Jurnale de TVA ──► TaxReturn D300 ──► comparație cu P300 (RO e-TVA)
                 ├► TaxReturn D394, D390
                 │
 JournalLine (C2) + parteneri + documente ──► TaxReturn D406 (SAF-T)
 Balanța (conturi 6, 7, TaxTreatment) ──► ProfitTaxComputation ──► TaxReturn D100, D101
                                                                    │
 TaxReturn ──► TaxReturnFile (XML, PDF, recipisă) ──► TaxObligation (sumă, scadență) ──► F5
```

Reguli de business ale modelului:

1. **O singură sursă pentru TVA.** Jurnalele de TVA, D300, D394, D390 și secțiunile de TVA din SAF-T se calculează din același registru (`VatEntry`), nu fiecare din altă parte. Registrul se scrie de C2, în aceeași tranzacție cu nota contabilă, deci nu poate diferi de conturile `4426`, `4427`, `4428` (decizia 2).
2. **Tipul de operațiune decide rândul din declarație.** Fiecare rând din registru are un tip de operațiune fiscală (`VatOperationType`, 3.2), din care rezultă rândul D300, secțiunea D394, codul D390 și codul de taxă SAF-T. Nicio declarație nu ghicește rândul din cont.
3. **Exigibilitatea decide perioada.** Un rând intră în declarația lunii în care TVA-ul devine exigibil: data facturii, de regulă; data încasării sau a plății, pentru TVA la încasare (F3, 3.7).
4. **Fișierul final se generează doar pe o lună închisă** (C3), ca cifrele declarate să nu se mai schimbe. Calculul în ciornă se poate face oricând (decizia 3).
5. **Rândurile calculate nu se editează.** Rândurile care vin din registru nu au câmp de editare; o corecție se face prin document sau notă. Doar rândurile pur declarative (pro-rata, regularizări cerute de lege, sume din perioade anterioare) se completează manual, cu motiv (decizia 4).
6. **O declarație depusă nu se modifică.** Corecția e o declarație rectificativă, legată de cea inițială, cu diferențele afișate.
7. **Fiecare declarație cu sumă de plată creează o obligație de plată** cu scadența legală, trimisă în Planificare (F5) ca sumă planificată (decizia 7).

## 3. Entități, documente și ciclul lor de viață

Convențiile de coloane, tipuri și chei sunt cele din F1, secțiunea 3.

### 3.1 Registrul de TVA (`VatEntry`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `EntryId` (nota din C2), `SourceDocumentType`, `SourceDocumentId`, `DocumentNumber`, `DocumentDate` | Legătura cu documentul și cu nota |
| `Direction` | 1 Livrare (vânzare), 2 Achiziție |
| `PartnerId`, `PartnerFiscalCode`, `PartnerCountryCode`, `PartnerName` | Copiate la data documentului (datele declarate nu se schimbă dacă partenerul se modifică mai târziu) |
| `OperationTypeCode` | Tipul de operațiune fiscală (3.2) |
| `VatRateCode`, `VatRatePercent` | Cota (din nomenclatorul Nucleului, cu data de vigoare) |
| `BaseRon`, `VatRon`, `BaseCurrency`, `CurrencyCode` | Baza și TVA-ul; în valută pentru operațiunile intracomunitare |
| `ExigibilityDate`, `PeriodYear`, `PeriodMonth` | Perioada declarației (regula 3) |
| `IsVatOnCollection`, `ParentVatEntryId` | Pentru TVA la încasare: rândul facturii are TVA-ul neexigibil; fiecare încasare sau plată creează un rând copil cu partea exigibilă |
| `DeductionPercent` | 100 implicit; mai puțin pentru cheltuielile cu deducere limitată (de exemplu 50% la autoturisme) |
| `IsReversal` | Rândurile stornate (storno în roșu, cu semn) |
| `EInvoiceStatus` | Pentru comparația cu RO e-TVA: factura e în e-Factura (trimisă sau primită) sau nu |

Rândurile se scriu de C2 din componentele `Net` și `Vat` ale evenimentului (C2, 3.1): pentru fiecare combinație cotă × categorie × tip de operațiune, un rând. Tipul de operațiune se determină din categorie, sensul documentului, țara și tipul partenerului, tipul articolului (bunuri sau servicii) și marcajele documentului (taxare inversă, import, regim special).

### 3.2 Tipuri de operațiuni fiscale (`VatOperationType`)

Nomenclator livrat, versionat pe dată, needitabil de client (vine cu actualizările). Exemple:

| Cod | Operațiune | D300 | D394 | D390 | SAF-T |
| --- | --- | --- | --- | --- | --- |
| `L-S21` | Livrare în țară, cota 21% | Rândul livrărilor cu cota standard | Secțiunea L (livrări) | — | Codul de taxă pentru 21% |
| `L-S11` | Livrare în țară, cota 11% | Rândul cotei reduse | L | — | 11% |
| `L-IC` | Livrare intracomunitară scutită (art. 294 alin. 2) | Rândul livrărilor intracomunitare | — | `L` | Scutit cu drept de deducere |
| `L-AE` | Livrare în țară cu taxare inversă (art. 331) | Rândul taxării inverse | V | — | Taxare inversă |
| `L-EXP` | Export | Rândul exporturilor | — | — | Scutit |
| `A-S21`, `A-S11` | Achiziție în țară | Rândurile TVA deductibile | A (achiziții) | — | 21%, 11% |
| `A-IC-B`, `A-IC-S` | Achiziție intracomunitară de bunuri, de servicii (taxare inversă) | TVA colectată și deductibilă, pe rândurile lor | — | `A`, `P` | Taxare inversă |
| `A-AE` | Achiziție în țară cu taxare inversă | TVA colectată și deductibilă | C | — | Taxare inversă |
| `A-IMP` | Import cu plata TVA în vamă sau cu certificat de amânare | Rândurile de import | — | — | Import |
| `A-NOD` | Achiziție fără drept de deducere | — (doar jurnalul) | A | — | Nedeductibil |

Rândurile exacte ale formularelor sunt în `TaxFormRowMapping` (versiunea formularului × tipul de operațiune × coloană), livrată cu produsul. Tabelul de mai sus e doar ilustrativ; codul nu presupune numere de rând.

### 3.3 Declarație (`TaxReturn`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `FormCode`, `FormVersion` | `D300`, `D394`, `D390`, `D406`, `D406A` (active), `D406S` (stocuri), `D100`, `D101`; versiunea în vigoare pentru perioadă |
| `PeriodYear`, `PeriodFrom`, `PeriodTo` | Lună, trimestru, semestru sau an |
| `Kind`, `CorrectsReturnId` | 1 Inițială, 2 Rectificativă (legată de cea pe care o corectează) |
| `Status` | 1 Ciornă, 2 Verificată, 3 Generată (XML valid), 4 Depusă, 5 Înlocuită (de o rectificativă) |
| `DueDate` | Termenul legal, din calendarul fiscal (3.7) |
| `CalculatedAt`, `CalculatedBy`, `CalculationHash` | La generare se verifică faptul că datele nu s-au schimbat de la calcul |
| `SubmittedAt`, `SubmittedBy`, `RegistrationNumber`, `UploadIndex` | Din recipisa ANAF |
| `AmountDue`, `AmountRefundable` | Suma de plată sau de rambursat (D300, D100) |

**Linii** (`TaxReturnLine`): `RowCode`, `Column` (bază, TVA, sumă), `CalculatedValue`, `ManualValue`, `ManualReason`, `SourceQuery` (cum s-a calculat, pentru detaliere). Pentru D394, D390 și D406 liniile sunt mii de rânduri pe partener sau pe document; ele stau în tabele proprii (`TaxReturnPartnerLine`, iar D406 direct în fișier), nu în `TaxReturnLine`.

**Fișiere** (`TaxReturnFile`): XML-ul generat, raportul validatorului, PDF-ul cu XML-ul atașat, recipisa. Stocate prin atașamentele Nucleului, cu păstrare 10 ani (02.01).

Ciclul de viață:

```
 Ciornă ──(verifică)──► Verificată ──(generează XML, lună închisă)──► Generată ──(recipisă)──► Depusă
   ▲                                                                                          │
   └──────────── recalculare (datele s-au schimbat) ◄─────────────────────────────────────────┤
                                                                       rectificativă nouă ◄───┘ (Înlocuită)
```

### 3.4 D300 și RO e-TVA

- D300 se calculează din registrul de TVA al perioadei (regula 1), pe rândurile versiunii de formular. Soldul de TVA de rambursat din perioada anterioară vine din D300 depus anterior.
- La închiderea lunii, C3 compară nota de regularizare (`4427`, `4426` → `4423` / `4424`) cu D300; diferența blochează închiderea (C3, 3.5).
- **RO e-TVA**: Integrări descarcă decontul precompletat P300 din SPV; C4 îl compară rând cu rând cu D300. Diferențele peste pragul legal (în 2026: cel puțin 20% și cel puțin 5.000 lei) sunt marcate; utilizatorul scrie explicația pe fiecare, pentru eventuala notificare de conformare. C4 arată și facturile care explică diferența: facturi primite în e-Factura și neînregistrate în ERP, facturi din ERP care nu sunt în e-Factura (decizia 5).

### 3.5 D406 SAF-T

| Fișier | Periodicitate | Conținut | Sursa |
| --- | --- | --- | --- |
| D406 lunar sau trimestrial | După categoria contribuabilului | Antet, nomenclatoare (conturi cu `SaftAccountCode`, parteneri, cote, articole, unități de măsură), registrul (`GeneralLedgerEntries`), facturi de vânzare și de cumpărare, plăți | C1, C2 (`JournalLine`), Nucleu, Vânzări, Achiziții, F1–F3 |
| D406 active | Anual | Mijloacele fixe și mișcările lor | Mijloace fixe |
| D406 stocuri | La cererea ANAF, pe perioada cerută | Stocurile și mișcările lor | Stocuri |

- Fișierul se scrie în flux (`XmlWriter`, citire paginată din proceduri, `NET-12`), fără să încarce totul în memorie; o lună cu 2 milioane de linii de notă nu trebuie să depășească 1 GB RAM.
- Verificările înainte de generare: conturile cu rulaj au cont SAF-T (C1), partenerii au cod fiscal valid sau codul implicit pentru persoane fizice, partenerii UE au cod de TVA, notele au numărul și jurnalul, documentele din registru au corespondent în facturi.
- Validarea se face cu validatorul oficial ANAF (DUKIntegrator, gratuit), rulat de Integrări pe serverul aplicației; erorile se afișează pe rând, cu legătura spre partenerul sau documentul greșit.
- Fișierul depășit ca mărime se împarte după regulile ANAF în vigoare (setare livrată, nu valoare în cod).

### 3.6 Impozitul pe profit (`ProfitTaxComputation`)

Fișa de calcul (registrul de evidență fiscală), pe firmă, pe trimestru (cumulat de la începutul anului) și pe an:

| Rând | Sursa |
| --- | --- |
| Venituri totale, cheltuieli totale | Rulajele conturilor din clasele 7 și 6, fără `691` și `698` |
| Rezultatul contabil | Diferența |
| + Cheltuieli nedeductibile | Conturile cu `TaxTreatment` = 2 (atribut nou în C1: `6588` amenzi, `6582` donații...); pentru `TaxTreatment` = 3, partea peste limită, calculată după regula limitei (protocol 2% din profitul calculat, ajutoare sociale 5% din salarii...) |
| − Venituri neimpozabile | Conturile cu `TaxTreatment` = 4 (dividende primite de la persoane juridice române, anularea provizioanelor nedeductibile) |
| ± Deduceri și elemente similare | Rânduri manuale, cu motiv (amortizarea fiscală diferită de cea contabilă, rezerva legală deductibilă) |
| − Pierderea fiscală de recuperat | Din D101 ale anilor anteriori, cu limitele legale |
| = Profit impozabil, × cota | Cota din Nucleu, cu dată de vigoare (16% în 2026) |
| − Credite fiscale | Sponsorizarea (limitele din Codul fiscal), impozitul plătit în străinătate |
| − Impozitul declarat în trimestrele anterioare | Din D100 depuse |
| = Impozitul trimestrului | Merge în D100 și în nota `691` = `4411` (prin C2) |

Rândurile calculate se pot detalia până la notele contabile. Regulile de limită sunt livrate și versionate pe dată, ca rândurile formularelor.

**Microîntreprinderi**: baza e venitul din conturile clasei 7, cu excepțiile legale (conturi cu `TaxTreatment` = 4), × cota (1% sau 3%, după condițiile din Nucleu), pe trimestru, în D100.

### 3.7 Calendarul fiscal (`TaxCalendarItem`) și obligațiile de plată (`TaxObligation`)

- Calendarul se generează pe an și firmă din regulile livrate: D300, D394, D390, D100 până pe 25 ale lunii următoare perioadei; D406 lunar până în ultima zi a lunii următoare; D101 până pe 25 martie (sau 25 a lunii a treia după încheierea exercițiului diferit de anul calendaristic). Termenele care cad în zile nelucrătoare se mută pe următoarea zi lucrătoare (calendarul Nucleului).
- Fiecare rând arată starea declarației (lipsă, ciornă, generată, depusă), cu notificare la 5 zile lucrătoare înainte de termen pentru cele nedepuse.
- `TaxObligation`: firmă, declarație, tip de buget (TVA, impozit pe profit, impozit pe dividende), sumă, scadență, cont de trezorerie ANAF; la depunerea declarației se publică `ObligatieFiscalaCreata`, consumat de F5 ca sumă planificată cu probabilitate 100%, iar plata ei (F2) o închide.

## 4. Fluxuri principale

### 4.1 Calculul și depunerea D300

1. Contabilul deschide D300 pentru firmă și lună. Sistemul calculează ciorna din registru (rândurile calculate, needitabile) și preia soldul de rambursat din D300 anterior.
2. Dacă Integrări a descărcat P300, ecranul arată coloana P300 și diferențele; contabilul explică diferențele peste prag.
3. „Verifică”: regulile formularului (totaluri, rânduri obligatorii, semne), corespondența cu soldurile `4426`, `4427`, `4428` din balanță.
4. După închiderea lunii (C3), „Generează XML”: C4 trimite datele la Integrări, care produce XML-ul după schema versiunii, îl validează cu DUKIntegrator și întoarce XML-ul, raportul și PDF-ul cu XML-ul atașat.
5. Utilizatorul semnează PDF-ul cu certificatul calificat și îl depune pe portalul ANAF (decizia 1), apoi încarcă recipisa sau scrie numărul de înregistrare. Declarația trece în Depusă, se publică `DeclaratieDepusa` și, dacă e TVA de plată, `ObligatieFiscalaCreata`.

### 4.2 D394 și D390

La fel ca D300, din același registru, grupat pe partener (cod fiscal) și tip de operațiune. Verificări specifice: partenerii din țară au CUI valid (cu verificarea ANAF, prin Integrări, la data facturii), partenerii UE au cod de TVA valid (VIES, prin Integrări), seriile și plajele de numere ale facturilor emise sunt complete.

### 4.3 D406 SAF-T

1. Contabilul pornește verificările pentru lună; ecranul arată problemele pe categorie (conturi, parteneri, documente), cu legătura spre ecranul care le rezolvă.
2. După închiderea lunii, „Generează”: job în fundal (Hangfire), cu progres; fișierul se validează cu DUKIntegrator.
3. Depunerea și recipisa ca la D300.

### 4.4 Impozitul pe profit trimestrial

1. După închiderea operativă a ultimei luni din trimestru (C3), contabilul deschide fișa de calcul; rândurile automate sunt calculate, cele manuale se completează cu motiv.
2. „Contează impozitul”: nota `691` = `4411` în ultima zi a trimestrului, prin C2 (luna e încă doar închisă operativ, deci acceptă note).
3. D100 se generează, se depune ca D300, iar obligația merge în F5.

### 4.5 Rectificativa

Când o lună cu declarații depuse se redeschide (C3, 4.2), declarațiile ei primesc marcajul „De verificat”. După reînchidere, C4 recalculează și arată diferențele față de declarația depusă; dacă există diferențe, utilizatorul creează rectificativa (pentru D300, conform regulilor legale în vigoare; dacă legea cere corecția în decontul următor, sistemul propune rândul de regularizare din perioada curentă). Dacă nu există diferențe, marcajul se șterge.

### 4.6 Cazuri limită

| Situație | Comportament |
| --- | --- |
| Factură cu TVA la încasare, încasată în trei tranșe în luni diferite | Rândul facturii are TVA-ul neexigibil (nu intră în D300); fiecare încasare creează un rând exigibil în luna ei |
| Schimbarea cotei de TVA în timpul anului | Documentele folosesc cota de la data faptului generator; registrul păstrează cota și procentul; D300 le pune pe rândurile versiunii de formular a perioadei |
| Formular nou publicat de ANAF | Livrat printr-o actualizare a produsului (rânduri, mapare, schemă), cu dată de vigoare; declarațiile depuse rămân pe versiunea lor |
| Partener care și-a schimbat CUI-ul după factură | Declarația folosește datele copiate la data documentului (3.1) |
| Factură de furnizor în e-Factura, neînregistrată în ERP | Apare în comparația cu P300 ca explicație a diferenței, cu legătura spre preluarea ei în Achiziții |
| Lună redeschisă după depunerea D300 | Declarația „De verificat”; rectificativă doar dacă cifrele s-au schimbat (4.5) |
| Firmă care trece de la TVA lunar la trimestrial | Periodicitatea din Nucleu are dată de vigoare; calendarul se regenerează de la acea dată |
| DUKIntegrator indisponibil pe server | Generarea se oprește cu eroare clară (`51953`); XML-ul nevalidat nu se poate marca depus |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Payload | Consumatori |
| --- | --- | --- | --- |
| `DeclaratieGenerata` v1 | XML generat și validat | `ReturnId`, `CompanyId`, `FormCode`, perioada | Notificări |
| `DeclaratieDepusa` v1 | Recipisa înregistrată | La fel + `RegistrationNumber`, `AmountDue` | Raportare, calendarul fiscal |
| `ObligatieFiscalaCreata` v1 | Declarație depusă cu sumă de plată | `ObligationId`, `CompanyId`, buget, sumă, scadență | F5 (sumă planificată), F2 (propunere de OP) |
| `DeclaratieDeVerificat` v1 | Luna unei declarații depuse s-a redeschis | `ReturnId`, motivul | Notificare către Contabil șef |

### 5.2 Consumate

`NotaContabilaValidata` (C2; registrul de TVA se scrie direct în tranzacția notei, iar evenimentul marchează doar ciornele de declarații ca neactualizate), `TvaLaIncasareExigibil` (F3), `PerioadaContabilaInchisa`, `PerioadaContabilaRedeschisa` (C3), `P300Descarcat`, `XmlDeclaratieValidat` (Integrări), `PlataInregistrata` (F2, închiderea obligației).

## 6. Contracte

```csharp
namespace Erp.Finance.Contracts.Tax;

// Folosit de C3 la închiderea lunii (pasul VAT_CLOSING)
public interface IVatReturnPreview
{
    Task<VatReturnSummary> GetSummaryAsync(int companyId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct);
}

public sealed record VatReturnSummary(decimal VatCollected, decimal VatDeductible, decimal VatPayable, decimal VatRefundable);

// Implementat de Integrări (02.14): generarea și validarea fișierelor ANAF
public interface IAnafDeclarationGateway
{
    Task<DeclarationFileResult> GenerateAsync(string formCode, string formVersion, Stream data, CancellationToken ct);
    Task<PrefilledVatReturn?> DownloadPrefilledVatReturnAsync(int companyId, int year, int month, CancellationToken ct);
}

public sealed record DeclarationFileResult(bool IsValid, string XmlPath, string? PdfPath, IReadOnlyList<ValidationMessage> Messages);
public sealed record ValidationMessage(byte Severity, string Code, string Text, string? Location);

// Implementat de Mijloace fixe și de Stocuri, pentru D406 active și D406 stocuri
public interface ISaftSectionProvider
{
    string Section { get; }        // "Assets", "PhysicalStock"
    IAsyncEnumerable<object> ReadAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);
}
```

Comenzi interne C4: `CalculateTaxReturn`, `VerifyTaxReturn`, `SetManualRowValue`, `ExplainPrefilledDifference`, `GenerateTaxReturnFile`, `RegisterSubmission`, `CreateCorrectiveReturn`, `CalculateProfitTax`, `PostProfitTax`, `GenerateTaxCalendar`, `ExportVatJournal`.

## 7. Schema SQL `fin` (partea C4)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `fin.VatEntry` | `Id bigint` | FK `EntryId`, `PartnerId`, `ParentVatEntryId` | `(CompanyId, PeriodYear, PeriodMonth, Direction) INCLUDE (OperationTypeCode, VatRateCode, BaseRon, VatRon)`; `(SourceDocumentType, SourceDocumentId)` |
| `fin.VatOperationType` | `Code nvarchar(20)` + `ValidFrom` | — | — |
| `fin.TaxForm`, `fin.TaxFormVersion`, `fin.TaxFormRowMapping` | `Code`; `Id int`; `Id int` | UQ `(FormCode, Version)`; UQ `(FormVersionId, OperationTypeCode, RowCode, Column)` | — |
| `fin.TaxReturn` | `Id int` | UX filtrat `(CompanyId, FormCode, PeriodFrom, Kind) WHERE Status <> 5`; FK `CorrectsReturnId` | `(CompanyId, DueDate, Status)` |
| `fin.TaxReturnLine`, `fin.TaxReturnPartnerLine` | `Id bigint` | FK `ReturnId` | `ReturnId` |
| `fin.TaxReturnFile` | `Id int` | FK `ReturnId`, `AttachmentId` | — |
| `fin.PrefilledVatReturn`, `fin.PrefilledVatDifference` | `Id int` | UQ `(CompanyId, PeriodYear, PeriodMonth, DownloadedAt)` | — |
| `fin.ProfitTaxComputation`, `fin.ProfitTaxComputationLine` | `Id int` | UQ `(CompanyId, Year, Quarter)` | — |
| `fin.TaxCalendarItem` | `Id int` | UQ `(CompanyId, FormCode, PeriodFrom)` | `(DueDate, Status)` |
| `fin.TaxObligation` | `Id int` | FK `ReturnId`, `PaymentId` | `(CompanyId, DueDate)` |

Exemplu de tabel complet:

```sql
CREATE TABLE fin.VatEntry (
    Id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_VatEntry PRIMARY KEY,
    CompanyId          int               NOT NULL CONSTRAINT FK_VatEntry_Company REFERENCES core.Company(Id),
    EntryId            bigint            NOT NULL CONSTRAINT FK_VatEntry_JournalEntry REFERENCES fin.JournalEntry(Id),
    SourceDocumentType nvarchar(40)      NOT NULL,
    SourceDocumentId   bigint            NOT NULL,
    DocumentNumber     nvarchar(40)      NOT NULL,
    DocumentDate       date              NOT NULL,
    Direction          tinyint           NOT NULL,          -- 1 Livrare, 2 Achiziție
    PartnerId          int               NULL CONSTRAINT FK_VatEntry_Partner REFERENCES core.Partner(Id),
    PartnerFiscalCode  nvarchar(30)      NULL,
    PartnerCountryCode char(2)           NULL,
    PartnerName        nvarchar(200)     NULL,
    OperationTypeCode  nvarchar(20)      NOT NULL,
    VatRateCode        nvarchar(10)      NOT NULL,
    VatRatePercent     decimal(5,2)      NOT NULL,
    BaseRon            decimal(19,2)     NOT NULL,
    VatRon             decimal(19,2)     NOT NULL,
    CurrencyCode       char(3)           NULL,
    BaseCurrency       decimal(19,2)     NULL,
    ExigibilityDate    date              NULL,              -- NULL: TVA la încasare încă neexigibil
    PeriodYear         smallint          NULL,
    PeriodMonth        tinyint           NULL,
    IsVatOnCollection  bit               NOT NULL CONSTRAINT DF_VatEntry_IsVatOnCollection DEFAULT (0),
    ParentVatEntryId   bigint            NULL CONSTRAINT FK_VatEntry_Parent REFERENCES fin.VatEntry(Id),
    DeductionPercent   decimal(5,2)      NOT NULL CONSTRAINT DF_VatEntry_DeductionPercent DEFAULT (100),
    IsReversal         bit               NOT NULL CONSTRAINT DF_VatEntry_IsReversal DEFAULT (0),
    EInvoiceStatus     tinyint           NULL,              -- 1 În e-Factura, 2 Lipsă din e-Factura, NULL nu se aplică
    CreatedAt          datetime2(3)      NOT NULL CONSTRAINT DF_VatEntry_CreatedAt DEFAULT (sysutcdatetime()),
    CONSTRAINT CK_VatEntry_Direction CHECK (Direction IN (1, 2)),
    CONSTRAINT CK_VatEntry_Period CHECK ((ExigibilityDate IS NULL AND PeriodYear IS NULL AND PeriodMonth IS NULL)
                                      OR (ExigibilityDate IS NOT NULL AND PeriodYear IS NOT NULL AND PeriodMonth BETWEEN 1 AND 12)),
    CONSTRAINT CK_VatEntry_Currency CHECK ((CurrencyCode IS NULL AND BaseCurrency IS NULL) OR (CurrencyCode IS NOT NULL AND BaseCurrency IS NOT NULL))
);
```

Proceduri stocate principale:

| Procedură | Rol | Publică |
| --- | --- | --- |
| `fin.usp_VatEntry_InsertForEntry` | Scrie rândurile registrului din componentele unei note (apelată de C2, aceeași tranzacție, TVP) | Nu |
| `fin.usp_VatJournal_Sales`, `_Purchases` | Jurnalele de TVA, paginate (`SP-12`) | Nu |
| `fin.usp_TaxReturn_CalculateD300`, `_CalculateD394`, `_CalculateD390` | Calculul pe set, pe versiunea formularului | Nu |
| `fin.usp_Saft_ReadGeneralLedger`, `_ReadPartners`, `_ReadInvoices`, `_ReadPayments` | Citirile paginate pentru fișierul D406 | Nu |
| `fin.usp_Saft_Check` | Verificările dinaintea generării | Nu |
| `fin.usp_ProfitTax_Calculate` | Fișa de calcul | Nu |
| `fin.usp_TaxCalendar_Generate` | Calendarul anului | Nu |

Coduri de eroare (plaja C4 `51940–51969`, prefix API `FIN_`):

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 51941 | `FIN_TAX_RETURN_NOT_FOUND` | 404 | Declarație inexistentă |
| 51942 | `FIN_TAX_RETURN_CONCURRENCY_CONFLICT` | 409 | `RowVersion` diferit (`SP-18`) |
| 51943 | `FIN_TAX_RETURN_PERIOD_OPEN` | 409 | Generarea fișierului pe o lună neînchisă |
| 51944 | `FIN_TAX_RETURN_DATA_CHANGED` | 409 | Datele s-au schimbat de la calcul (hash diferit); se recalculează |
| 51945 | `FIN_TAX_RETURN_ROW_NOT_EDITABLE` | 422 | Editarea unui rând calculat |
| 51946 | `FIN_TAX_RETURN_REASON_REQUIRED` | 422 | Rând manual sau explicație P300 fără motiv |
| 51947 | `FIN_TAX_RETURN_ALREADY_SUBMITTED` | 409 | Modificarea unei declarații depuse |
| 51948 | `FIN_TAX_RETURN_NOT_GENERATED` | 409 | Înregistrarea recipisei fără XML valid |
| 51949 | `FIN_TAX_FORM_VERSION_MISSING` | 409 | Nu există versiune de formular pentru perioadă |
| 51950 | `FIN_VAT_OPERATION_TYPE_UNKNOWN` | 422 | Componentă de TVA pentru care nu se poate determina tipul de operațiune |
| 51951 | `FIN_SAFT_ACCOUNT_CODE_MISSING` | 422 | Cont cu rulaj fără cont SAF-T |
| 51952 | `FIN_SAFT_PARTNER_INVALID` | 422 | Partener fără cod fiscal valid sau partener UE fără cod de TVA |
| 51953 | `FIN_ANAF_VALIDATOR_UNAVAILABLE` | 503 | DUKIntegrator nu poate fi rulat |
| 51954 | `FIN_PROFIT_TAX_PERIOD_NOT_SOFT_CLOSED` | 409 | Calculul final al impozitului cu luna de sfârșit de trimestru deschisă |

Conformitate: schema și procedurile respectă `SQL-01`–`SQL-25`, `SP-01`–`SP-21`, `MIG-01`–`MIG-07`, cu abaterea:

- Abatere de la `SQL-19` și `SQL-11` pentru `fin.VatEntry`: fără `ModifiedAt`, `ModifiedBy`, `RowVersion`. Motiv: rândurile se scriu o dată, odată cu nota; corecția se face prin storno, ca la `JournalLine`. Permanentă.

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Fin.Tax.View` | Registrul și jurnalele de TVA, declarațiile, calendarul |
| `Fin.Tax.Prepare` | Calculul, verificarea, rândurile manuale, explicațiile P300 |
| `Fin.Tax.Generate` | Generarea fișierelor |
| `Fin.Tax.Submit` | Înregistrarea recipisei și crearea rectificativelor |
| `Fin.ProfitTax.Manage` | Fișa de calcul și contarea impozitului |

Roluri livrate: Contabil șef (toate), Contabil (`View`, `Prepare`), Auditor (`View`), Director financiar (`View`).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru C4 |
| --- | --- |
| Codul fiscal, titlul VII (TVA) și normele | Exigibilitatea, cotele, taxarea inversă, TVA la încasare, deducerea limitată; jurnalele de vânzări și cumpărări |
| OPANAF pentru D300, D394, D390, D100, D101 (modelele în vigoare) | Rândurile și schemele, versionate pe dată |
| OUG 70/2024 și OPANAF pentru RO e-TVA | Preluarea P300 și explicarea diferențelor peste prag |
| OPANAF 1783/2021 și modificările (SAF-T) | D406 lunar sau trimestrial, active anual, stocuri la cerere; nomenclatoarele ANAF |
| Codul fiscal, titlul II (impozit pe profit) și titlul III (microîntreprinderi) | Fișa de calcul, limitele de deductibilitate, creditele fiscale, cotele |
| Codul de procedură fiscală | Termenele, rectificativele, păstrarea documentelor (10 ani) |

Integrări: Integrări (02.14) pentru XML, validator, SPV (P300), verificarea CUI și VIES. Interne: C1 (`SaftAccountCode`, `TaxTreatment`), C2 (registrul de TVA în tranzacția notei), C3 (lună închisă, nota de TVA), C5 (balanța), F3 (TVA la încasare), F5 și F2 (obligații și plăți), Nucleu (regimul firmei, cote, calendar).

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Jurnalul de vânzări, jurnalul de cumpărări | Pe lună, cu coloanele pe cote și tipuri de operațiuni, totaluri egale cu D300 | Contabil, Auditor |
| Registrul de TVA la încasare | Facturile cu TVA neexigibil, încasările și plățile, soldul `4428` | Contabil |
| Diferențe RO e-TVA | Pe lună: rândurile P300 față de D300, explicațiile, facturile care explică | Contabil șef |
| Registrul de evidență fiscală | Fișa de calcul a impozitului pe profit, cu detalierea | Contabil șef, Auditor |
| Declarații depuse | Pe firmă și an: formular, perioadă, număr de înregistrare, sumă, rectificative | Contabil șef, Director financiar |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/c4-declaratii/](ecrane/c4-declaratii/), în meniul Finanțe și contabilitate › Declarații:

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Calendar declarații | Declarațiile lunii pe firme, cu termenul, starea, suma, fișierele | `c4-01-calendar-declaratii` |
| Decont TVA (D300) | Rândurile calculate, comparația cu P300 și explicațiile, verificările, generarea | `c4-02-decont-tva` |
| Jurnale de TVA | Jurnalul de vânzări sau de cumpărări, pe cote și tipuri de operațiuni | `c4-03-jurnale-tva` |
| SAF-T (D406) | Verificările pe categorii, generarea în fundal, raportul validatorului, fișierele | `c4-04-saf-t` |
| Impozit pe profit | Fișa de calcul pe trimestru, cu detalierea și nota de impozit | `c4-05-impozit-pe-profit` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.02-C4-An")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| C4-A1 | Pentru orice lună, TVA colectată din registru = rulajul creditor `4427`, iar TVA deductibilă = rulajul debitor `4426` (fără nota de regularizare) | Test de proprietate |
| C4-A2 | D300 octombrie 2026 pentru Exemplu Distribuție dă TVA colectată 412.900, deductibilă 268.330, de plată 144.570, egal cu nota IN 00020 din C3 | Test de integrare (cu C3) |
| C4-A3 | O factură cu TVA la încasare apare în D300 doar în lunile încasărilor, pentru partea încasată | Test de integrare (cu F3) |
| C4-A4 | O achiziție intracomunitară de servicii apare în D300 atât la colectat, cât și la deductibil, și în D390 cu codul `P` | Test de integrare |
| C4-A5 | Generarea XML pe o lună deschisă e refuzată (`51943`); după o modificare a datelor de la calcul, e refuzată cu `51944` | Test de integrare |
| C4-A6 | XML-urile D300, D394, D390, D100, D406 generate pe setul de date de test trec validatorul ANAF fără erori | Test de integrare (cu DUKIntegrator) |
| C4-A7 | Diferențele P300 peste prag (20% și 5.000 lei) sunt marcate, iar cele sub prag nu | Test unitar |
| C4-A8 | Fișa de calcul pentru trimestrul 3 2026 adaugă cheltuielile cu `TaxTreatment` = 2 și partea de protocol peste limită | Test unitar |
| C4-A9 | D406 pentru o lună cu 2 milioane de linii se generează în sub 15 minute, cu cel mult 1 GB memorie | Test de performanță |
| C4-A10 | Depunerea D300 cu TVA de plată creează obligația cu scadența 25 a lunii următoare, mutată pe ziua lucrătoare, și ea apare în prognoza F5 | Test de integrare (cu F5) |
| C4-A11 | Redeschiderea unei luni cu D300 depus marchează declarația „De verificat”; fără schimbări, marcajul dispare la reînchidere | Test de integrare (cu C3) |
| C4-A12 | Toate procedurile C4 rulează corect pe baza case sensitive creată pe un server cu collation case insensitive | Test de integrare (`TST-09`) |

Criteriile C4-A1, C4-A2 și C4-A6 sunt critice: o declarație care nu se potrivește cu contabilitatea sau pe care ANAF o respinge înseamnă amenzi pentru client.

## Precizări din 02.15 Mijloace fixe și obiecte de inventar (aplicate pe 27.11.2026)

Modulul 02.15 ([README](../15-mijloace-fixe/README.md), părțile M1–M7) a fost adăugat pe 27.11.2026. Precizările de mai jos completează acest document; unde textul de mai sus spune altceva, precizarea are prioritate.

- **D101**: amortizarea contabilă (nedeductibilă) și cea fiscală (deducere), partea nedeductibilă (plafon, uz mixt), rezerva din reevaluare devenită impozabilă și valoarea fiscală rămasă la ieșiri vin din `IFiscalDepreciationQuery` ([M3 §6](../15-mijloace-fixe/M3-AMORTIZARE.md)) și din evenimentul `MijlocFixIesit` (marcajele fiscale, M5).
- **D406 active**: secțiunea `Assets` și `AssetTransactions` vine din `ISaftAssetsQuery` ([M7 §6](../15-mijloace-fixe/M7-INVENTARIERE-RAPOARTE.md)), implementarea pentru Mijloace fixe a contractului de mai sus.
- **TVA**: semnalele de ajustare din M5 și M6 (lipsuri neimputabile, donații, bunuri de capital) apar în lista de ajustări propuse pentru decont, confirmate de contabil.
- **Sponsorizarea**: valoarea donațiilor de mijloace fixe (`IMF-DON`) intră în calculul limitei.

## Decizii de validare

Toate deciziile au fost confirmate de Aurelian pe 15.11.2026:

| # | Întrebare | Decizia | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Depunerea declarațiilor | În faza 1, ERP generează XML-ul validat și PDF-ul; semnarea și depunerea pe portalul ANAF le face utilizatorul, apoi încarcă recipisa. Depunerea automată vine când ANAF oferă un API pentru declarații | 4.1, 3.3 | Decis de Aurelian (15.11.2026) |
| 2 | Sursa datelor de TVA | Un registru de TVA propriu, scris odată cu nota contabilă, din care se fac jurnalele și toate declarațiile de TVA | 2 (regula 1), 3.1 | Decis de Aurelian (15.11.2026) |
| 3 | Când se generează fișierul final | Doar după închiderea lunii în C3; calculul în ciornă oricând | 2 (regula 4) | Decis de Aurelian (15.11.2026) |
| 4 | Corecții manuale în declarații | Rândurile calculate nu se editează (corecția se face prin document sau notă); doar rândurile pur declarative se completează manual, cu motiv | 2 (regula 5) | Decis de Aurelian (15.11.2026) |
| 5 | Diferențele față de RO e-TVA | Nu blochează generarea D300; cele peste prag cer explicație scrisă pe fiecare rând | 3.4 | Decis de Aurelian (15.11.2026) |
| 6 | Impozitul pe profit | Calculat în ERP (fișa de calcul), din conturi marcate deductibil, nedeductibil, limitat sau neimpozabil (atribut nou în C1), plus rânduri manuale cu motiv | 3.6 | Decis de Aurelian (15.11.2026) |
| 7 | Obligațiile de plată | Fiecare declarație depusă cu sumă de plată intră automat în Planificare (F5) ca sumă planificată, la scadență | 2 (regula 7), 3.7 | Decis de Aurelian (15.11.2026) |

## Note de implementare (24.11.2026)

| Pas | Ce s-a implementat | Decizii și abateri |
| --- | --- | --- |
| C4.1 | Migrarea `2026.11.067` (`fin.VatOperationType`, `fin.VatEntry`); seed-ul `13_VatOperationTypes` (21 de tipuri, cu cotele vechi până la 31.07.2025); clasificarea pură `VatRegister` (`Erp.Finance.Domain.Declarations`); procedurile `fin.usp_VatEntry_Write`, `_WriteExigible`, `fin.usp_VatJournal_Get`; cotele Nucleului în contextul contării; registrul scris de `PostingService` odată cu nota facturilor de vânzare și de furnizor și cu nota TVA la încasare devenite exigibile; stornarea la sursă copiază rândurile cu semn schimbat; interogarea `GetVatJournal`; permisiunea `Fin.Tax.View` (Contabil, Auditor, Director financiar); API `/api/finance/vat-journal`; testele C4-A1, A3 și clasificarea C4-A4 (unitar) | **Tipul de operațiune** vine din sensul documentului, categoria cotei (S, Z, E, AE, K, G, O), tipul partenerului (UE → intracomunitar, non-UE → export / import), marcajele `ReverseCharge` și `Services` sau tipul articolului. **Facturile pe totaluri** (fără cotă pe componente, ca evenimentele F3 de acum) primesc cota standard în vigoare cea mai apropiată de raportul TVA / bază; fără TVA, regimul după partener. **TVA la încasare**: rândul facturii e neexigibil (fără perioadă), iar fiecare `TvaLaIncasareExigibil` adaugă rânduri copil, împărțite proporțional pe cotele facturii, în luna încasării (evenimentul poartă acum și baza, componenta `Base`). Perioada rândurilor e data contabilă a documentului. Rămân: rândurile deconturilor de cheltuieli (fără partener în eveniment), cota de deducere limitată (50% autoturisme), statusul e-Factura pe rând (RAMAS 1.29) |
| C4.2 | Migrarea `2026.11.068` (`fin.TaxForm`, `fin.TaxReturn`, `fin.TaxReturnLine`, `fin.TaxReturnFile`, `fin.TaxCalendarItem`, `fin.TaxObligation`); seed-ul `14_TaxForms`; calendarul pur `TaxCalendar` (termenele pe periodicitatea TVA a anului, mutate pe ziua lucrătoare după sărbătorile legale, Paștele ortodox calculat); calculul pur `D300` din rezumatul registrului (`fin.usp_VatRegister_Summary`, cu amprenta datelor); procedurile `fin.usp_TaxReturn_Save`, `_Get`, `_Transition`, `_SaveFile`, `_GetFile`, `_MarkReview`, `_ClearReview`, `fin.usp_TaxCalendar_Save`, `_List`; comenzile `CalculateTaxReturn`, `VerifyTaxReturn`, `GenerateTaxReturn`, `SubmitTaxReturn`, `GenerateTaxCalendar`; consumatorii redeschiderii și reînchiderii lunii; evenimentul `DeclaratieDepusa`; permisiunile `Fin.Tax.Prepare` (Contabil), `Fin.Tax.Generate`, `Fin.Tax.Submit`; API `/api/finance/tax-returns`, `/tax-calendar`, `/tax-return-files/{id}`; testele C4-A5, A10, A11 | **Amprenta** e `CHECKSUM_AGG` pe rândurile registrului perioadei, reținută la calcul și comparată la generare (51944) și la reînchiderea lunii (marcajul „De verificat” se șterge doar dacă amprenta e aceeași). **Fișierul generat** e, până la C4.3, instantaneul JSON al calculului (generatorul XML se înregistrează pe formular prin `ITaxReturnFileGenerator`). **Obligația de plată** se scrie la depunere, cu suma planificată în F5 (categoria TAXE, probabilitate 100%) la scadența din calendar; `ObligatieFiscalaCreata` și `DeclaratieDeVerificat` nu se publică încă (RAMAS 1.30). Doar D300 se calculează; celelalte formulare răspund `51949` până la pașii lor |
| C4.3 | Migrarea `2026.11.069` (`fin.TaxReturnComparison`, `TaxReturn.PrecompletedAt`); maparea pură `D300Layout` (tipurile de operațiuni pe rândurile formularului; taxarea inversă la achiziții pe un rând colectat și unul deductibil); comparația pură `PrecompletedVatComparison` (pragurile 5.000 lei și 20%); generatorul `D300XmlGenerator` (antetul firmei la sfârșitul perioadei din `ICompanyDirectory`, tipul decontului după lungimea perioadei, valorile în lei întregi); procedurile `fin.usp_TaxReturn_SaveComparison`, `_Explain`; verificarea refuză diferențele peste prag neexplicate (51946); comenzile `ImportPrecompletedVat`, `ExplainTaxDifference`; API `/tax-returns/{id}/precompleted`, `/explain`; testele C4-A2 (adaptat), C4-A7 | **Maparea rândurilor** e în cod (`D300Layout`, versiunea livrată), nu în tabelul `TaxFormRowMapping`: tabelul versionat vine odată cu a doua versiune a formularului (RAMAS 1.31). **C4-A2** se verifică pe datele testului, nu pe setul „Exemplu Distribuție”: totalurile D300 (colectată, deductibilă, de plată) sunt egale cu nota de regularizare a TVA din simularea închiderii (C3). **P300** se încarcă deocamdată prin comandă (rândurile `R{rând}` × `BASE`/`VAT`); descărcarea din SPV și facturile care explică diferența vin cu Integrări (RAMAS 2). Recalcularea păstrează valorile P300 și explicațiile, refăcând marcajele. Diferențele nu blochează generarea (decizia 5), ci verificarea până la explicare. **XML-ul** urmează structura formularului (atributele `R{rând}_1`, `R{rând}_2` pe `declaratie300`); numerele de rând și spațiul de nume se confirmă pe XSD-ul ANAF al versiunii (RAMAS 4), iar validarea cu DUKIntegrator și PDF-ul cu XML-ul atașat sunt ale Integrărilor. Rândurile de regularizări, prorata și soldul de rambursat din perioada anterioară rămân (RAMAS 1.31) |
| C4.4 | Migrarea `2026.11.070` (`fin.TaxReturnPartnerLine`); calculul pur `InformativeReturns` (D394 pe secțiunile L, V, A, C, pe partener și cotă; D390 pe codurile L, S, A, P, pe partener și țară; problemele pe linie); procedurile `fin.usp_VatRegister_ByPartner`, `fin.usp_TaxReturn_SavePartnerLines`; `CalculateTaxReturn` acceptă D394 și D390 (rânduri = totalurile pe secțiune, cu numărul de parteneri; liniile pe partener separat); generatoarele `D394XmlGenerator`, `D390XmlGenerator`; testul C4-A4 (integrare: D300 colectat și deductibil, D390 cu codul `P`) | **Problemele pe linie** (partener din țară fără cod fiscal, partener UE fără cod de TVA sau cu țara România) se arată, dar nu blochează: verificarea CUI la ANAF și VIES, la data facturii, vine cu Integrări (RAMAS 2.12). Completitudinea seriilor de facturi emise și secțiunile D394 pentru persoane fizice, bonuri fiscale și regimuri speciale rămân (RAMAS 1.32). Tipul partenerului (UE, non-UE) vine din `core.Partner.PartnerType`. Structura XML (`declaratie394` / `declaratie390`, elementele `op`) se confirmă pe XSD-urile ANAF (RAMAS 4.3) |
| C4.5 | Funcția `fin.ufn_SaftAccountCode` (contul SAF-T al contului sau al celui mai apropiat părinte); procedurile `fin.usp_Saft_Summary` (rezumatul și amprenta: notele și registrul), `_Check` (conturi fără cont SAF-T, parteneri fără cod fiscal sau UE fără prefixul țării, note fără număr), `_ReadMasterData`, `_ReadGeneralLedger` (paginat pe jurnal, după cheia notei și a liniei), `_ReadInvoices` (paginat); `SaftFileGenerator` (fișierul scris în flux într-un fișier temporar, salvat în flux, fără să încarce luna în memorie); `CalculateTaxReturn` acceptă D406 (rândurile = rezumatul perioadei); interogarea `GetSaftChecks`; API `/api/finance/saft-checks`; codurile 51951, 51952 | **Generarea** rulează sincron în comanda „Generează” (nu în job Hangfire cu progres) și verifică întâi problemele blocante (conturi, parteneri); notele fără număr sunt doar avertisment. **Soldurile conturilor** vin din balanță (`IBalanceQuery`, regula 1 din C5). **Facturile** sunt rândurile registrului de TVA grupate pe document (o linie pe cotă), fără articole; **plățile** (`Payments`), fișierele anuale de active și cele de stocuri, împărțirea pe segmente și descărcarea în flux rămân (RAMAS 1.33, 2.13). C4-A9 (2 milioane de linii, 15 minute, 1 GB) rămâne test de performanță neexecutat (RAMAS 3). Structura și spațiul de nume se confirmă pe XSD-ul ANAF (RAMAS 4.3) |
| C4.6 | Migrarea `2026.11.071` (`fin.ProfitTaxComputation`, `fin.ProfitTaxComputationLine`); tratamentul fiscal livrat al conturilor în seed-ul `09` (6581, 6582 nedeductibile; 6231 limitat; 7611, 7613 neimpozabile); calculul pur `ProfitTax` (regimul de profit și de microîntreprindere); procedurile `fin.usp_ProfitTax_Data`, `_Save`, `_Get`, `_SetPosted`; comenzile `CalculateProfitTax`, `SetProfitTaxManualRow`, `PostProfitTax` (nota 691 = 4411 prin `fin.usp_SystemEntry_Write`), interogarea `GetProfitTax`; `CalculateTaxReturn` acceptă D100 și D101 din fișă; permisiunea `Fin.ProfitTax.Manage` (Contabil șef); API `/api/finance/profit-tax/{an}/{trimestru}`; testele C4-A8 (unitar) și fișa trimestrială (integrare) | **Datele** sunt rulajele cumulate de la începutul anului, fără 691, 698 și fără notele de închidere a claselor 6 și 7. **Limita de protocol** e 2% din rezultatul contabil plus cheltuielile de protocol; alte limite (ajutoare sociale, sponsorizarea ca credit calculat) sunt rânduri manuale (`DEDUCTIONS`, `LOSS`, `CREDITS`, cu motiv). **Impozitul trimestrelor anterioare** e suma contată (sau calculată) a fișelor anterioare din an; trimestrul 4 e calculul anual (D101). **Nota** e SourceKind 4 în jurnalul IN, în ultima zi a trimestrului, după închiderea operativă a lunii (51954), rescrisă la o nouă contare. **Regimul și cota** vin din setările firmei `Fin.Tax.ProfitRegime` și `Fin.Tax.Rate` (implicit profit, 16%; microîntreprindere 1%), nu din Nucleu, și nu au încă ecran; seed-ul aplică tratamentul fiscal doar conturilor rămase pe cel implicit. D100 și D101 folosesc fișierul JSON până la generatorul XML (RAMAS 1.34) |
| C4.7 | Ecranele `DeclarationsPages`: Calendar declarații (c4-01: termenele lunii, numărătorile, generarea calendarului, panoul declarației cu fișierele, verificarea, generarea și înregistrarea depunerii), Decont TVA (c4-02: rândurile pe secțiuni, comparația cu P300 și explicațiile), Jurnale de TVA (c4-03: vânzări / cumpărări, totalurile pe tip de operațiune), SAF-T (c4-04: verificările pe categorii, rezumatul, panoul), Impozit pe profit (c4-05: trimestrele și anul, rândurile manuale cu motiv, contarea); modelul `taxModel`; interogarea `FindTaxReturn` (API `GET /api/finance/tax-returns`); meniul Declarații | **Fără** calendarul pe firmele grupului, exportul Excel, încărcarea recipisei din ecran (API-ul o primește), descărcarea P300 și facturile care explică diferențele (Integrări), detalierea rândurilor până la note și lista conturilor nedeductibile din c4-05 (RAMAS 1.35). Sumele rămân text, fără calcule în browser (numărătorile din antet sunt doar număr de declarații) |

