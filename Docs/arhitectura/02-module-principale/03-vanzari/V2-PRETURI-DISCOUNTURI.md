# ERP · 02.03 Vânzări · V2 Prețuri și discounturi

27 noiembrie 2026 · Aurelian · **✓ Validat (27.11.2026)**, cu ecranele și toate cele 12 decizii („1-12 DA, ecrane validate”); completat pe 27.11.2026 la cererea lui Aurelian cu istoricul complet al prețurilor, prețurile pe durată determinată (revenirea automată la prețul anterior) și listele de prețuri pe partener (2 regulile 1 și 4, 3.1, 3.1.1, 4.4). A doua din cele 7 părți ale modulului 02.03 Vânzări. Pornește de la [V1 Configurare](V1-CONFIGURARE.md), validată pe 27.11.2026 („1-11 DA, ecrane validate”), și de la propunerile P2, P3 și P4 din [INSPIRATIE_WINDOWS_CLIENT.md](INSPIRATIE_WINDOWS_CLIENT.md), confirmate de Aurelian pe 27.11.2026.

Modulul 02.03 Vânzări are schema SQL `sales`, prefixul de cod `Erp.Sales` și plaja de erori SQL `52000–52999`; V2 folosește `52100–52199`. Această parte acoperă tot ce dă prețul unei linii de vânzare: listele de prețuri (de vânzare și cu amănuntul), lanțul de liste (lista clientului, lista grupei, lista de bază), pragurile de cantitate, regulile de rotunjire, modificările de preț programate și aprobate, regulile de discount (automate și opționale, pe linie și pe document, promoții, bonus în marfă), discountul manual cu plafon, verificarea marjei și a prețului sub cost, aprobarea excepțiilor de preț și acordurile de rabat pe rulaj. Documente părinte: [02 Module principale](../MODULE_PRINCIPALE.md), [02.01 Nucleu](../01-nucleu/NUCLEU.md) (§3.3 Articole, §3.3.1 Variante, regula 9 a căutării prețului, §3.5 Precizie și cursuri, §3.6 Tranzacții), [V1](V1-CONFIGURARE.md) (condițiile clientului, grupa comercială, plafonul agentului, setările de vânzări), [02.05 Stocuri](../05-stocuri/README.md) (S1 gestiunile la preț cu amănuntul, S3 costul și modificarea prețului cu amănuntul), [02.02 Finanțe](../02-finante-contabilitate/README.md) (F4 cursurile). Principii: [01 Scop și principii](../../01-scop-si-principii/SCOP_SI_PRINCIPII.md) (P1–P10). Reguli de cod: [Bune practici (SQL și cod)](../../bune-practici/BUNE_PRACTICI.md), referite prin identificator.

**Ce trebuie știut înainte de citire (context autonom, pentru oameni și pentru Copilot / Claude Code):**

- Produs ERP licențiat, instalat on-premise, o bază SQL Server per grup de firme, collation `Latin1_General_100_CS_AS_SC_UTF8` (case sensitive, `SQL-21`); căutările fără diacritice folosesc coloane calculate cu `Latin1_General_100_CI_AI_SC_UTF8` (`SQL-25`). Monolit modular .NET 10 + MediatR; Dapper și proceduri stocate, **fără EF Core sau alt ORM**. Fără componente plătite.
- **Articolul e model, varianta e SKU** (Nucleu §3.3.1). Liniile documentelor stau pe variantă (`core.ItemVariant`). Un preț se poate defini pe variantă, pe o grupă de valori a unui model (de exemplu „toate aromele” sau „mărimile mari”) sau pe model; ordinea de căutare e regula 9 din Nucleu §3.3.1, aplicată aici în `sales.fn_VariantPrice`.
- **Prețul se definește pe UM de bază** a modelului (Nucleu §3.3.1, regula 9). Pe o linie în altă UM (de exemplu bax de 12), prețul e prețul de bază înmulțit cu factorul UM din setul articolului, apoi rotunjit la precizia listei.
- Prețurile se țin pe `decimal(19,6)`, cu zecimalele date de politica de precizie (Nucleu §3.5, `PrecisionPolicy`: listă de prețuri → articol → firmă); valorile pe `decimal(19,4)`, rotunjite explicit la 2 zecimale. Rotunjirea se face doar prin `IPrecisionService` (P7, `NET-14`).
- **Prețul de listă** e prețul din lista de prețuri, înainte de discounturi. **Prețul unitar** e prețul final al liniei, după discounturi. Pe linie se păstrează amândouă și fiecare discount aplicat, cu sursa lui (P2), ca factura, marja și rapoartele să poată explica orice preț.
- Discounturile sunt **reguli tipizate** (condiții pe client, grupă, canal, zonă, agent, articol, familie, cantitate, valoare, modalitate de plată). Nu există filtre scrise ca text sau SQL în configurare (INSPIRATIE §4).
- Tranzacțiile, legăturile între documente și asistentul de generare sunt în Nucleu §3.6 și V1. Documentele generate din sursă **preiau prețul și discounturile din sursă**; nu se recalculează (decizia 4).
- **Ce NU face V2:** nu creează documente de vânzare (V3–V6), nu face aprobarea ofertelor și comenzilor pe valoare (V3), nu emite nota de reducere pentru rabatul pe rulaj (V6, tranzacția `FS-RED`), nu calculează costul (S3, citit prin `IStockValuationQuery`) și nu face prețurile din POS offline (modulul POS le primește prin evenimente).

## 1. Scop și perimetru

| Zonă | Ce face V2 | Ce primește de la alții |
| --- | --- | --- |
| Liste de prețuri | Liste de vânzare și cu amănuntul, cu monedă, preț cu sau fără TVA, listă părinte cu ajustare procentuală, regulă de rotunjire, precizie, liniile pe variantă, grupă de valori sau model, cu praguri de cantitate și valabilitate | Articolele, variantele, grupele de valori, UM-urile (Nucleu); lista din condițiile clientului și a grupei (V1) |
| Modificări de preț | Modificarea programată și aprobată a unei liste (procent, sumă, preț nou), cu previzualizarea marjei și a listelor derivate | Costul curent (S3) |
| Căutarea prețului | Motorul de preț (`IPriceEngine`): lista documentului, lanțul de liste, pragul, conversia de UM și de monedă, rotunjirea | Cursul BNR (Nucleu fluxul 4.3, F4) |
| Discounturi | Reguli automate și opționale, pe linie și pe document, promoții, bonus în marfă, praguri de cantitate și de valoare, prioritate și exclusivitate; discountul manual | Plafonul agentului (V1 `SalesAgent.MaxDiscountPct`) |
| Controlul prețului | Plafoane de discount, marja minimă, prețul sub cost, prețul lipsă, aprobarea excepțiilor pe document | Costul (S3), regulile de aprobare (V2), circuitul documentului (V3) |
| Rabat pe rulaj | Acordurile cu praguri pe perioadă, estimarea zilnică a rabatului câștigat | Facturile validate (V5); nota de reducere la final de perioadă (V6) |
| Prețul cu amănuntul | Lista cu amănuntul (cu TVA), legată de gestiunile magazin; cererea documentului de modificare a prețului (MP) | S1 (`RetailPriceListId`), S3 (documentul MP) |

## 2. Model de domeniu

```
 PriceList (firmă, cod, tip: vânzare / cu amănuntul, monedă, cu sau fără TVA, precizie, rotunjire)
   ├─ ParentPriceList + ParentAdjustPct (lanț de cel mult 3 niveluri: listă client → listă grupă → listă de bază)
   └─ PriceListLine × N (variantă | model + grupă de valori | model, prag de cantitate, preț, preț minim, valabil de la–până la)
          ▲ scrise direct sau dintr-o PriceChange (modificare programată, aprobată) × PriceChangeLine
 PriceRoundingRule (nomenclator cu seed: pas, terminație, sens)

 DiscountRule (automată / opțională, pe linie / pe document, procent / sumă pe unitate / sumă pe document / bonus în marfă,
               promoție, grup de exclusivitate, prioritate, prezentare, valabilitate)
   ├─ DiscountRuleTarget × N (condiții: client, grupă, canal, zonă, agent, punct de livrare, tranzacție,
   │                          variantă, model, familie, grupă de valori, grupă de articole; incluse sau excluse)
   └─ DiscountRuleTier × N (praguri de cantitate sau de valoare → procent sau sumă)

 Linia oricărui document de vânzare (V3–V6): ListPrice, sursa prețului, UnitPrice, NetAmount, cost, marjă
   └─ DocumentLineDiscount × N (discounturile aplicate, în ordine, cu regula sursă sau „manual”)

 SalesApprovalRule (nivel, rol, plafon de discount, marjă minimă) ─► SalesApproval (jurnal, doar adăugare)
 RebateAgreement (client sau plătitor, perioadă, bază) ─► RebateTier × N ─► estimare zilnică ─► FS-RED (V6)
```

Reguli de domeniu:

1. **Lista documentului se alege o dată, la crearea documentului**, în ordinea: lista proprie a partenerului în moneda documentului (3.1.1) → lista din condițiile clientului (V1 §3.4) → lista grupei comerciale → lista implicită a firmei. Tranzacția poate impune o listă (de exemplu `FV-LIV-AG` cu lista de prețuri a mașinii). Lista aleasă se scrie pe antet (`PriceListId`) și se poate schimba doar cu dreptul `Sales.Price.ChangeList`, cât timp documentul e ciornă.
2. **O listă poate avea o listă părinte, cu ajustare procentuală.** Când lista nu are preț pentru variantă, prețul se ia din părinte și se ajustează (de exemplu HoReCa = Bază + 3 %), apoi se rotunjește după regula listei copil. Cu ajustare 0 % părintele e un simplu „rezervor” pentru articolele fără preț propriu. Lanțul are cel mult 3 niveluri și nu poate avea cicluri (`52102`); părintele are aceeași monedă și aceeași bază de TVA (`52103`).
3. **În aceeași listă, linia cea mai specifică câștigă**, în ordinea din Nucleu (regula 9): variantă → cea mai apropiată grupă de valori a modelului (urcând în arbore; la egalitate, slotul cu numărul cel mai mic) → model. Lanțul de liste se urmează doar dacă lista curentă nu are nicio linie aplicabilă, pe niciun nivel.
4. **Lista păstrează tot istoricul și un preț în vigoare nu se modifică.** O linie cu `ValidFrom` ≤ azi nu se mai editează și nu se șterge (`52105`); liniile vechi rămân pentru totdeauna, ca orice preț de la orice dată să poată fi reconstituit (P6). Există două feluri de linii: **permanente** (fără dată de sfârșit) și **temporare** (cu dată de sfârșit, de exemplu 01.03.2026–03.03.2026). O linie permanentă nouă închide automat linia permanentă anterioară (`ValidTo` = ziua dinainte). O linie temporară **nu închide nimic**: în perioada ei acoperă prețul permanent, iar după ultima ei zi prețul revine singur la linia permanentă care era în vigoare (de exemplu cea din 28.02.2026), fără să fie reintrodus. Liniile viitoare se pot edita sau șterge până intră în vigoare.
5. **Pragurile de cantitate sunt în UM de bază**, pe aceeași țintă și dată: câștigă pragul cel mai mare ≤ cantitatea liniei (convertită în UM de bază). Pragurile se aplică pe linie, nu pe documentul întreg; cumularea cantităților pe document se face cu reguli de discount (3.6).
6. **Discounturile automate nu se pot scoate de pe document** (`52111`). Cele opționale se adaugă de utilizator, doar dacă rolul lui are voie (`52110`), iar cele editabile până la valoarea din regulă. Discountul manual e un procent pe linie sau pe document, limitat de plafon (3.9).
7. **Ordinea de calcul e fixă** (3.7) și e aceeași în toate documentele: preț de listă → discounturi automate pe linie → opționale pe linie → manual pe linie → bonus în marfă → discounturi pe document → rotunjire → verificări. Același calcul rulează în ecran (previzualizare), la salvare și la validare; rezultatul de la validare e cel scris.
8. **Prețul și discounturile se îngheață la confirmarea documentului.** Ciorna se recalculează la fiecare schimbare de client, dată, cantitate sau listă. Documentele generate dintr-o sursă (aviz din comandă, factură din aviz) copiază prețul unitar, prețul de listă și discounturile sursei, proporțional cu cantitatea; nu caută din nou prețul.
9. **Excepțiile de preț se aprobă pe document, nu pe linie.** Plafonul agentului depășit, marja sub minim, prețul sub cost sau un preț modificat manual pun documentul în „De aprobat”. Aprobarea acoperă exact liniile și valorile aprobate; o schimbare ulterioară care înrăutățește excepția cere aprobare din nou.

## 3. Entități și setări

### 3.1 Listele de prețuri (`PriceList`)

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `CompanyId`, `Code`, `Name` | `int`, `nvarchar(20)`, `nvarchar(100)` | Codul unic pe firmă, fără diferență de majuscule (`CodeKey`); nu se schimbă după prima folosire (`52014`) |
| `Kind` | `tinyint` | 1 **Vânzare** (prețuri pentru documentele de vânzare) · 2 **Cu amănuntul** (prețul de raft din magazine, cu TVA, folosit de S1 pentru gestiunile la preț cu amănuntul și de POS) |
| `CurrencyCode` | `char(3)` | Moneda prețurilor. Lista cu amănuntul e întotdeauna în RON (`52107`) |
| `PricesIncludeVat` | `bit` | 0 = prețuri fără TVA (vânzare en-gros); 1 = cu TVA (cu amănuntul, obligatoriu la `Kind` = 2). La o listă cu TVA, prețul fără TVA se calculează cu cota variantei la data prețului și se rotunjește la precizia listei |
| `ParentPriceListId`, `ParentAdjustPct` | `int NULL`, `decimal(9,4)` | Lista părinte și ajustarea procentuală (−90 … +500). Regula 2 |
| `RoundingRuleId` | `int NULL` | Regula de rotunjire pentru prețurile calculate (din părinte, din modificări procentuale); prețurile scrise de mână nu se rotunjesc. Gol = rotunjirea la precizia listei |
| `PrecisionPolicyId` | `int NULL` | Precizia prețurilor listei (Nucleu §3.5); gol = politica firmei |
| `RequiresApproval` | `bit` | 1 = orice preț nou sau schimbat trece printr-o modificare aprobată (3.3), nu se scrie direct pe linie. Implicit 1 pentru lista de bază și pentru listele cu amănuntul |
| `ValidFrom`, `ValidTo` | `date`, `date NULL` | Perioada listei; liniile au și ele valabilitate |
| `PartnerId` | `int NULL` | Gol = listă generală (atribuită clienților, grupelor, gestiunilor). Completat = **lista proprie a partenerului** (3.1.1) |
| `IsDefault` | `bit` | Lista implicită a firmei (cel mult una activă de vânzare pe firmă și monedă) |
| `IsActive`, `Notes` | | O listă folosită (pe clienți, grupe, gestiuni, tranzacții sau ca părinte) nu se dezactivează (`52118`) |

**Linia** (`PriceListLine`):

| Câmp | Tip | Reguli |
| --- | --- | --- |
| `ItemVariantId` | `int NULL` | Ținta „variantă” (cea mai specifică) |
| `ItemId`, `ValueListGroupId` | `int NULL`, `int NULL` | Ținta „model” (doar `ItemId`) sau „grupă de valori a modelului” (`ItemId` + `ValueListGroupId`, grupa trebuie să aparțină unei liste de valori folosite pe un slot al modelului, `52104`). Exact o țintă pe linie (`CHECK`) |
| `MinQty` | `decimal(19,6)` | Pragul de cantitate în UM de bază (0 = de la prima unitate) |
| `Price` | `decimal(19,6)` | Prețul pe UM de bază, în moneda listei, cu sau fără TVA după listă |
| `MinPrice` | `decimal(19,6) NULL` | Prețul minim de vânzare: un preț unitar final sub el e o excepție (3.9). Gol = se verifică doar marja |
| `ValidFrom`, `ValidTo` | `date`, `date NULL` | Regula 4. `IsTemporary` = 0: `ValidTo` gol, completat automat când apare o linie permanentă nouă pe aceeași țintă și prag. `IsTemporary` = 1: `ValidTo` obligatoriu, scris de utilizator; nu închide alte linii |
| `IsTemporary` | `bit` | Linie pe durată determinată (promoție de câteva zile, preț de campanie). Două linii temporare pe aceeași țintă și prag nu se pot suprapune (`52119`); o linie temporară nu poate începe înaintea primei linii permanente a țintei, ca să existe întotdeauna un preț la care să se revină |
| `ReasonCodeId`, `Notes` | `int NULL`, `nvarchar(300) NULL` | Motivul (opțional) și nota, afișate în istoric |
| `PriceChangeId` | `int NULL` | Modificarea din care vine linia (3.3) |

Exemple (Exemplu Distribuție SRL, prețuri fără TVA, UM de bază bucata):

| Listă | Cum e făcută | APA05 Apă minerală 0,5 L | APA15 Apă minerală 1,5 L | CEAI05 Ceai rece piersici 0,5 L | APA2N Apă plată 2 L |
| --- | --- | --- | --- | --- | --- |
| `BAZA` Prețuri de bază distribuție (implicită) | Prețuri proprii; precizie 2; `RequiresApproval` = 1 | 1,80 (variantă) | 3,05 (variantă) | 3,22 (model „Ceai rece 0,5 L”, toate aromele) | 4,10; de la 600 buc 3,95 |
| `HORECA` HoReCa | Părinte `BAZA` + 3 %, rotunjire `R005` (la 0,05) | 1,85 (1,854 → 1,85) | 3,15 (3,1415 → 3,15) | 3,30 (3,3166 → 3,30) | 4,20 (preț propriu) |
| `RETAIL-CJ` Magazin Cluj, cu amănuntul | Cu TVA, RON, rotunjire `E49` (terminații ,49 și ,99); gestiunea `MAG-CJ` | 2,49 cu TVA 11 % | 4,49 cu TVA 11 % | 4,99 cu TVA 21 % | 6,99 cu TVA 11 % |
| `EXPORT-EUR` Export | EUR, fără părinte | 0,38 EUR | 0,64 EUR | 0,68 EUR | 0,86 EUR |

Clienții din grupa `RETAIL` (de exemplu Mega Retail SRL) lucrează pe lista `BAZA`, cu discountul din contractul lor (3.6). Clienții din grupa `HORECA` lucrează pe lista `HORECA`. Un client poate avea o listă proprie (3.1.1).

**Cum se alege linia la o dată** (regula 4): dintre liniile aceleiași ținte și aceluiași prag valabile la data prețului (`ValidFrom` ≤ dată și `ValidTo` gol sau ≥ dată), câștigă linia temporară, dacă există; altfel linia permanentă. Exemplu pe lista `HCJ-2026` a lui Cafe Unirii SRL, varianta APA05:

| Linie | Fel | Valabilă | Preț | Ce se vinde |
| --- | --- | --- | --- | --- |
| 1 | Permanentă | 01.01.2026 – 27.02.2026 (închisă de linia 2) | 1,74 | — |
| 2 | Permanentă | 28.02.2026 – 31.05.2026 (închisă de linia 4) | 1,78 | 28.02 și de la 04.03 la 31.05: 1,78 |
| 3 | Temporară | 01.03.2026 – 03.03.2026 (campanie „Mărțișor”) | 1,59 | 01.03–03.03: 1,59; pe 04.03 revine singur la 1,78 |
| 4 | Permanentă | din 01.06.2026 | 1,85 | de la 01.06: 1,85 (deci CV 4433 din 27.11 are 1,85) |

Istoricul complet (toate liniile, cu cine și când le-a creat, din ce modificare vin și motivul) se vede în tab-ul „Istoric” al listei și pe fiecare articol (`v2-10`), iar raportul „Istoric de preț” îl exportă.

#### 3.1.1 Listele de prețuri pe partener

O listă cu `PartnerId` completat e **lista proprie a partenerului**: se creează și se vede din fișa partenerului (tab-ul Vânzări › Prețuri) și din Vânzări › Prețuri › Liste de prețuri, cu filtrul „pe partener”.

- Un partener poate avea câte o listă proprie activă pe fiecare monedă (de exemplu `HCJ-2026` „HoReCa Cluj 2026” a lui Cafe Unirii SRL în RON și, dacă ar fi nevoie, una în EUR), `52120` la a doua în aceeași monedă.
- Lista proprie are de regulă ca părinte lista grupei clientului, cu ajustare 0 %: ține doar prețurile negociate cu partenerul (5 linii la Cafe Unirii), iar restul articolelor vin din părinte. Poate avea și ajustare (de exemplu „HoReCa − 2 %” pentru un client mare).
- Precedența (regula 1): lista proprie în moneda documentului câștigă în fața listei din condițiile clientului. Câmpul `CustomerTerms.PriceListId` rămâne pentru clienții care folosesc o listă generală (de exemplu un lanț întreg pe aceeași listă).
- O listă proprie nu se poate atribui altui client, grupe sau gestiuni (`52120`) și nu poate fi părinte pentru altă listă.
- Are aceleași reguli ca orice listă: istoric complet, linii permanente și temporare, praguri, rotunjire, modificări aprobate dacă `RequiresApproval` = 1.
- Prețurile de la furnizori sunt deja pe partener în Achiziții (A1 §3.6, `SupplierPriceList`); V2 acoperă partea de client.

### 3.2 Regulile de rotunjire (`PriceRoundingRule`)

Nomenclator cu seed (MIG-06, [seed/nomenclatoare-vanzari/](../../seed/nomenclatoare-vanzari/)), editabil: `Code`, `Name`, `Step` (pasul), `Ending` (terminația, opțională), `Direction` (1 la cel mai apropiat, cu jumătatea în sus · 2 în sus · 3 în jos).

Formula, cu `p` prețul calculat: fără terminație, `p` se rotunjește la multiplu de `Step` în sensul ales; cu terminație, prețul devine cel mai mic `k × Step + Ending` ≥ `p` (sens 2) sau cel mai mare ≤ `p` (sens 3).

| Cod | Denumire | Pas | Terminație | Sens | Exemplu (2,31) |
| --- | --- | --- | --- | --- | --- |
| `R001` | La 0,01 (normal) | 0,01 | — | apropiat | 2,31 |
| `R005` | La 0,05 | 0,05 | — | apropiat | 2,30 |
| `R010` | La 0,10 | 0,10 | — | apropiat | 2,30 |
| `R100` | La leu întreg | 1 | — | apropiat | 2,00 |
| `E99` | Terminație ,99 (în sus) | 1 | 0,99 | sus | 2,99 |
| `E90` | Terminație ,90 (în sus) | 1 | 0,90 | sus | 2,90 |
| `E49` | Terminații ,49 sau ,99 (în sus) | 0,50 | 0,49 | sus | 2,49 |

Rotunjirea listei se aplică doar prețurilor calculate (din părinte sau dintr-o modificare procentuală). Prețul unitar al liniei de document, după discounturi, se rotunjește la precizia listei (de regulă 2 zecimale), nu cu regula listei.

### 3.3 Modificările de preț (`PriceChange`)

Schimbarea prețurilor unei liste cu `RequiresApproval` = 1 se face printr-un document de modificare, nu direct pe linie (P3, decizia 3):

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Number`, `PriceListId`, `Name` | Seria internă `MPV` (V1 §3.1); o modificare privește o singură listă |
| `EffectiveFrom`, `EffectiveTo` | Data de la care intră în vigoare liniile noi; azi sau în viitor, niciodată în trecut (`52108`). Cu `EffectiveTo` completat, modificarea e **temporară**: liniile noi sunt temporare (regula 4) și, după `EffectiveTo`, prețurile revin singure la cele permanente |
| `Method` | 1 Procent (+/−) · 2 Sumă (+/−) · 3 Preț nou scris pe fiecare rând · 4 Copiere din altă listă, cu procent |
| `Value`, `SourcePriceListId`, `RoundingRuleId` | Procentul sau suma; lista sursă la metoda 4; rotunjirea (implicit a listei) |
| Filtrul | Familia, modelul, grupa de articole, grupa de valori sau variantele alese; furnizorul principal (Achiziții A1) |
| `Status` | 1 Ciornă · 2 De aprobat · 3 Aprobată (programată) · 4 Aplicată (a intrat în vigoare) · 9 Anulată |
| `PreparedBy`, `ApprovedBy`, `ApprovedAt` | Cine pregătește nu aprobă (`52116`) |

Rândul (`PriceChangeLine`): ținta (ca la linia de listă), `MinQty`, prețul vechi (în vigoare la `EffectiveFrom`), prețul nou calculat, prețul nou final (poate fi corectat de mână pe rând), costul curent (S3, informativ), marja veche și marja nouă, `IsExcluded`.

La aprobare, procedura scrie liniile noi în listă cu `ValidFrom` = `EffectiveFrom` și, la o modificare permanentă, închide liniile permanente vechi (regula 4), în aceeași tranzacție; la o modificare temporară nu închide nimic. Previzualizarea arată și efectul în listele copil (liniile lor calculate din părinte se schimbă singure; liniile lor proprii nu) și numărul de clienți care folosesc lista. Comenzile deja confirmate își păstrează prețul (regula 8).

**Lista cu amănuntul.** La aprobarea unei modificări pe o listă cu `Kind` = 2, Vânzările cer Stocurilor câte un document de modificare a prețului (`MP`, S3 §3.7) în ciornă pentru fiecare gestiune magazin legată de listă, cu data `EffectiveFrom` și stocul de la acea dată. Gestionarul magazinului îl validează după reetichetare. Până atunci POS-ul vinde la prețul nou (primit prin eveniment), iar diferența de evaluare se regularizează la validarea MP.

### 3.4 Căutarea prețului de listă (`sales.fn_VariantPrice` și `IPriceEngine`)

Intrare: firma, lista documentului, data prețului, varianta, UM-ul liniei, cantitatea, moneda documentului.

1. **Data prețului** e data documentului (setarea `PriceDateBasis` = 1, implicit) sau data de livrare cerută pe linie (= 2, pentru comenzile cu livrare peste câteva săptămâni, când lista se schimbă între timp).
2. Cantitatea se convertește în UM de bază cu factorul din setul articolului.
3. În lista documentului se caută, la data prețului, linia cu ținta cea mai specifică (regula 3), apoi pragul cel mai mare ≤ cantitatea (regula 5), apoi, pe aceeași țintă și prag, linia temporară valabilă la dată înaintea celei permanente (regula 4).
4. Dacă lista nu are nicio linie aplicabilă, se trece la părinte; prețul găsit se înmulțește cu `(1 + ParentAdjustPct / 100)` la fiecare nivel urcat și se rotunjește cu regula listei copil. Cel mult 3 niveluri.
5. Dacă lista are TVA inclus și documentul e fără TVA (sau invers), prețul se convertește cu cota variantei la data prețului.
6. Prețul pe UM de bază se înmulțește cu factorul UM-ului liniei (de exemplu 1,85 × 12 = 22,20 lei pe bax de APA05) și se rotunjește la precizia listei.
7. Dacă moneda listei diferă de moneda documentului, prețul se convertește la cursul BNR al datei prețului (Nucleu fluxul 4.3), iar linia primește semnul „convertit”, cu cursul folosit.
8. Fără preț: linia rămâne fără preț de listă și apare ca excepție (3.9). Utilizatorul cu `Sales.Price.Override` scrie prețul; fără drept, documentul nu se confirmă (`52106`).

Rezultatul (`PriceSource`) se scrie pe linie: lista și linia găsită, nivelul (variantă, grupă, model), adâncimea în lanț, ajustarea, conversiile. Ecranul „De unde vine prețul” (`v2-06`) îl afișează pas cu pas.

### 3.5 Prețul modificat manual

Cu dreptul `Sales.Price.Override`, utilizatorul poate scrie direct prețul unitar pe o linie (de exemplu un preț negociat la telefon). Sistemul păstrează prețul de listă, calculează discountul echivalent („manual, din preț”) și aplică aceleași verificări ca la discountul manual (3.9). Prețul 0 pe o linie fără bonus în marfă cere un motiv de tip 6 (V1 §3.8, `52013`) și se raportează separat (gratuități, mostre).

### 3.6 Regulile de discount (`DiscountRule`)

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Code`, `Name` | Codul unic pe firmă; numele apare pe document la prezentarea „linie separată” și în detaliul liniei |
| `Kind` | 1 **Automată** (se aplică singură dacă se îndeplinesc condițiile; nu se poate scoate) · 2 **Opțională** (o adaugă utilizatorul, dacă rolul lui e în lista regulii) |
| `IsEditable`, `MaxValue` | Doar la opționale: utilizatorul alege valoarea între 0 și `MaxValue` |
| `Level` | 1 **Pe linie** · 2 **Pe document** (se aplică pe valoarea netă a liniilor eligibile, după discounturile de linie, și se repartizează proporțional pe ele) |
| `Calc` | 1 Procent · 2 Sumă pe unitate de bază (în moneda regulii) · 3 Sumă pe document (doar `Level` = 2) · 4 **Bonus în marfă** (3.6.1) |
| `Value` | Procentul sau suma; când regula are praguri (`DiscountRuleTier`), valoarea vine din pragul atins |
| `TierBasis` | 1 Cantitatea liniei · 2 Cantitatea cumulată pe document a articolelor eligibile · 3 Valoarea netă a liniei · 4 Valoarea netă a documentului |
| `IsPromotion`, `ExclusiveGroup` | Promoție (regula 7 din 3.7); grupul de exclusivitate: din regulile automate ale aceluiași grup, pe o linie se aplică doar cea mai avantajoasă pentru client |
| `Priority` | Ordinea de aplicare în cascadă (mai mic = mai întâi); la egalitate, `Id` |
| `Presentation` | 1 Inclus în preț · 2 Afișat pe linie · 3 Linie separată de discount · gol = setarea firmei (3.10) |
| `DocumentTypes` | Pe ce documente se aplică (bife: ofertă, comandă, proformă, factură fără sursă, factură cu livrare). Avizele și facturile generate preiau discounturile din sursă (regula 8) |
| `PaymentMethodId`, `MinDocumentAmount` | Condiții opționale pe document: modalitatea de plată (de exemplu 2 % la plata pe loc) și valoarea minimă netă |
| `RoleIds` (tabel de legătură) | Rolurile care pot adăuga regula opțională; la automate, gol |
| `ValidFrom`, `ValidTo`, `Status` | 1 Ciornă · 2 Activă · 3 Suspendată · 4 Expirată (automat, după `ValidTo`) |

**Condițiile** (`DiscountRuleTarget`): `TargetKind` și `TargetId`, cu `IsExcluded`. Tipuri: pe client (1 client, 2 grupă comercială, 3 canal, 4 zonă, cu subzonele, 5 agent, 6 punct de livrare, 7 listă de prețuri a documentului, 8 tranzacție) și pe articol (9 variantă, 10 model, 11 familie, 12 grupă de valori, 13 grupă de articole, cu subgrupele). Regula de combinare: în cadrul aceluiași tip, oricare (SAU); între tipuri diferite, toate (ȘI); o excludere câștigă întotdeauna. O regulă fără condiții pe client se aplică tuturor clienților; fără condiții pe articol, tuturor articolelor.

**Pragurile** (`DiscountRuleTier`): `Threshold` (cantitate în UM de bază sau valoare în moneda regulii, după `TierBasis`) și `Value`. Pragurile sunt crescătoare și unice (`52109`); se aplică pragul cel mai mare atins, pe toată cantitatea (nu în trepte).

Exemple (Exemplu Distribuție SRL):

| Cod | Regula | Tip | Condiții | Valoare | Prezentare |
| --- | --- | --- | --- | --- | --- |
| `DR-MR26` | Contract Mega Retail 2026 | Automată, pe linie, procent | Client Mega Retail SRL; toate articolele | 10 % | Afișat pe linie |
| `PR-CEAI-NOV` | Promoție ceai rece HoReCa noiembrie | Automată, promoție, pe linie | Grupa HORECA; modelul „Ceai rece 0,5 L” | 12 %, 01–30.11.2026 | Afișat pe linie |
| `BON-APA15` | 5+1 baxuri APA15 HoReCa | Automată, bonus în marfă | Grupa HORECA; varianta APA15 | la 30 buc, 6 buc gratuite | Linie de bonus |
| `VOL-APA` | Volum apă (prag pe document) | Automată, pe linie, cu praguri | Grupa DISTRIB; familia „Apă minerală”; `TierBasis` = 2 | 2 % de la 2.400 buc, 3,5 % de la 6.000 buc | Inclus în preț |
| `DOC-5000` | Comandă mare telesales | Opțională, pe document, editabilă | Canal TEL; `MinDocumentAmount` 5.000 lei; rolul Telesales | până la 1,5 % | Linie separată |
| `CASH-2` | Plată pe loc | Automată, pe document | Modalitatea de plată `NUM` sau `POS-T` | 2 % | Linie separată |

#### 3.6.1 Bonusul în marfă

Câmpuri suplimentare la `Calc` = 4: `BuyQty` (cantitatea de cumpărat, în UM de bază), `FreeItemVariantId` (gol = același articol), `FreeQty` (cantitatea gratuită la fiecare `BuyQty`), `MaxFreeQty` (plafon pe document, opțional), `FreeGoodsVatTreatment`.

Exemplu: Hotel Central SRL comandă 360 buc APA15 (60 de baxuri de 6). Regula `BON-APA15` („5+1 baxuri”: un bax gratuit la 5 baxuri cumpărate) are `BuyQty` = 30 buc și `FreeQty` = 6 buc; la 360 buc rezultă 360 / 30 × 6 = **72 buc** (12 baxuri) gratuite. Motorul adaugă o **linie de bonus** legată de linia care a generat-o: aceeași variantă, 72 buc, prețul de listă 3,15, discount 100 % (`BON-APA15`), prețul unitar 0. Linia de bonus se rezervă și se livrează ca orice linie (V3, V4) și nu se poate modifica manual (doar prin cantitatea liniei sursă).

Tratamentul fiscal al bonusului (`FreeGoodsVatTreatment`): 1 **reducere de preț acordată în cadrul vânzării** (implicit: linia are bază de TVA 0, ca orice reducere comercială din factură, Codul fiscal art. 286 alin. (4) lit. a) · 2 **livrare fără plată**, cu TVA colectat la cost (art. 270 alin. (4)), pentru bonusurile care nu țin de vânzarea respectivă (de exemplu mostre pentru alt articol). Alegerea pe regulă se confirmă cu contabilul firmei (decizia 7).

### 3.7 Ordinea de calcul (motorul de preț)

Pentru fiecare linie, în ordine:

1. **Prețul de listă** (3.4).
2. **Discounturile automate pe linie** care îndeplinesc condițiile la data prețului. Dacă setarea `PromotionsExclusive` = 1 și cel puțin o promoție se aplică liniei, celelalte discounturi automate nepromoționale ale liniei cad. Din fiecare grup de exclusivitate rămâne doar regula cea mai avantajoasă pentru linie. Restul se aplică în ordinea `Priority`, **în cascadă** (fiecare pe prețul rămas după cele dinainte, setarea `LineDiscountCombination` = 1, implicit) sau **însumate** (procentele se adună și se aplică o dată, = 2).
3. **Discounturile opționale pe linie** alese de utilizator, în aceeași combinare.
4. **Discountul manual pe linie** (procent), ultimul în cascadă.
5. **Bonusul în marfă**: liniile de bonus, după cantitatea finală a liniei sursă.
6. **Discounturile pe document** (automate, apoi opționale, apoi manualul pe document), în cascadă, pe valoarea netă a liniilor eligibile (fără liniile de bonus). Fiecare se repartizează proporțional pe linii, la 2 zecimale, cu diferența de rotunjire pe linia cea mai mare, ca marja și TVA-ul pe cote să fie exacte.
7. **Rotunjirea**: prețul unitar al liniei, la precizia listei; valoarea netă = cantitate × preț unitar, rotunjită la 2 zecimale, minus partea repartizată din discounturile pe document.
8. **Verificările** (3.9): plafoane, marjă, cost, preț lipsă. Rezultatul: lista de excepții și nivelul de aprobare cerut.

Discounturile aplicate se scriu în `sales.DocumentLineDiscount`, în ordinea de aplicare, cu regula, procentul, suma pe unitate și suma totală. Cele pe document au și un rând pe antet (`LineId` gol), cu suma totală.

**Prezentarea pe documentul tipărit și în e-Factura** (setarea `DefaultDiscountPresentation`, suprascrisă pe regulă):

| Prezentare | Pe PDF | În e-Factura (UBL, CIUS-RO) |
| --- | --- | --- |
| 1 Inclus în preț | Doar prețul unitar final | `PriceAmount` = prețul unitar; fără reduceri |
| 2 Afișat pe linie (implicit) | Preț de listă, discount %, preț unitar | Prețul brut (BT-148) = prețul de listă, reducerea de preț (BT-147) = diferența, prețul net (BT-146) = prețul unitar |
| 3 Linie separată | Linie „Discount …” cu valoare negativă, pe fiecare cotă de TVA | Reducere la nivel de document (BG-20), câte una pe categorie și cotă de TVA, cu motivul (BT-97) = numele regulii |

### 3.8 Discountul manual și plafoanele

- **Plafonul agentului** (`SalesAgent.MaxDiscountPct`, V1 §3.7) limitează discountul manual pe linie și pe document, inclusiv cel calculat din prețul scris de mână (3.5). Pentru utilizatorii care nu sunt agenți se folosește plafonul rolului (`SalesApprovalRule` cu nivelul 0, 3.9).
- Plafonul se compară cu **discountul manual al liniei** (nu cu totalul discounturilor), ca o promoție automată să nu consume plafonul agentului.
- **Plafonul total** al firmei (`MaxTotalDiscountPct`, implicit 30 %) se compară cu totalul discounturilor față de prețul de listă, pe linie (fără liniile de bonus); peste el, aprobare, oricine ar fi acordat discountul.
- Discountul manual pe document se repartizează pe linii și se adună la discountul manual al fiecărei linii pentru verificarea plafonului.

### 3.9 Verificările de preț și aprobarea excepțiilor

| Excepție | Când | Ce se întâmplă |
| --- | --- | --- |
| 1 Plafonul de discount manual depășit | Discountul manual al liniei > plafonul agentului sau al rolului | Aprobare de la nivelul care acoperă procentul |
| 2 Plafonul total depășit | Totalul discounturilor liniei > `MaxTotalDiscountPct` | Aprobare de la nivelul care acoperă procentul |
| 3 Marja sub minim | Marja liniei < `MinMarginPct` (implicit 8 %) sau prețul unitar < `MinPrice` din listă | După `BelowMarginAction`: 1 avertizare · 2 aprobare (implicit) · 3 blocare (`52113`) |
| 4 Preț sub cost | Prețul unitar (în RON, fără TVA) < costul unitar curent | După `BelowCostAction`: 2 aprobare cu motiv de tip 6 (implicit) · 3 blocare (`52114`) |
| 5 Preț lipsă | Nicio listă nu dă preț | Prețul se scrie cu `Sales.Price.Override`; altfel documentul nu se confirmă (`52106`) |

**Marja** = (prețul unitar în RON fără TVA − costul unitar) / prețul unitar. Costul unitar vine din Stocuri (`IStockValuationQuery.GetUnitCostAsync`, S3), pe gestiunea liniei și la data documentului: costul mediu la CMP, costul primului strat disponibil la FIFO. Liniile de bonus se verifică împreună cu linia sursă (marja combinată). Articolele nestocabile (servicii) au marja verificată doar dacă au cost standard (S3 §3.8). Costul și marja se văd doar cu dreptul `Sales.Price.ViewCost`; un agent fără drept vede doar semnul „marjă sub minim”.

**Regulile de aprobare** (`SalesApprovalRule`, `Kind` = 1 excepții de preț; `Kind` = 2 pentru oferte și comenzi pe valoare și termen de plată, cu coloanele `AppliesTo`, `MaxNetAmountRon`, `MaxExtraPaymentDays` și 4 reguli livrate, e descris în V3 §3.8; precizare din V3, 27.11.2026; `Kind` = 3 pentru notele de credit fără retur fizic, cu plafonul `MaxNetAmountRon` pe rol: nivelul 0 Operator facturare 1.000 lei, nivelul 1 Șef vânzări 25.000 lei, nivelul 2 Director comercial fără limită, descris în V6 §3.8; precizare din Vânzări V6, 27.11.2026): `Level` (0–3), `RoleId`, `MaxManualDiscountPct`, `MaxTotalDiscountPct`, `MinMarginPct`, `AllowBelowCost`. Nivelul 0 e plafonul fără aprobare al rolului. Pentru fiecare excepție se caută cel mai mic nivel care o acoperă; documentul cere aprobarea celui mai mare nivel necesar dintre toate excepțiile lui. Reguli livrate la activarea modulului (editabile): nivelul 0 Agent de teren și Telesales 5 %, Key account 7 %; nivelul 1 Șef vânzări: manual până la 15 %, total până la 35 %, marja până la 3 %, sub cost cu motiv; nivelul 2 Director comercial (rol nou, opțional): fără limită.

**Circuitul**: documentul cu excepții nu se poate confirma (oferta nu se poate trimite, factura fără sursă nu se poate valida) până la aprobare (`52115`). „Cere aprobare” trimite o notificare aprobatorilor nivelului necesar (Nucleu fluxul 4.7), cu liniile și excepțiile. Aprobatorul aprobă sau respinge cu comentariu; nu modifică prețurile (ca la Achiziții A3: forma finală e a celui care vinde). Autorul nu își aprobă propriul document (`52116`). Jurnalul (`SalesApproval`) e doar prin adăugare. O modificare după aprobare care mărește un discount sau scade marja sub valoarea aprobată readuce documentul în „De aprobat”; o modificare în favoarea firmei nu.

### 3.10 Setările de prețuri ale firmei (completare la `SalesSettings`, V1 §3.9)

| Setare | Implicit | Ce înseamnă |
| --- | --- | --- |
| `DefaultPriceListId` | `BAZA` | Lista implicită a firmei (ultima în precedență, V1 §3.4) |
| `PriceDateBasis` | 1 | 1 data documentului · 2 data de livrare cerută pe linie (3.4) |
| `LineDiscountCombination` | 1 | 1 cascadă · 2 însumare (3.7) |
| `PromotionsExclusive` | 1 | O promoție pe linie anulează celelalte discounturi automate nepromoționale ale liniei |
| `DefaultDiscountPresentation` | 2 | 1 inclus în preț · 2 afișat pe linie · 3 linie separată (3.7) |
| `MaxTotalDiscountPct` | 30 | Plafonul total pe linie, peste care e nevoie de aprobare (3.8) |
| `MinMarginPct` | 8 | Marja minimă pe linie (3.9) |
| `BelowMarginAction` | 2 | 1 avertizare · 2 aprobare · 3 blocare |
| `BelowCostAction` | 2 | 2 aprobare cu motiv · 3 blocare |
| `RequireOverrideReason` | 0 | 1 = prețul scris de mână cere motiv de tip 6 |
| `RebateAccrualEnabled` | 1 | Estimarea zilnică a rabatului pe rulaj (3.11) |

### 3.11 Acordurile de rabat pe rulaj (`RebateAgreement`)

Rabatul pe rulaj (bonus de volum la final de lună, trimestru sau an) nu se poate pune pe factura curentă, pentru că nu se știe dinainte pragul atins. Se ține ca acord:

| Câmp | Reguli |
| --- | --- |
| `CompanyId`, `Code`, `Name` | Unic pe firmă |
| `PartnerId` | Clientul; la lanțuri cu mai multe firme client, plătitorul desemnat (V1 §3.4), cu clienții lui |
| `PeriodKind`, `ValidFrom`, `ValidTo` | 1 lună · 2 trimestru · 3 an; perioadele se calculează în intervalul acordului |
| `Basis` | 1 valoarea netă facturată (fără TVA, după storno și retururi) · 2 cantitatea în UM de bază |
| Condiții pe articol | Aceleași tipuri ca la discounturi (9–13), în `RebateAgreementTarget` |
| `IsRetroactive` | 1 = procentul pragului atins se aplică întregului rulaj al perioadei (implicit); 0 = doar peste prag, în trepte |
| Praguri (`RebateTier`) | `Threshold` și `Pct`, crescătoare |
| `Status` | Ciornă · Activ · Încheiat |

Exemplu: acordul `RB-MR-Q4` cu Mega Retail SRL, trimestrul 4 2026, pe valoarea netă facturată din toate articolele: 2 % de la 150.000 lei, 3 % de la 250.000 lei, retroactiv. La 27.11.2026, rulajul trimestrului e 186.420,00 lei: pragul atins e 2 %, rabatul estimat 3.728,40 lei; până la pragul de 3 % lipsesc 63.580,00 lei.

Un job zilnic (Nucleu, joburi) calculează rulajul și rabatul estimat pe acorduri active și le afișează pe fișa clientului și în ecranul `v2-07`. La închiderea perioadei, V6 propune nota de reducere `FS-RED` pe valoarea finală, cu repartizarea pe cotele de TVA ale facturilor din perioadă. Decontul (`RebateSettlement`) îl calculează jobul V6 în ziua `RebateSettlementDay` a lunii de după perioadă, îl aprobă șeful de vânzări, iar nota se contează pe `709` (V6 §3.5 și 4.4; în exemplul V6, rulajul final al trimestrului e 268.900,00 lei, deci 3 %, adică 8.067,00 lei) (precizare din Vânzări V6, 27.11.2026). Estimarea nu se contează în V2; dacă firma vrea provizion lunar pentru rabaturi, îl face contabilul în C2 pe baza raportului (decizia 9).

## 4. Fluxuri principale

### 4.1 Prețul unei linii de comandă (exemplu complet)

Pe 27.11.2026, Vlad Georgescu (Agent de teren, plafon de discount 5 %) introduce comanda `CV 4441` pentru Hotel Central SRL (grupa HORECA, fără listă proprie, deci lista `HORECA`), livrare din DEP-CJ. Costurile curente (S3, CMP) sunt: APA05 1,21 lei, APA15 2,05 lei, CEAI05 1,94 lei.

| # | Articol | Cantitate | Preț de listă | Discounturi | Preț unitar | Valoare netă |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | APA05 | 480 buc | 1,85 (HORECA ← BAZA 1,80 + 3 %, `R005`) | manual 8 % | 1,70 (1,702 rotunjit) | 816,00 |
| 2 | CEAI05 | 288 buc | 3,30 (HORECA ← BAZA, modelul 3,22 + 3 %) | `PR-CEAI-NOV` 12 % | 2,90 (2,904 rotunjit) | 835,20 |
| 3 | APA15 | 360 buc | 3,15 (HORECA ← BAZA 3,05 + 3 %) | — | 3,15 | 1.134,00 |
| 4 | APA15, bonus la linia 3 | 72 buc | 3,15 | `BON-APA15` 100 % | 0,00 | 0,00 |

1. La alegerea clientului, motorul fixează lista `HORECA` și data prețului 27.11.2026.
2. Linia 1: `HORECA` nu are linie pe APA05, deci se urcă la `BAZA` (1,80, pe variantă), se aplică +3 % = 1,854 și regula `R005` → 1,85. Nicio regulă automată nu se aplică. Vlad scrie discount manual 8 %: 1,85 × 0,92 = 1,702 → 1,70 (efectiv 8,11 %). Plafonul lui e 5 %, deci apare excepția 1. Marja: (1,70 − 1,21) / 1,70 = 28,8 %, peste 8 %.
3. Linia 2: prețul vine de pe model (toate aromele de ceai 0,5 L): 3,22 × 1,03 = 3,3166 → 3,30. Promoția `PR-CEAI-NOV` e exclusivă, deci un eventual discount automat nepromoțional ar cădea. 3,30 × 0,88 = 2,904 → 2,90. Marja 33,1 %.
4. Linia 3: 360 buc, regula `BON-APA15` (6 buc la fiecare 30 buc) adaugă linia 4 cu 72 buc la preț 0. Marja combinată a liniilor 3 și 4: (1.134,00 − 432 × 2,05) / 1.134,00 = (1.134,00 − 885,60) / 1.134,00 = 21,9 %.
5. Discountul opțional pe document `DOC-5000` nu e disponibil: valoarea netă 2.785,20 lei e sub 5.000 lei și Vlad nu e telesales (ecranul arată „nu se aplică: valoare sub 5.000 lei”).
6. Totaluri: valoarea de listă 3.199,20 lei (888,00 + 950,40 + 1.134,00 + 226,80 pentru cele 72 buc de bonus), discounturi 414,00 lei (72,00 manual, 115,20 promoție, 226,80 bonus), valoarea netă 2.785,20 lei. TVA: 11 % pe apă (816,00 + 1.134,00 = 1.950,00 → 214,50) și 21 % pe ceai (835,20 → 175,39), total TVA 389,89 lei, total comandă 3.175,09 lei.
7. „Confirmă” e blocat de excepția 1 (`52115`). Vlad apasă „Cere aprobare”, cu nota „client nou pe apă, concurența la 1,70”. Radu Stan (Șef vânzări, nivelul 1: până la 15 %) primește notificarea, vede linia, marja și istoricul clientului, și aprobă. Comanda se poate confirma; aprobarea apare în jurnal și în raportul de excepții.

### 4.2 Scumpirea programată a apei

Pe 27.11.2026, Cristina Marin pregătește modificarea `MPV 0014` „Scumpire apă minerală +4 % de la 01.12.2026” pe lista `BAZA`: metoda procent, +4 %, familia „Apă minerală”, rotunjire `R001`.

| Variantă | Prag | Preț vechi | Preț nou | Cost | Marjă veche | Marjă nouă |
| --- | --- | --- | --- | --- | --- | --- |
| APA05 | 0 | 1,80 | 1,87 | 1,21 | 32,8 % | 35,3 % |
| APA15 | 0 | 3,05 | 3,17 | 2,05 | 32,8 % | 35,3 % |
| APA05N | 0 | 1,80 | 1,87 | 1,23 | 31,7 % | 34,2 % |
| APA2N | 0 | 4,10 | 4,26 | 2,74 | 33,2 % | 35,7 % |
| APA2N | 600 | 3,95 | 4,11 | 2,74 | 30,6 % | 33,3 % |

Previzualizarea arată și efectul în listele copil: în `HORECA`, APA05 trece de la 1,85 la 1,95 (1,87 × 1,03 = 1,9261 → 1,95) și APA15 de la 3,15 la 3,25; APA2N rămâne 4,20 (preț propriu). Lista `BAZA` e folosită direct de 118 clienți, iar prin `HORECA` (și `HCJ-2026`) de încă 64 de clienți; 23 de comenzi confirmate își păstrează prețul. Cristina trimite la aprobare; Radu Stan aprobă. Pe 01.12.2026 liniile noi intră în vigoare; comenzile confirmate înainte își păstrează prețul.

### 4.3 Modificarea prețului cu amănuntul

Gestionarul Magazinului Cluj vrea APA05 la 2,29 lei cu TVA de la 01.12 (campanie). Pe lista `RETAIL-CJ` se face modificarea `MPV 0015`, metoda preț nou, 2,29. La aprobare, Vânzările cer documentul `MP` în ciornă pentru gestiunea `MAG-CJ` (S3), cu stocul de APA05 la 01.12 și diferența (2,49 − 2,29) × cantitate. POS-ul primește prețul nou prin `PreturiVanzareModificate`. Gestionarul validează MP-ul după reetichetare.

### 4.4 Prețul temporar al unui client

Pe 25.02.2026, Vlad Georgescu negociază cu Cafe Unirii SRL un preț de campanie la APA05 pentru 01–03.03.2026. Pe lista proprie `HCJ-2026` (fără aprobare), adaugă o linie temporară: APA05, 1,59 lei, 01.03.2026–03.03.2026, motivul `CAMPANIE`. Linia permanentă din 28.02.2026 (1,78) rămâne neatinsă. O comandă din 02.03 primește 1,59; una din 04.03 primește din nou 1,78, fără ca cineva să reintroducă prețul. În tab-ul Istoric al articolului se văd toate cele 4 linii din 3.1, cu perioada fiecăreia și cu „prețul în vigoare azi”. Pe o listă cu `RequiresApproval` = 1, același lucru se face cu o modificare temporară (3.3, `EffectiveTo`), iar pe lista cu amănuntul se cer două documente MP: unul la începutul și unul la sfârșitul perioadei (S3).

### 4.5 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Lista copil și lista părinte au linie pe același articol, pe niveluri diferite (copilul pe model, părintele pe variantă) | Câștigă copilul: lanțul se urmează doar când lista curentă nu are nicio linie aplicabilă (regula 3) |
| Prețul se schimbă între ofertă și comandă | Comanda generată din ofertă preia prețul ofertei cât timp oferta e valabilă (V3); după expirare, se recalculează și se semnalează diferența |
| Comandă în EUR pentru un client cu listă în RON | Prețul se convertește la cursul BNR al datei prețului; linia e marcată „convertit”, cu cursul |
| Promoție care începe după data comenzii, dar înainte de livrare | Cu `PriceDateBasis` = 1 nu se aplică (contează data comenzii); cu 2, se aplică liniilor cu livrarea în perioada promoției |
| Discount pe document și retur parțial | Returul și stornarea (V6) preiau discountul repartizat pe linie, proporțional cu cantitatea, deci valoarea stornată e exact cea facturată |
| Bonus în marfă și livrare parțială | Linia de bonus se livrează proporțional cu linia sursă (de exemplu 180 din 360 buc → 36 din 72 buc bonus) |
| Linie de bonus fără stoc | Comanda se poate confirma; bonusul intră în așteptare la rezervare, ca orice linie (S5) |
| Cost necunoscut (articol fără intrări) | Verificarea marjei se sare, linia primește semnul „cost necunoscut”; nu blochează |
| Regulă automată modificată după confirmarea comenzii | Nu atinge comenzile confirmate (regula 8); ciornele se recalculează la următoarea editare |
| Două reguli automate din același grup de exclusivitate | Pe fiecare linie rămâne cea mai avantajoasă pentru client; pe linii diferite pot câștiga reguli diferite |

## 5. Evenimente

### 5.1 Publicate

| Eveniment | Când | Conținut (pe lângă anvelopă) | Consumatori |
| --- | --- | --- | --- |
| `PreturiVanzareModificate` v1 | Intrarea în vigoare a unor linii noi (direct sau din modificare aprobată), pe listă | `CompanyId`, `PriceListId`, `Kind`, `EffectiveFrom`, variantele și prețurile noi (în loturi de cel mult 1.000) | POS, e-commerce, Stocuri (MP pentru listele cu amănuntul), CRM |
| `RegulaDiscountModificata` v1 | Activarea, suspendarea, expirarea sau schimbarea unei reguli | `CompanyId`, `DiscountRuleId`, `Code`, `Status`, `ValidFrom`, `ValidTo` | POS (promoțiile), e-commerce |
| `ExceptiePretAprobata` v1 | Aprobarea sau respingerea excepțiilor unui document | `DocumentRef`, nivelul, aprobatorul, excepțiile | Raportare (jurnalul de excepții, V7) |

### 5.2 Consumate

| Eveniment | De la | Ce face V2 |
| --- | --- | --- |
| `ArticolModificat` v1 | Nucleu | La dezactivarea unei variante, liniile de listă pe ea apar în raportul „Liste incomplete”; la schimbarea cotei de TVA, listele cu TVA inclus primesc o alertă (prețul cu TVA rămâne, prețul fără TVA se schimbă) |
| `FirmaModificata` v1 | Nucleu | La activarea modulului: lista `BAZA` goală, implicită, și regulile de aprobare din 3.9 |
| `FacturaVanzareValidata` v1, `FacturaStornoValidata` v1 | V5, V6 | Rulajul acordurilor de rabat (3.11) |

## 6. Contracte publice (`Erp.Sales.Contracts`, partea V2)

```csharp
public enum PriceListKind : byte { Sales = 1, Retail = 2 }
public enum DiscountKind : byte { Automatic = 1, Optional = 2 }
public enum DiscountLevel : byte { Line = 1, Document = 2 }
public enum DiscountCalc : byte { Percent = 1, AmountPerUnit = 2, AmountPerDocument = 3, FreeGoods = 4 }
public enum DiscountPresentation : byte { IncludedInPrice = 1, ShownOnLine = 2, SeparateLine = 3 }
public enum PriceExceptionKind : byte { ManualDiscountCap = 1, TotalDiscountCap = 2, BelowMargin = 3, BelowCost = 4, MissingPrice = 5 }

public sealed record PriceRequest(int CompanyId, int PartnerId, int? DeliveryPointId, SalesDocumentType DocumentType,
    int TransactionTypeId, int PriceListId, DateOnly DocumentDate, string CurrencyCode, int? PaymentMethodId,
    int? SalesAgentId, int UserId, decimal? HeaderManualDiscountPct, IReadOnlyList<int> HeaderOptionalRuleIds,
    IReadOnlyList<PriceRequestLine> Lines);
public sealed record PriceRequestLine(int LineKey, int ItemVariantId, int UomId, decimal Qty, int WarehouseId,
    DateOnly? RequestedDate, decimal? ManualDiscountPct, decimal? OverrideUnitPrice, IReadOnlyList<int> OptionalRuleIds);

public sealed record PriceSourceDto(int PriceListId, long PriceListLineId, byte Level /* 1 variantă, 2 grupă, 3 model */,
    byte Depth /* 0 lista documentului, 1–3 părinte */, decimal CumulativeAdjustPct, bool IsConverted, decimal? ExchangeRate);
public sealed record AppliedDiscountDto(int? DiscountRuleId, string Code, DiscountLevel Level, DiscountCalc Calc,
    decimal Pct, decimal AmountPerUnit, decimal Amount, short Sequence, bool IsPromotion, bool IsManual);
public sealed record PriceExceptionDto(int LineKey, PriceExceptionKind Kind, decimal Value, decimal Limit, byte RequiredLevel);
public sealed record PricedLineDto(int LineKey, int? ParentLineKey /* linia sursă a unui bonus */, decimal? ListPrice,
    PriceSourceDto? Source, IReadOnlyList<AppliedDiscountDto> Discounts, decimal? UnitPrice, decimal NetAmount,
    decimal? UnitCostRon, decimal? MarginPct, DiscountPresentation Presentation);
public sealed record PricingResult(IReadOnlyList<PricedLineDto> Lines, IReadOnlyList<AppliedDiscountDto> HeaderDiscounts,
    decimal ListAmount, decimal DiscountAmount, decimal NetAmount, IReadOnlyList<PriceExceptionDto> Exceptions,
    byte RequiredApprovalLevel, IReadOnlyList<string> Hints /* de exemplu „DOC-5000: valoare sub 5.000 lei” */);

// Folosit de V3–V6 la ciornă, salvare și validare; de POS și e-commerce la sincronizare
public interface IPriceEngine
{
    Task<PricingResult> PriceAsync(PriceRequest request, CancellationToken ct);
    Task<int> ResolvePriceListAsync(int companyId, int partnerId, int transactionTypeId, CancellationToken ct);
}

// Folosit de Stocuri S1 (intrarea în gestiunea cu amănuntul, 54008), S3 (MP), POS
public interface IPriceListQueries
{
    Task<decimal?> GetListPriceAsync(int priceListId, int itemVariantId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<PriceListExportRow>> ExportAsync(int priceListId, DateOnly date, CancellationToken ct);
}
public sealed record PriceListExportRow(int ItemVariantId, string Sku, decimal MinQty, decimal Price, string CurrencyCode,
    bool PricesIncludeVat, DateOnly ValidFrom, DateOnly? ValidTo);

public interface ISalesApprovalService
{
    Task<ApprovalPlanDto> PlanAsync(DocumentRef document, IReadOnlyList<PriceExceptionDto> exceptions, CancellationToken ct);
    Task RequestAsync(DocumentRef document, string? note, CancellationToken ct);
    Task DecideAsync(DocumentRef document, bool approve, string? comment, CancellationToken ct);
}
```

`IPriceEngine` e o clasă de aplicație care citește datele cu procedurile din 7 (un singur apel pentru toate liniile, cu TVP) și face calculul din 3.7 în C#, ca să fie testabil unitar și identic în ecran și la validare. Procedura `sales.usp_Price_Resolve` întoarce, pentru toate liniile, prețul de listă găsit și sursa lui; `sales.usp_DiscountRule_Candidates` întoarce regulile active care se potrivesc clientului, documentului și articolelor, cu pragurile.

Comenzi interne V2: `SavePriceList`, `SavePriceListLines` (doar pe liste fără `RequiresApproval` sau linii viitoare), `SavePriceChange`, `SubmitPriceChange`, `ApprovePriceChange`, `CancelPriceChange`, `ApplyDuePriceChanges` (job, 00:05), `SaveDiscountRule`, `ActivateDiscountRule`, `SuspendDiscountRule`, `SaveSalesApprovalRule`, `RequestPriceApproval`, `DecidePriceApproval`, `SaveRebateAgreement`, `AccrueRebates` (job zilnic), `SaveRoundingRule`.

## 7. Schema SQL `sales` (partea V2)

| Tabel | Cheie primară | Chei unice / străine principale | Indecși suplimentari |
| --- | --- | --- | --- |
| `sales.PriceList` | `Id int` | UX `(CompanyId, CodeKey)`; UX `(CompanyId, PartnerId, CurrencyCode) WHERE PartnerId IS NOT NULL AND IsActive = 1`; FK `PartnerId`; UX `(CompanyId, CurrencyCode) WHERE IsDefault = 1 AND IsActive = 1 AND Kind = 1`; FK `CompanyId`, `CurrencyCode`, `ParentPriceListId`, `RoundingRuleId`, `PrecisionPolicyId` | `(CompanyId, Kind, IsActive)` |
| `sales.PriceListLine` | `Id bigint` | UX `(PriceListId, TargetKey, MinQty, ValidFrom)`; FK `PriceListId`, `ItemVariantId`, `ItemId`, `ValueListGroupId`, `PriceChangeId` | `(ItemVariantId, PriceListId, ValidFrom) INCLUDE (MinQty, Price, ValidTo)`; `(ItemId, PriceListId) WHERE ItemVariantId IS NULL` |
| `sales.PriceRoundingRule` | `Id int` | UX `(CodeKey)` | — |
| `sales.PriceChange` | `Id int` | UX `(CompanyId, Number)`; FK `PriceListId`, `SourcePriceListId`, `RoundingRuleId`, `PreparedBy`, `ApprovedBy` | `(Status, EffectiveFrom) WHERE Status = 3` |
| `sales.PriceChangeLine` | `Id bigint` | FK `PriceChangeId` (cascadă doar pe ciornă, `SQL-15`), ținta ca la `PriceListLine` | `(PriceChangeId)` |
| `sales.DiscountRule` | `Id int` | UX `(CompanyId, CodeKey)`; FK `PaymentMethodId`, `FreeItemVariantId`, `CurrencyCode` | `(CompanyId, Status, ValidFrom, ValidTo)` |
| `sales.DiscountRuleTarget` | `Id int` | UX `(DiscountRuleId, TargetKind, TargetId)`; FK `DiscountRuleId` | `(TargetKind, TargetId) INCLUDE (DiscountRuleId, IsExcluded)` |
| `sales.DiscountRuleTier` | `Id int` | UX `(DiscountRuleId, Threshold)` | — |
| `sales.DiscountRuleRole` | `(DiscountRuleId, RoleId)` | FK spre `core.Role` | — |
| `sales.DocumentLineDiscount` | `Id bigint` | FK `DiscountRuleId`; `CHECK (Pct BETWEEN 0 AND 100)` | `(DocumentType, DocumentId, LineId)`; `(DiscountRuleId, DocumentType) INCLUDE (Amount)` pentru rapoartele de promoții |
| `sales.SalesApprovalRule` | `Id int` | UX `(CompanyId, Kind, Level, RoleId)` | — |
| `sales.SalesApproval` | `Id bigint` | doar adăugare (`SP-21`); FK `DecidedBy` | `(DocumentType, DocumentId)` |
| `sales.RebateAgreement`, `sales.RebateAgreementTarget`, `sales.RebateTier`, `sales.RebateAccrual` | `Id int` / `bigint` | UX `(CompanyId, CodeKey)`; `RebateAccrual` UX `(AgreementId, PeriodStart, AccrualDate)` | `(CompanyId, Status)` |
| `sales.SalesSettings` (coloane noi) | — | `DefaultPriceListId`, `PriceDateBasis`, `LineDiscountCombination`, `PromotionsExclusive`, `DefaultDiscountPresentation`, `MaxTotalDiscountPct`, `MinMarginPct`, `BelowMarginAction`, `BelowCostAction`, `RequireOverrideReason`, `RebateAccrualEnabled` | — |
| `sales.CustomerTerms`, `sales.CustomerGroup` (V1) | — | FK `PriceListId` spre `sales.PriceList`, adăugat de migrarea V2 | — |
| `inv.Warehouse` (S1) | — | FK `RetailPriceListId` spre `sales.PriceList`, adăugat de migrarea V2; trigger-ul de validare refuză o listă cu `Kind` ≠ 2 | — |

`DocumentLineDiscount` e comun tuturor documentelor de vânzare: `DocumentType` (enumerarea `SalesDocumentType`), `DocumentId`, `LineId` (gol pentru rândul de antet al unui discount pe document), `DiscountRuleId` (gol pentru manual), `IsManual`, `Level`, `Calc`, `Sequence`, `Pct`, `AmountPerUnit`, `Amount`, `IsPromotion`. Liniile documentelor (V3–V6) au coloanele `ListPrice`, `UnitPrice`, `PriceListId`, `PriceListLineId`, `PriceLevel`, `PriceDepth`, `IsPriceConverted`, `IsPriceOverridden`, `ParentLineId` (bonus), `HeaderDiscountAmount`, `UnitCostRon`, `MarginPct`.

Exemple de tabele complete:

```sql
CREATE TABLE sales.PriceListLine (
    Id                bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PriceListLine PRIMARY KEY,
    PriceListId       int               NOT NULL CONSTRAINT FK_PriceListLine_PriceList REFERENCES sales.PriceList(Id),
    ItemVariantId     int               NULL CONSTRAINT FK_PriceListLine_Variant REFERENCES core.ItemVariant(Id),
    ItemId            int               NULL CONSTRAINT FK_PriceListLine_Item REFERENCES core.Item(Id),
    ValueListGroupId  int               NULL CONSTRAINT FK_PriceListLine_ValueGroup REFERENCES core.ValueListGroup(Id),
    TargetKind        AS (CASE WHEN ItemVariantId IS NOT NULL THEN CONVERT(tinyint, 1)
                               WHEN ValueListGroupId IS NOT NULL THEN CONVERT(tinyint, 2)
                               ELSE CONVERT(tinyint, 3) END) PERSISTED,
    TargetKey         AS (CONVERT(bigint, ISNULL(ItemVariantId, 0)) * 4294967296
                          + CONVERT(bigint, ISNULL(ItemId, 0)) * 65536 + ISNULL(ValueListGroupId, 0)) PERSISTED,
    MinQty            decimal(19,6)     NOT NULL CONSTRAINT DF_PriceListLine_MinQty DEFAULT (0),
    Price             decimal(19,6)     NOT NULL,
    MinPrice          decimal(19,6)     NULL,
    ValidFrom         date              NOT NULL,
    ValidTo           date              NULL,
    IsTemporary       bit               NOT NULL CONSTRAINT DF_PriceListLine_IsTemporary DEFAULT (0),
    PriceChangeId     int               NULL CONSTRAINT FK_PriceListLine_PriceChange REFERENCES sales.PriceChange(Id),
    ReasonCodeId      int               NULL CONSTRAINT FK_PriceListLine_Reason REFERENCES sales.ReasonCode(Id),
    Notes             nvarchar(300)     NULL,
    CreatedAt         datetime2(3)      NOT NULL CONSTRAINT DF_PriceListLine_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy         int               NOT NULL,
    ModifiedAt        datetime2(3)      NULL,
    ModifiedBy        int               NULL,
    RowVersion        rowversion        NOT NULL,
    CONSTRAINT CK_PriceListLine_Target CHECK (
           (ItemVariantId IS NOT NULL AND ItemId IS NULL AND ValueListGroupId IS NULL)
        OR (ItemVariantId IS NULL AND ItemId IS NOT NULL)),
    CONSTRAINT CK_PriceListLine_Values CHECK (MinQty >= 0 AND Price >= 0 AND (MinPrice IS NULL OR MinPrice >= 0)),
    CONSTRAINT CK_PriceListLine_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
    CONSTRAINT CK_PriceListLine_Temporary CHECK (IsTemporary = 0 OR ValidTo IS NOT NULL),
    CONSTRAINT UQ_PriceListLine UNIQUE (PriceListId, TargetKey, MinQty, ValidFrom)
);
CREATE INDEX IX_PriceListLine_Variant ON sales.PriceListLine (ItemVariantId, PriceListId, ValidFrom)
    INCLUDE (MinQty, Price, MinPrice, ValidTo) WHERE ItemVariantId IS NOT NULL;
CREATE INDEX IX_PriceListLine_Item ON sales.PriceListLine (ItemId, PriceListId, ValidFrom)
    INCLUDE (ValueListGroupId, MinQty, Price, MinPrice, ValidTo) WHERE ItemVariantId IS NULL;

CREATE TABLE sales.DiscountRule (
    Id                    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DiscountRule PRIMARY KEY,
    CompanyId             int               NOT NULL CONSTRAINT FK_DiscountRule_Company REFERENCES core.Company(Id),
    Code                  nvarchar(20)      NOT NULL,
    CodeKey               AS (UPPER(Code)) PERSISTED,
    Name                  nvarchar(100)     NOT NULL,
    Kind                  tinyint           NOT NULL,   -- 1 automată, 2 opțională
    Level                 tinyint           NOT NULL,   -- 1 linie, 2 document
    Calc                  tinyint           NOT NULL,   -- 1 procent, 2 sumă pe unitate, 3 sumă pe document, 4 bonus în marfă
    Value                 decimal(19,6)     NULL,       -- gol când regula are praguri
    IsEditable            bit               NOT NULL CONSTRAINT DF_DiscountRule_IsEditable DEFAULT (0),
    MaxValue              decimal(19,6)     NULL,
    TierBasis             tinyint           NULL,       -- 1 cantitate linie, 2 cantitate document, 3 valoare linie, 4 valoare document
    CurrencyCode          char(3)           NULL CONSTRAINT FK_DiscountRule_Currency REFERENCES core.Currency(Code),
    IsPromotion           bit               NOT NULL CONSTRAINT DF_DiscountRule_IsPromotion DEFAULT (0),
    ExclusiveGroup        varchar(20)       NULL,
    Priority              smallint          NOT NULL CONSTRAINT DF_DiscountRule_Priority DEFAULT (100),
    Presentation          tinyint           NULL,       -- gol = setarea firmei
    DocumentTypes         tinyint           NOT NULL,   -- biți: 1 ofertă, 2 comandă, 4 proformă, 8 factură fără sursă, 16 factură cu livrare
    PaymentMethodId       int               NULL CONSTRAINT FK_DiscountRule_PaymentMethod REFERENCES core.PaymentMethod(Id),
    MinDocumentAmount     decimal(19,4)     NULL,
    BuyQty                decimal(19,6)     NULL,
    FreeItemVariantId     int               NULL CONSTRAINT FK_DiscountRule_FreeVariant REFERENCES core.ItemVariant(Id),
    FreeQty               decimal(19,6)     NULL,
    MaxFreeQty            decimal(19,6)     NULL,
    FreeGoodsVatTreatment tinyint           NULL,       -- 1 reducere de preț, 2 livrare fără plată
    ValidFrom             date              NOT NULL,
    ValidTo               date              NULL,
    Status                tinyint           NOT NULL CONSTRAINT DF_DiscountRule_Status DEFAULT (1),
    Notes                 nvarchar(500)     NULL,
    CreatedAt             datetime2(3)      NOT NULL CONSTRAINT DF_DiscountRule_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy             int               NOT NULL,
    ModifiedAt            datetime2(3)      NULL,
    ModifiedBy            int               NULL,
    RowVersion            rowversion        NOT NULL,
    CONSTRAINT CK_DiscountRule_Kind CHECK (Kind IN (1, 2) AND Level IN (1, 2) AND Calc BETWEEN 1 AND 4
        AND Status BETWEEN 1 AND 4 AND DocumentTypes > 0),
    CONSTRAINT CK_DiscountRule_DocAmount CHECK (Calc <> 3 OR Level = 2),
    CONSTRAINT CK_DiscountRule_Pct CHECK (Calc <> 1 OR Value IS NULL OR Value BETWEEN 0 AND 100),
    CONSTRAINT CK_DiscountRule_Editable CHECK (IsEditable = 0 OR (Kind = 2 AND MaxValue IS NOT NULL)),
    CONSTRAINT CK_DiscountRule_FreeGoods CHECK (Calc <> 4 OR (Level = 1 AND BuyQty > 0 AND FreeQty > 0
        AND FreeGoodsVatTreatment IN (1, 2))),
    CONSTRAINT CK_DiscountRule_Currency CHECK (Calc NOT IN (2, 3) OR CurrencyCode IS NOT NULL),
    CONSTRAINT CK_DiscountRule_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
CREATE UNIQUE INDEX UX_DiscountRule_Code ON sales.DiscountRule (CompanyId, CodeKey);
CREATE INDEX IX_DiscountRule_Active ON sales.DiscountRule (CompanyId, Status, ValidFrom, ValidTo) INCLUDE (Kind, Level, Priority);
```

`sales.usp_Price_Resolve` primește un TVP cu liniile (cheia liniei, varianta, cantitatea în UM de bază, data prețului) și lista documentului. Urcă lanțul de liste cu o expresie fixă pe cel mult 3 niveluri (fără CTE recursiv: `ParentPriceListId` al listei, al părintelui și al bunicului, citite o dată), iar pentru fiecare nivel alege linia cu `TargetKind` minim și `MinQty` maxim ≤ cantitate, valabilă la dată. Grupa de valori a variantei se găsește prin `core.ValueListGroupAncestor` (Nucleu §3.3.1), cu distanța, ca să câștige grupa cea mai apropiată. Procedura nu aplică rotunjiri: întoarce prețul brut și ajustarea cumulată, iar rotunjirea o face `IPrecisionService` în aplicație.

Proceduri: `sales.usp_PriceList_Save`, `sales.usp_PriceListLine_Save` (refuză liniile în vigoare, `52105`), `sales.usp_Price_Resolve` (TVP), `sales.usp_PriceChange_Save`, `sales.usp_PriceChange_Preview` (prețurile noi, costurile prin parametru TVP primit de la aplicație, efectul în listele copil), `sales.usp_PriceChange_Approve` (scrie liniile și închide liniile vechi, `SP-03`), `sales.usp_PriceChange_ApplyDue`, `sales.usp_DiscountRule_Save`, `sales.usp_DiscountRule_Candidates` (TVP), `sales.usp_DocumentLineDiscount_Write` (TVP, în tranzacția documentului), `sales.usp_SalesApproval_Plan`, `sales.usp_SalesApproval_Decide`, `sales.usp_Rebate_Accrue`. Costul nu se citește prin join în schema `inv` (`SQL-16`): aplicația îl cere prin `IStockValuationQuery` și îl trimite ca parametru. Seed: `database/seed/sales/55_PriceRoundingRule.sql`; motivele noi de tip 6 se adaugă în `54_ReasonCode.sql`.

| Cod SQL | Cod API | HTTP | Când apare |
| --- | --- | --- | --- |
| 52101 | `SALES_PRICE_LINE_DUPLICATE` | 409 | Două linii pe aceeași listă, țintă, prag și dată de început |
| 52102 | `SALES_PRICELIST_PARENT_INVALID` | 400 | Lanț de liste cu ciclu sau mai lung de 3 niveluri; lista e propriul părinte |
| 52103 | `SALES_PRICELIST_PARENT_MISMATCH` | 400 | Părintele are altă monedă sau altă bază de TVA ori e listă cu amănuntul pentru o listă de vânzare |
| 52104 | `SALES_PRICE_TARGET_INVALID` | 400 | Linia nu are exact o țintă; grupa de valori nu aparține unui slot al modelului |
| 52105 | `SALES_PRICE_LINE_IN_EFFECT` | 409 | Modificarea sau ștergerea unei linii deja în vigoare; scrierea directă pe o listă cu `RequiresApproval` = 1 |
| 52106 | `SALES_PRICE_MISSING` | 409 | Confirmare sau validare cu o linie fără preț, fără dreptul de a scrie prețul |
| 52107 | `SALES_RETAIL_LIST_INVALID` | 400 | Lista cu amănuntul nu e în RON, nu are TVA inclus, are praguri sau e legată de o gestiune care nu e magazin |
| 52108 | `SALES_PRICE_CHANGE_INVALID` | 409 | Modificare cu dată în trecut, trimisă fără rânduri, aprobată în altă stare decât „De aprobat” |
| 52109 | `SALES_DISCOUNT_RULE_INVALID` | 400 | Praguri necrescătoare sau duplicate, procent în afara 0–100, bonus fără cantități sau pe nivelul document |
| 52110 | `SALES_DISCOUNT_NOT_ALLOWED` | 403 | Regulă opțională adăugată de un rol care nu e în lista ei, pe un document de alt tip, în afara perioadei sau peste `MaxValue` |
| 52111 | `SALES_DISCOUNT_MANDATORY` | 409 | Încercarea de a scoate un discount automat de pe document |
| 52112 | `SALES_DISCOUNT_OVER_LIMIT` | 409 | Discount manual sau total peste plafon la confirmare, fără aprobare valabilă |
| 52113 | `SALES_PRICE_BELOW_MARGIN` | 409 | Marja sub minim sau preț sub `MinPrice`, cu `BelowMarginAction` = 3 |
| 52114 | `SALES_PRICE_BELOW_COST` | 409 | Preț sub cost cu `BelowCostAction` = 3 sau fără motiv |
| 52115 | `SALES_PRICE_APPROVAL_PENDING` | 409 | Confirmarea, trimiterea sau validarea unui document cu excepții neaprobate |
| 52116 | `SALES_APPROVAL_SELF` | 403 | Autorul aprobă propriul document sau propria modificare de preț |
| 52117 | `SALES_REBATE_INVALID` | 400 | Acord cu praguri necrescătoare, perioadă suprapusă pentru același client și aceleași articole |
| 52118 | `SALES_PRICELIST_IN_USE` | 409 | Dezactivarea unei liste folosite de clienți, grupe, gestiuni, tranzacții, ca părinte sau ca listă implicită |
| 52119 | `SALES_PRICE_TEMP_OVERLAP` | 409 | Linie temporară suprapusă cu altă linie temporară pe aceeași țintă și prag, sau începută înaintea primei linii permanente a țintei |
| 52120 | `SALES_PARTNER_PRICELIST_INVALID` | 409 | A doua listă proprie activă a partenerului în aceeași monedă; lista proprie atribuită altui client, grupe, gestiuni sau folosită ca părinte; partenerul nu e client |

## 8. Permisiuni și roluri

| Permisiune | Ce permite |
| --- | --- |
| `Sales.PriceList.View` / `.Manage` | Listele de prețuri; `.Manage` scrie linii direct doar pe listele fără aprobare și pe liniile viitoare |
| `Sales.PriceChange.Prepare` / `.Approve` | Modificările de preț: pregătire, aprobare |
| `Sales.Discount.View` / `.Manage` | Regulile de discount și promoțiile |
| `Sales.Price.Override` | Scrierea prețului unitar pe linie (preț negociat, preț lipsă) |
| `Sales.Price.ChangeList` | Schimbarea listei de prețuri pe o ciornă |
| `Sales.Price.ViewCost` | Costul și marja pe linii și în rapoarte |
| `Sales.Price.Approve` | Aprobarea excepțiilor de preț, la nivelul dat de `SalesApprovalRule` |
| `Sales.Rebate.Manage` | Acordurile de rabat |

Roluri livrate (V1 §8, completate): **Șef vânzări** (toate permisiunile V2; nivelul 1 de aprobare), **Key account** (`PriceList.View`, `Discount.View`, `PriceChange.Prepare`, `Rebate.Manage` pe portofoliu, `Price.ViewCost`; plafon 7 %), **Agent de teren** și **Telesales** (`PriceList.View`, `Discount.View`, `Price.Override` doar în plafon; plafon 5 %; fără `ViewCost`), **Operator facturare** (`PriceList.View`, `Price.Override` pe facturile fără sursă), **Gestionar magazin** (`PriceList.View` pe lista magazinului, `PriceChange.Prepare` pe ea), **Contabil clienți** (`View` pe tot V2, `ViewCost`), **Director comercial** (rol nou, opțional: nivelul 2 de aprobare).

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru V2 |
| --- | --- |
| Reducerile comerciale din factură nu intră în baza de TVA (Codul fiscal art. 286 alin. (4) lit. a) | Discounturile (pe linie, pe document și bonusul tratat ca reducere) scad baza de TVA a liniei sau, la discountul pe document, a fiecărei cote, proporțional |
| e-Factura (UBL 2.1, CIUS-RO) | Prezentarea 2 folosește prețul brut și reducerea de preț pe linie (BT-148, BT-147, BT-146); prezentarea 3, reducerile la nivel de document (BG-20) pe categorii de TVA, cu motivul; trimiterea în V5 și Integrări (02.14) |
| Afișarea prețurilor la raft în magazine (Legea 363/2007, OG 21/1992) | Lista cu amănuntul ține prețul cu TVA; eticheta de raft se tipărește din listă (S7, Tipărire); prețul unitar pe kilogram sau litru se calculează din UM-ul de bază al articolului |
| Promoțiile în magazine (Legea 650/2002, prețul anterior al reducerii) | Pentru o reducere pe lista cu amănuntul, istoricul liniilor dă cel mai mic preț din ultimele 30 de zile, afișat în raportul de etichete (prețul de referință) |
| Legea 72/2013 (termene de plată) | Discountul de plată pe loc (`CASH-2`) e reducere comercială acordată pe factură, nu penalitate; nu schimbă termenul de plată |
| Precizia prețurilor (P7) | Prețurile pe 6 zecimale; valorile și TVA-ul pe 2 zecimale, rotunjite doar prin `IPrecisionService` |

Integrări externe: niciuna în V2. Interne: Nucleu (articole, variante, grupe de valori, UM, precizie, cursuri, joburi, notificări), Stocuri (S1 lista magazinului, S3 costul și MP), V1 (condițiile clientului, plafoanele agenților, setările), V3–V6 (folosesc `IPriceEngine` și scriu `DocumentLineDiscount`), POS și e-commerce (prin evenimente și `IPriceListQueries`).

**Precizări aplicate altor documente la validarea V2 (27.11.2026):**

- **Nucleu §3.3.1, regula 9**: căutarea se face în lista documentului, apoi în lanțul de liste părinte (cel mult 3 niveluri, cu ajustare procentuală), cu pragurile de cantitate în UM de bază; procedura e `sales.usp_Price_Resolve` (TVP), apelată de `IPriceEngine`.
- **V1 §3.8**: `sales.ReasonCode` tipul 6 devine „excepție de preț” (preț sub minim, sub cost, modificat manual, gratuitate), cu 3 motive noi în seed (`NEGOCIAT`, `ALINIERE`, `CLIENTNOU`); nomenclatorul nou `sales.PriceRoundingRule`.
- **V1 §3.9**: coloanele noi din 3.10 în `SalesSettings`; `ApprovalThresholds` se înlocuiește cu tabelul `SalesApprovalRule`.
- **Stocuri S1**: `RetailPriceListId` trimite la o listă cu `Kind` = 2 (`52107`); **S3 §3.7**: documentul MP se creează în ciornă de Vânzări la aprobarea unei modificări pe lista cu amănuntul, câte unul pe gestiune.

## 10. Rapoarte

| Raport | Conținut | Utilizatori |
| --- | --- | --- |
| Liste de prețuri la dată | Prețul fiecărei variante în fiecare listă, la o dată aleasă, cu sursa (proprie, din părinte), cu export Excel | Șef vânzări, Key account, agenți |
| Istoric de preț | Pe variantă și listă: toate liniile, cu datele, modificarea din care vin și cine a aprobat; cel mai mic preț din ultimele 30 de zile | Șef vânzări, Contabil |
| Liste incomplete | Variantele active vândute în ultimele 90 de zile fără preț în lista unui client sau a unei grupe (deci prin părinte sau fără preț); liniile pe variante inactive | Șef vânzări |
| Discounturi acordate | Pe regulă, client, agent și articol: valoarea discounturilor, procentul mediu, partea promoțiilor, a bonusului și a discountului manual | Șef vânzări, Director comercial |
| Excepții de preț | Documentele cu excepții, cine a cerut, cine a aprobat, marja aprobată față de cea realizată | Șef vânzări, Contabil |
| Rabaturi pe rulaj | Acordurile active, rulajul, pragul atins, rabatul estimat, cât lipsește până la pragul următor | Key account, Șef vânzări, Contabil |

## 11. Ecrane principale

Tiparul comun: listă cu filtre → fișă cu tab-uri → istoric (`UI-08`). Machetele (HTML + PNG) sunt în [ecrane/v2-preturi-discounturi/](ecrane/v2-preturi-discounturi/). Meniul Vânzări › Prețuri are Liste de prețuri, Modificări de preț, Discounturi, Promoții, Rabaturi pe rulaj și „De unde vine prețul”.

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Liste de prețuri | Listele firmei cu tipul, moneda, părintele și numărul de clienți; fișa listei `HORECA` cu antetul (părinte `BAZA` + 3 %, rotunjire `R005`) și liniile: proprii și calculate din părinte, pe variantă și pe model | `v2-01-liste-preturi` |
| Modificare de preț | `MPV 0014` „Scumpire apă +4 %”: metoda, filtrul, data, previzualizarea pe rânduri cu marja veche și nouă, efectul în `HORECA`, clienții afectați, trimiterea la aprobare | `v2-02-modificare-pret` |
| Discounturi | Lista regulilor active, viitoare și expirate; fișa regulii `PR-CEAI-NOV` cu tipul, nivelul, condițiile pe client și pe articol, valabilitatea, prioritatea, prezentarea, documentele | `v2-03-regula-discount` |
| Promoție cu bonus în marfă | Fișa regulii `BON-APA15` (5+1 baxuri): cantitățile, articolul gratuit, plafonul, tratamentul fiscal, simularea pe o cantitate | `v2-04-bonus-marfa` |
| Comandă › Linii și prețuri | Formularul comenzii `CV 4441` Hotel Central: liniile cu preț de listă, discounturi, preț unitar, marjă; linia de bonus; totalurile; panoul de excepții și butonul „Cere aprobare” | `v2-05-comanda-preturi` |
| De unde vine prețul | Explicația pas cu pas pentru APA05 la Hotel Central, 480 buc, 27.11.2026: lista, lanțul, linia găsită, ajustarea, rotunjirea, discounturile luate și cele respinse, verificările | `v2-06-de-unde-vine-pretul` |
| Rabaturi pe rulaj | Acordul `RB-MR-Q4` cu Mega Retail: pragurile, rulajul pe luni, rabatul estimat, cât lipsește până la 3 % | `v2-07-rabat-rulaj` |
| Aprobare excepții de preț | Cererea lui Vlad Georgescu pe `CV 4441`, văzută de Radu Stan: excepția, plafonul, marja, istoricul clientului, Aprobă / Respinge | `v2-08-aprobare-pret` |
| Fișa partenerului › Vânzări › Prețuri | Lista proprie `HCJ-2026` a lui Cafe Unirii SRL cu prețurile negociate și istoricul APA05: liniile permanente și cea temporară 01–03.03.2026, cu prețul pe zile și revenirea la 1,78 | `v2-10-lista-partener-istoric` |
| Configurare › Vânzări › Prețuri | Setările din 3.10 și regulile de aprobare pe niveluri | `v2-09-configurare-preturi` |

## 12. Criterii de acceptare și teste cheie

Fiecare criteriu are cel puțin un test automat marcat `[Trait("Criteriu", "02.03-V2-Vn")]` (`TST-01`).

| # | Criteriu | Test |
| --- | --- | --- |
| V2-A1 | Pentru APA05 la Hotel Central (lista `HORECA` fără linie proprie) prețul de listă e 1,85: `BAZA` 1,80 × 1,03 = 1,854, rotunjit cu `R005`; sursa arată adâncimea 1 și ajustarea 3 % | Test unitar (motor) + integrare (`usp_Price_Resolve`) |
| V2-A2 | CEAI05 primește prețul de pe model (3,22) când nu are linie pe variantă; o linie nouă pe variantă câștigă din ziua ei; o linie pe grupă de valori câștigă în fața modelului | Test de integrare |
| V2-A3 | Pragul APA2N: 599 buc → 4,10, 600 buc → 3,95; pragul se compară în UM de bază (50 baxuri de 12 = 600 buc → 3,95) | Test unitar |
| V2-A4 | O linie în vigoare nu se modifică (`52105`); o linie nouă cu `ValidFrom` 01.12 închide linia veche la 30.11 | Test de integrare |
| V2-A5 | `MPV 0014` aprobată de Radu Stan scrie 5 linii cu `ValidFrom` 01.12.2026; Cristina Marin nu o poate aproba (`52116`); comenzile confirmate înainte își păstrează prețul | Test de integrare |
| V2-A6 | Pe `CV 4441` valorile din 4.1 ies exact: 816,00 + 835,20 + 1.134,00 + 0,00 = 2.785,20 lei, TVA 389,89 lei, total 3.175,09 lei; linia de bonus are 72 buc | Test unitar (motor) |
| V2-A7 | Cu `PromotionsExclusive` = 1, o regulă automată nepromoțională nu se aplică pe CEAI05 cât timp `PR-CEAI-NOV` e activă; cu 0, se aplică în cascadă după prioritate | Test unitar |
| V2-A8 | Cascadă versus însumare: 10 % și 5 % dau 14,5 % în cascadă și 15 % însumate | Test unitar |
| V2-A9 | Discountul pe document de 1,5 % pe 6.000,00 lei (două cote de TVA) se repartizează pe linii la 2 zecimale, suma repartizată e exact 90,00, iar TVA-ul pe cote e calculat după repartizare | Test unitar |
| V2-A10 | Discountul manual 8 % al lui Vlad (plafon 5 %) blochează confirmarea (`52115`) până la aprobarea nivelului 1; o creștere ulterioară la 10 % cere din nou aprobare; o scădere la 6 % nu | Test de integrare |
| V2-A11 | Prețul sub cost cu `BelowCostAction` = 2 cere motiv de tip 6 și aprobare; cu 3 e refuzat (`52114`); costul vine din `IStockValuationQuery`, nu din join | Test de integrare (cu Stocuri) |
| V2-A12 | Avizul și factura generate din `CV 4441` au aceleași prețuri și discounturi, proporțional cu cantitatea, chiar dacă lista sau promoția s-a schimbat între timp | Test de integrare |
| V2-A13 | Prezentarea 2 produce în UBL prețul brut 1,85, reducerea 0,15 și prețul net 1,70 pe linia APA05; prezentarea 3 produce câte o reducere de document pe fiecare cotă de TVA | Test unitar (generatorul UBL, V5) |
| V2-A14 | Aprobarea unei modificări pe `RETAIL-CJ` creează un document MP în ciornă pentru `MAG-CJ` și publică `PreturiVanzareModificate` | Test de integrare (cu Stocuri) |
| V2-A15 | Rabatul `RB-MR-Q4` la un rulaj de 186.420,00 lei e 3.728,40 lei (2 %, retroactiv); la 250.000,00 lei devine 7.500,00 lei | Test unitar |
| V2-A16 | Motorul de preț pentru o comandă de 200 de linii face cel mult 3 apeluri la baza de date (prețuri, reguli, cost) și răspunde sub 300 ms pe baza de test | Test de performanță |
| V2-A17 | Seed-ul regulilor de rotunjire rulat de două ori nu dublează rânduri (`MIG-06`); `E49` dă 2,49 pentru 2,31 și 2,99 pentru 2,51 | Test de integrare + unitar |
| V2-A19 | Pe `HCJ-2026`, APA05: 28.02 → 1,78; 01.03, 02.03, 03.03 → 1,59; 04.03 → 1,78; 01.06 → 1,85. Adăugarea liniei temporare nu schimbă `ValidTo` al liniei din 28.02; o a doua linie temporară 02.03–05.03 e refuzată (`52119`) | Test de integrare (`usp_Price_Resolve`) |
| V2-A20 | Un client cu listă proprie în RON și cu lista `HORECA` în condiții primește pe un document în RON lista proprie, iar pe unul în EUR lista din condiții; o a doua listă proprie activă în RON e refuzată (`52120`) | Test de integrare |
| V2-A18 | Toate procedurile V2 rulează corect pe baza case sensitive; codul `dr-mr26` e găsit ca `DR-MR26` în căutare, dar nu se poate crea ca regulă nouă | Test de integrare |

Criteriile V2-A1, V2-A6, V2-A10 și V2-A12 sunt critice: ele decid prețul facturat clientului, controlul discounturilor și faptul că livrarea și factura nu schimbă prețul convenit.

## Decizii de validare

| # | Întrebare | Propunerea | Unde e aplicată | Stare |
| --- | --- | --- | --- | --- |
| 1 | Cum se leagă listele între ele | Listă părinte cu ajustare procentuală (cel mult 3 niveluri: client → grupă → bază); lista copil ține doar excepțiile, restul se calculează din părinte și se rotunjește | 2 regula 2, 3.1, 3.4 | ✓ DA (27.11.2026) |
| 2 | Pe ce se pune prețul | Pe variantă, pe grupă de valori a modelului sau pe model (regula 9 din Nucleu), pe UM de bază, cu praguri de cantitate pe linie | 2 regulile 3 și 5, 3.1 | ✓ DA (27.11.2026) |
| 3 | Schimbarea prețurilor și istoricul | Lista păstrează tot istoricul; un preț în vigoare nu se editează și nu se șterge; schimbarea e o linie nouă cu dată. Liniile pot fi permanente sau temporare (de exemplu 01.03–03.03.2026); după o linie temporară prețul revine singur la cel permanent anterior (cel din 28.02), fără reintroducere. Pe lista de bază și pe listele cu amănuntul, doar prin modificare aprobată (pregătește unul, aprobă altul), cu previzualizarea marjei | 2 regula 4, 3.3, 4.2 | ✓ DA (27.11.2026) |
| 4 | Prețul pe documentele generate | Se îngheață la confirmare și se copiază din sursă (comandă → aviz → factură), fără recalculare; doar ciornele se recalculează | 2 regula 8, 4.4 | ✓ DA (27.11.2026) |
| 5 | Combinarea discounturilor | Ordinea fixă din 3.7; discounturile de linie în cascadă (setabil: însumare); promoțiile exclusive față de celelalte automate (setabil); grupuri de exclusivitate „cea mai bună pentru client” | 3.6, 3.7, 3.10 | ✓ DA (27.11.2026) |
| 6 | Prezentarea discountului | Implicit „afișat pe linie” (preț de listă, discount, preț unitar; în e-Factura preț brut și reducere de preț), setabil pe regulă: inclus în preț sau linie separată | 3.7 | ✓ DA (27.11.2026) |
| 7 | Bonusul în marfă | Regulă de tip bonus („5+1”), linie de bonus legată de linia sursă, cu preț de listă și discount 100 %; tratament fiscal implicit „reducere de preț în cadrul vânzării”, configurabil pe regulă, de confirmat cu contabilul | 3.6.1 | ✓ DA (27.11.2026) |
| 8 | Plafoane și aprobare | Plafonul agentului pe discountul manual; plafon total pe firmă 30 %; marjă minimă 8 % cu aprobare; preț sub cost cu motiv și aprobare; aprobare pe document, la nivelul cel mai mare necesar; aprobatorul nu modifică prețurile | 3.8, 3.9, 4.1 | ✓ DA (27.11.2026) |
| 9 | Rabatul pe rulaj | Acord cu praguri pe lună, trimestru sau an, retroactiv implicit; estimare zilnică fără contare; nota de reducere `FS-RED` la final de perioadă (V6) | 3.11 | ✓ DA (27.11.2026) |
| 10 | Prețul cu amănuntul | Listă separată, cu TVA, în RON, legată de gestiunile magazin; modificarea ei aprobată creează documentul MP în ciornă (S3) și trimite prețurile la POS | 3.1, 3.3, 4.3 | ✓ DA (27.11.2026) |
| 11 | Nomenclatoarele V2 | Regulile de rotunjire cu seed (7, inclusiv terminațiile ,99 / ,90 / ,49); motivele de excepție de preț în nomenclatorul de motive din V1, cu 3 motive noi | 3.2, 9 | ✓ DA (27.11.2026) |
| 12 | Listele de prețuri pe partener | Fiecare client poate avea listă proprie (câte una activă pe monedă), creată din fișa partenerului, cu părinte lista grupei; are prioritate față de lista din condiții; aceleași reguli de istoric și prețuri temporare | 2 regula 1, 3.1.1, 4.4 | ✓ DA (27.11.2026) |
