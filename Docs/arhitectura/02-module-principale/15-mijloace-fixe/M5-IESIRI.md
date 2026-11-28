# ERP · 02.15 Mijloace fixe și obiecte de inventar · M5 Ieșiri: vânzare, casare, lipsă, donație, aport, transfer în grup, trecere în obiecte de inventar

27 noiembrie 2026 · Aurelian · **✓ Aprobat autonom (27.11.2026)** (lucru autonom cerut de Aurelian; deciziile 1–10 adoptate pe recomandare). A cincea din cele 7 părți ale modulului 02.15.

Schema `fa`, prefix `Erp.FixedAssets`, erorile M5 în plaja `65600–65749`. Această parte acoperă **scoaterea bunurilor din evidență**: vânzarea (pornită din Vânzări cu factura `FV-MF`), casarea cu valorificarea materialelor recuperate, lipsa la inventar (imputată sau nu), donația și sponsorizarea, aportul la capitalul altei societăți, transferul către altă firmă a grupului și trecerea în obiecte de inventar. Documente părinte: [M1](M1-CONFIGURARE.md) §3.2 (tranzacțiile `IMF-*`), §3.3 (grupul `DISPOSE`), §3.9 (modelele de contare); [M2](M2-REGISTRU-INTRARI.md) (fișa, componentele, jurnalul valorilor); [M3](M3-AMORTIZARE.md) (amortizarea lunii ieșirii); [Vânzări V1 și V5](../03-vanzari/V5-FACTURARE.md) (factura); [Stocuri S2](../05-stocuri/README.md) (intrarea materialelor recuperate). Reguli de cod: [Bune practici](../../bune-practici/BUNE_PRACTICI.md).

**Ce trebuie știut înainte de citire (context autonom):**

- Produs ERP on-premise, SQL Server case sensitive (`SQL-21`), .NET 10 + MediatR, Dapper și proceduri stocate, **fără ORM**, fără componente plătite. O bază de date pentru un grup de firme: transferul în grup se face în aceeași bază, între firme diferite.
- Ieșirea e un document `IMF` (`fa.AssetDisposal`) cu tranzacție (Nucleu §3.6). Validarea scrie rândul `DISPOSAL` în jurnalul valorilor pe fiecare regim (M2), trece fișa în starea 8 „Ieșită” și publică `MijlocFixIesit` v1 cu blocul `Accounting` (C2).
- **Amortizarea lunii ieșirii se calculează integral** (convenția livrată, M1 §3.6 `StopConvention = 1`): dacă `AML` pentru luna ieșirii nu e încă validat, ieșirea calculează și scrie ea cota lunii pentru fișă (linie `DEPRECIATION` cu sursa ieșirea), iar documentul lunar o exclude. Așa valoarea netă la ieșire e corectă oricând se validează documentele.
- **Vânzarea pornește din Vânzări**, nu de aici: factura `FV-MF` are pe linie fișa (`AssetId`), venitul pe cheia `AssetSaleIncome` (`7583`), iar la validarea facturii Vânzările apelează `IAssetDisposalPort` în aceeași tranzacție SQL, care generează ieșirea `IMF-VZ`. Factura nu există fără ieșire (concordanța `ASSET_SALES` din C3 e mereu 0).
- **Ce NU face M5:** nu facturează (V5), nu face intrarea în stoc a materialelor recuperate (S2, la cererea M5), nu face inventarierea (M7, care generează lipsa), nu ține obiectele de inventar în folosință (M6).

## 1. Scop și perimetru

| Zonă | Ce face M5 | Ce NU face (și cine face) |
| --- | --- | --- |
| Vânzare | Verifică fișa la emiterea facturii, generează ieșirea la validare, calculează câștigul sau pierderea | Factura, TVA-ul, încasarea (V5, F2) |
| Casare | Procesul-verbal de casare cu comisia, valoarea rămasă pe cheltuială, materialele recuperate trimise la stoc | Intrarea și vânzarea deșeurilor (S2, V5) |
| Lipsă | Ieșirea din inventariere, cu imputarea către persoana răspunzătoare (debit `4282`) sau pe cheltuială | Lista de inventar și diferențele (M7); reținerea pe salariu (02.11) |
| Donație, aport | Ieșirea cu valoarea, beneficiarul și contractul; aportul cu titlurile primite | TVA-ul de colectat (A5/C4 pe baza evenimentului), limita sponsorizării (C4, D101) |
| Transfer în grup | Factura `FV-MF` către firma din grup și ciorna de intrare `MFI-GRP` în firma primitoare, cu datele tehnice și atașamentele | Validarea intrării la primitor (M2) |
| Trecere în OB | Ieșirea din mijloace fixe și deschiderea evidenței în folosință (M6) | Evidența ulterioară (M6) |
| Componente | Ieșirea unei componente (înlocuire), cu părintele rămas | — |

## 2. Model de domeniu

```
 fa.AssetDisposal (IMF) ─── fa.AssetDisposalLine × N     (fișă: valorile pe regim la ieșire, venitul, cheltuiala, rezerva)
                        ├── fa.AssetDisposalRecovery × N (materiale recuperate la casare: articol, cantitate, valoare → S2)
                        └── fa.AssetDisposalCharge × 0..N (imputare la lipsă: persoana, suma, TVA)
 la validare → fa.AssetLedger (DEPRECIATION a lunii, DISPOSAL) · fa.Asset.Status = 8 · MijlocFixIesit (Accounting)
```

Reguli de business:

1. **Doar fișele în stările 1–4 pot ieși** (`65601`): cele în conservare ies cu valoarea rămasă (conservarea se închide automat la ieșire); cele „în curs de PIF” ies fără amortizare. O fișă ieșită nu mai poate fi modificată, doar consultată, iar numărul de inventar nu se refolosește.
2. **Părintele iese cu componentele** (`65110`, M2): ieșirea părintelui adaugă automat liniile componentelor active, cu același motiv. O componentă poate ieși singură (înlocuire).
3. **Ieșirea folosește valorile la data ieșirii, pe fiecare regim**: valoarea brută, amortizarea cumulată (inclusiv luna ieșirii), deprecierea, rezerva din reevaluare. Rezerva rămasă se transferă integral în `1175` la ieșire.
4. **Ieșirea parțială nu există.** O parte dintr-un bun iese după descompunere (M4), apoi ieșirea fișei rezultate.
5. **Data ieșirii nu poate fi într-o lună închisă** (C3) și nici înaintea ultimei operații validate pe fișă (`65602`).
6. **Stornarea** ieșirii e posibilă doar cât luna e deschisă și nu există amortizare validată după ea (`65690`); la vânzare, stornarea se face doar prin stornarea facturii în Vânzări (V6), care stornează ieșirea prin același port.

## 3. Entități și documente

### 3.1 Documentul de ieșire (`fa.AssetDisposal`)

**Antet**: tranzacția (`IMF-VZ`, `IMF-CAS`, `IMF-LIP`, `IMF-DON`, `IMF-APT`, `IMF-GRP`, `IMF-OB`), numărul (`IMF 000031`), data, motivul, comisia (pentru casare și trecerea în OB: membri cu rolul „Membru comisie”, aprobare de la Șeful administrativ), partenerul (cumpărător, beneficiarul donației, societatea la care se face aportul, firma din grup), documentul sursă (factura, lista de inventar, contractul), atașamentele.

**Linia**: fișa, valorile la ieșire pe regim (citite din jurnal, nu editate), suma venitului (din factură sau valoarea de aport) și rezultatul (câștig sau pierdere, calculat), cota de amortizare a lunii ieșirii scrisă de document, rezerva transferată.

### 3.2 Vânzarea (`IMF-VZ`, din factura `FV-MF`)

1. Operatorul de facturare (sau contabilul) emite în Vânzări o factură cu tranzacția `FV-MF`. Pe linie alege fișa (căutare după număr de inventar, doar fișele firmei în stările 1–4 și neincluse în altă factură în ciornă, grupul `DISPOSE`). Articolul liniei e articolul tehnic `MF-VANZARE` (nestocabil), cu denumirea fișei; prețul e introdus.
2. Panoul fișei din factură (prin `IAssetDisposalPort.PreviewAsync`) arată valoarea netă contabilă estimată la data facturii și rezultatul estimat; TVA-ul se calculează normal de V5 (inclusiv taxarea inversă pentru bunurile din art. 331, de exemplu deșeurile și unele echipamente, după codul din nomenclator).
3. La validarea facturii, Vânzările apelează `IAssetDisposalPort.DisposeAsync` în aceeași tranzacție SQL; M5 creează și validează `IMF-VZ` cu legătura pe linie (`DISPOSE`). Dacă fișa nu mai poate ieși (de exemplu a fost casată între timp), validarea facturii eșuează cu eroarea M5 și nu se consumă număr fiscal.
4. Nota: factura contează `4111` = `7583` + `4427` (cheia `AssetSaleIncome`); ieșirea contează `28xx` = `21x` (amortizarea), `6583` = `21x` (valoarea netă), `29xx` = `21x` (deprecierea, dacă există), `105` = `1175` (rezerva rămasă).

### 3.3 Casarea (`IMF-CAS`)

Pe baza propunerii de casare (din inventariere, M7, sau manuală) și a deciziei administratorului. Procesul-verbal de scoatere din funcțiune (formularul 14-2-3/aA, OMFP 2634) se tipărește din document, cu comisia, starea bunului și materialele recuperate. Valoarea rămasă merge pe `6583`.

**Materialele recuperate** (`fa.AssetDisposalRecovery`): articol (de exemplu `FIER-VECHI`), cantitate, valoarea estimată (prețul de valorificare). La validare, M5 cere Stocurilor o intrare cu tranzacția S2 nouă `NIR-REC` „Intrare din recuperări”, prin `IRecoveredMaterialIntakePort` (portul S2), în gestiunea aleasă: `3028` (sau contul articolului) = `7588`. Materialele se vând apoi prin facturare normală (V5).

### 3.4 Lipsa la inventar (`IMF-LIP`)

Generată din lista de inventar validată (M7, grupul `COUNT_DIFF`, liniile cu minus). Contabilul completează pentru fiecare linie:

- **Imputată**: persoana (angajat sau terț), valoarea imputată (implicit valoarea de înlocuire din lista de inventar, cel puțin valoarea netă), TVA-ul (bunul lipsă imputat e livrare taxabilă, art. 270), scadența. Nota: ieșirea ca la casare (`6583` = `21x`), plus `4282` = `7581` + `4427` (cheile noi `ChargeReceivableEmployee`, `ChargeIncome`). Reținerea pe statul de plată o face 02.11 (viitor) din evenimentul `ImputareInregistrata`.
- **Neimputabilă**: ieșirea pe cheltuială, cu marcarea fiscală „nedeductibilă” (lipsă neimputabilă neacoperită de asigurare) pentru D101 și semnalul de ajustare a TVA-ului dedus pentru bunurile de capital (eveniment către C4, care propune ajustarea în decontul de TVA).

### 3.5 Donația și aportul (`IMF-DON`, `IMF-APT`)

- **Donație sau sponsorizare**: beneficiarul, contractul, valoarea; ieșirea pe `6582` (cheia nouă `DonationExpense`) în loc de `6583`. Evenimentul marchează suma pentru calculul limitei de sponsorizare (C4, D101) și semnalul de TVA (livrare fără plată: colectare, cu excepțiile din Codul fiscal) către C4.
- **Aport la capitalul altei societăți**: societatea, valoarea de aport din raportul de evaluare, titlurile primite (`261` / `263` / `265`, cheia nouă `ContributedInvestment`). Ieșirea: `28xx` = `21x`, titlurile la valoarea de aport, diferența față de valoarea netă pe venit sau cheltuială (cheile `AssetContributionGain` / `AssetContributionLoss`, cu modelul livrat după OMFP 1802).

### 3.6 Transferul în grup (`IMF-GRP`)

Firmele din aceeași bază sunt entități juridice diferite, deci transferul e o vânzare: `IMF-GRP` se creează din factura `FV-MF` emisă către partenerul care e o firmă a grupului (`core.Partner.GroupCompanyId`). La validare, M5 creează în firma primitoare o **ciornă de intrare `MFI-GRP`** (M2) cu: denumirea, grupa și clasa propuse (după codul grupei, dacă există și acolo), valoarea de intrare = prețul facturii, seria, anul de fabricație, garanția, datele tehnice, atașamentele copiate (prin referință la același fișier), legătura spre fișa vânzătorului. Primitorul o verifică și o validează (cu PIF). Dacă primitorul folosește aceeași politică, durata propusă e durata rămasă la vânzător (opțiune pe document).

### 3.7 Trecerea în obiecte de inventar (`IMF-OB`)

Pentru bunurile care nu mai îndeplinesc condițiile de mijloc fix (de exemplu fișe vechi cu valoare de intrare sub pragul curent, amortizate complet, pe care firma decide să le țină ca obiecte de inventar). Doar fișele amortizate complet pe ambele regimuri sau cu decizie motivată (`65640`). Ieșirea din mijloace fixe (`28xx` = `21x`) și deschiderea evidenței în folosință în M6 (`ObiectInventarDatInFolosinta`, extrabilanțier `8039`, cu persoana și locul de pe fișă).

## 4. Fluxuri principale

### 4.1 Vânzarea autoturismului Dacia Logan

1. Pe 10.12.2026, Elena Dima emite `FV EXD 10302` (`FV-MF`) către Auto Second SRL: linia „Autoturism Dacia Logan CJ-08-EXD” legată de MF 000203, 18.000,00 lei + TVA 21 % 3.780,00 = 21.780,00 lei.
2. Panoul arată: brut 52.000,00, amortizat la 30.11 47.666,67, cota din decembrie 866,67, valoare netă la ieșire 3.466,66, câștig estimat 14.533,34.
3. La validare se creează `IMF 000031` (`IMF-VZ`). Note: `4111` = `7583` 18.000,00 + `4427` 3.780,00 (factura); `2813` = `2133` 48.533,34; `6583` = `2133` 3.466,66 (ieșirea).
4. Fiscal: valoarea fiscală rămasă e 0 (amortizat complet pe `TAX`); D101 primește venitul și cheltuiala cu valoarea fiscală rămasă (0, deductibilă).
5. Documentul lunar `AML 2026-12` nu mai include MF 000203 (cota lunii a scris-o ieșirea).

### 4.2 Casarea stivuitorului Balkancar

MF 000118 (stivuitor diesel Balkancar, 2009), 64.300,00 lei, amortizat complet. Comisia (Adrian Barbu, Gheorghe Ilie, Mihai Stan) propune casarea pe 15.12.2026, aprobată de administrator. `IMF 000032` (`IMF-CAS`):

- valoarea rămasă 0,00 (fără `6583`); nota `2813` = `2131` 64.300,00;
- recuperări: 1.700 kg fier vechi × 0,50 lei = 850,00 lei, intrare în gestiunea `DEP-CJ` prin `NIR-REC 000004`: `3028` = `7588` 850,00;
- procesul-verbal de scoatere din funcțiune tipărit și semnat, atașat.

### 4.3 Lipsa videoproiectorului

La inventarul din 31.12.2026 (M7, `INV 000012`), MF 000402 Videoproiector Epson EB-L630U (brut 7.200,00, amortizat 5.400,00, net 1.800,00), în responsabilitatea lui Mihnea Pavel, lipsește. `IMF 000034` (`IMF-LIP`) generată din inventar: imputat lui Mihnea Pavel la valoarea de înlocuire 2.100,00 + TVA 441,00. Note: `2814` = `214` 5.400,00; `6583` = `214` 1.800,00; `4282` = `7581` 2.100,00 + `4427` 441,00. Evenimentul `ImputareInregistrata` ajunge la lista de lichidare a angajatului (M6 §4.4).

### 4.4 Laptop transferat la Exemplu Producție

MF 000433 (laptop, 5.890,00, amortizat 163,61 după decembrie) se vinde la Exemplu Producție SRL (firmă a grupului) pe 05.01.2027 cu `FV EXD 10351` (`FV-MF`), la valoarea netă 5.726,39 + TVA. Se creează `IMF 000035` (`IMF-GRP`) fără rezultat, iar în Exemplu Producție apare ciorna `MFI-GRP` cu laptopul, seria 7XK2Q94, garanția până la 10.11.2029 și durata rămasă propusă 35 de luni.

### 4.5 Cazuri limită

| Caz | Comportament |
| --- | --- |
| Factura `FV-MF` cu o fișă deja ieșită | Validarea facturii eșuează (`65601`), fără număr fiscal consumat |
| Două facturi în ciornă pe aceeași fișă | A doua nu poate alege fișa (grupul `DISPOSE` o arată ca rezervată); `65603` la validare |
| Vânzarea unei fișe cu rezervă din reevaluare | Rezerva rămasă se transferă în `1175` în aceeași notă |
| Ieșirea părintelui cu două componente active | Liniile componentelor se adaugă automat; documentul nu se poate valida fără ele (`65110`) |
| Casare fără materiale recuperate | Permisă; secțiunea de recuperări goală |
| Lipsă găsită ulterior (bunul reapare) | Intrare `MFI-PLS` (M2) la valoarea justă, nu stornarea lipsei dintr-o lună închisă |
| Stornarea facturii `FV-MF` în luna următoare, cu luna ieșirii închisă | Stornarea facturii în V6 reface fișa printr-o intrare de corecție în luna deschisă (`MFI-ADJ` cu valorile de la ieșire), cu avertisment |
| Ieșire din conservare | Conservarea se închide automat la data ieșirii |

## 5. Evenimente

| Eveniment | Direcție | Conținut | Consumatori |
| --- | --- | --- | --- |
| `MijlocFixIesit` v1 | Publicat | Tranzacția, fișele, valorile pe regim, rezultatul, `Accounting` (`DisposalGross`, `DisposalDepreciation`, `DisposalNetValue`, `DisposalImpairment`, `RevaluationSurplus`), marcajele fiscale (nedeductibil, ajustare TVA) | C2, C4 (D101, TVA), M3, M7, 02.13 |
| `ImputareInregistrata` v1 | Publicat | Persoana, suma, TVA-ul, documentul | F (creanța `4282`), 02.11 HR (reținerea, viitor), M6 (lichidarea) |
| `MaterialeRecuperateDeIntrat` v1 | Publicat (prin port, sincron) | Articole, cantități, valori, gestiune | S2 (`NIR-REC`) |
| `FacturaVanzareValidata` (linii `FV-MF`) | Consumat, sincron prin port | — | Generează `IMF-VZ` / `IMF-GRP` |
| `FacturaVanzareStornata` (linii `FV-MF`) | Consumat, sincron prin port | — | Stornează ieșirea |

## 6. Contracte publice (`Erp.FixedAssets.Contracts`, partea M5)

```csharp
public interface IAssetDisposalPort
{
    /// Apelat de Vânzări la editarea liniei FV-MF: valorile estimate la data facturii.
    Task<AssetDisposalPreviewDto> PreviewAsync(int companyId, long assetId, DateOnly invoiceDate, decimal salePrice, CancellationToken ct);
    /// Apelat de Vânzări în tranzacția SQL de validare a facturii; aruncă BusinessRuleException (65601, 65603...) dacă fișa nu poate ieși.
    Task<long> DisposeAsync(AssetSaleDisposalRequest request, IDbTransaction tx, CancellationToken ct);
    /// Apelat de Vânzări (V6) la stornarea facturii.
    Task ReverseAsync(long salesInvoiceId, IDbTransaction tx, CancellationToken ct);
}
public sealed record AssetDisposalPreviewDto(long AssetId, string InventoryNumber, decimal GrossBook, decimal AccumulatedBook,
    decimal ExitMonthDepreciation, decimal NetBook, decimal NetTax, decimal RevaluationReserve, decimal EstimatedGainLoss);
public sealed record AssetSaleDisposalRequest(int CompanyId, long SalesInvoiceId, DateOnly InvoiceDate, int PartnerId,
    IReadOnlyList<AssetSaleLine> Lines);
public sealed record AssetSaleLine(long SalesInvoiceLineId, long AssetId, decimal NetSaleAmount);
```

Portul S2 folosit de casare: `IRecoveredMaterialIntakePort.CreateAsync(RecoveredMaterialIntakeRequest, IDbTransaction, CancellationToken)` (definit în `Erp.Stocks.Contracts`, precizare la S2).

## 7. Schema SQL `fa` (partea M5)

| Tabel | Cheie primară | Chei unice / străine | Indecși |
| --- | --- | --- | --- |
| `fa.AssetDisposal` | `Id bigint` | UX `(CompanyId, NumberKey)`; FK `TransactionTypeId`, `PartnerId NULL`, `SourceDocumentId NULL` | `(CompanyId, DocumentDate)` |
| `fa.AssetDisposalLine` | `Id bigint` | FK `AssetDisposalId`, `AssetId`; UX `(AssetId) WHERE Status = 3` (o fișă iese o singură dată) | — |
| `fa.AssetDisposalLineBook` | `Id bigint` | FK `AssetDisposalLineId`, `BookCode`; valorile pe regim | — |
| `fa.AssetDisposalRecovery` | `Id bigint` | FK `AssetDisposalId`, `ItemId`, `StockIssueId NULL` (NIR-ul creat) | — |
| `fa.AssetDisposalCharge` | `Id bigint` | FK `AssetDisposalLineId`, `EmployeeId NULL`, `PartnerId NULL` | — |

Procedurile: `fa.usp_AssetDisposal_Validate` (în tranzacția apelantului când vine din Vânzări; `SET XACT_ABORT ON`, verificarea stării cu `UPDLOCK` pe fișă), `fa.usp_AssetDisposal_Reverse`.

**Erorile M5** (`65600–65749`):

| Cod | Nume | HTTP | Când |
| --- | --- | --- | --- |
| 65601 | `FA_ASSET_CANNOT_EXIT` | 409 | Fișa nu e în stările 1–4 (deja ieșită, compusă, anulată) |
| 65602 | `FA_EXIT_BEFORE_LAST_OPERATION` | 409 | Data ieșirii e înaintea ultimei operații validate sau într-o lună închisă |
| 65603 | `FA_ASSET_RESERVED_BY_INVOICE` | 409 | Fișa e pe altă factură `FV-MF` în ciornă sau validată |
| 65610 | `FA_SALE_ONLY_FROM_SALES` | 400 | Încercare de `IMF-VZ` manual (vânzarea pornește doar din factură) |
| 65620 | `FA_COMMITTEE_REQUIRED` | 400 | Casare sau trecere în OB fără comisie și aprobare |
| 65630 | `FA_CHARGE_PERSON_REQUIRED` | 400 | Lipsă imputată fără persoană sau sumă |
| 65640 | `FA_TO_OB_NOT_ALLOWED` | 409 | Trecere în OB a unei fișe neamortizate complet, fără decizie motivată |
| 65650 | `FA_GROUP_PARTNER_NOT_COMPANY` | 400 | `IMF-GRP` către un partener care nu e firmă a grupului |
| 65690 | `FA_EXIT_REVERSE_BLOCKED` | 409 | Stornare în lună închisă sau după amortizare validată |

## 8. Permisiuni și roluri

| Permisiune | Contabil mijloace fixe | Administrator patrimoniu | Șef administrativ | Membru comisie | Operator facturare |
| --- | --- | --- | --- | --- | --- |
| `FixedAssets.Disposal.Create` (casare, donație, aport, OB) | ✓ | ✓ (ciornă) | — | — | — |
| `FixedAssets.Disposal.Approve` | — | — | ✓ | — | — |
| `FixedAssets.Disposal.Validate` | ✓ | — | — | — | — |
| `FixedAssets.Disposal.SignCommittee` | — | ✓ | ✓ | ✓ | — |
| `Sales.Invoice.AssetLine` (linia `FV-MF`) | ✓ | — | — | — | ✓ |
| `FixedAssets.Disposal.Reverse` | ✓ (și Contabilul șef) | — | — | — | — |

## 9. Cerințe legale și integrări

| Cerință | Ce înseamnă pentru M5 |
| --- | --- |
| OMFP 1802 | Scoaterea din evidență la cedare sau când nu se mai așteaptă beneficii; venitul din cedare pe `7583`, valoarea neamortizată pe `6583`; rezerva din reevaluare transferată în `1175` |
| OMFP 2634/2015 | Procesul-verbal de scoatere din funcțiune a mijloacelor fixe, de declasare a bunurilor materiale (14-2-3/aA) |
| Codul fiscal | Valoarea fiscală rămasă deductibilă la vânzare și casare; lipsurile neimputabile nedeductibile (cu excepțiile legale); limitele sponsorizării; TVA la vânzare, la lipsa imputată, la donație și ajustarea pentru bunurile de capital |
| SAF-T D406 | Tranzacțiile de ieșire pe activ (`AssetTransactions` de tip cedare / casare), cu valorile la ieșire |

**Precizări aplicate (27.11.2026):** Vânzări V1 (tranzacția `FV-MF`, articolul tehnic `MF-VANZARE`) și V5 (linia cu `AssetId`, panoul fișei, apelul `IAssetDisposalPort` în tranzacția de validare), V6 (stornarea prin port); Stocuri S2 (tranzacția `NIR-REC` și `IRecoveredMaterialIntakePort`); C2 (cheile `DonationExpense`, `ContributedInvestment`, `AssetContributionGain` / `AssetContributionLoss`, `ChargeReceivableEmployee`, `ChargeIncome`, `RecoveredMaterialIncome` (`7588`) și componentele de ieșire); C3 (concordanța `ASSET_SALES`); C4 (D101: valoarea fiscală rămasă, lipsurile nedeductibile, sponsorizarea; ajustările de TVA).

## 10. Rapoarte

| Raport | Conținut |
| --- | --- |
| Registrul ieșirilor | Pe perioadă, tranzacție, grupă: valorile la ieșire, venitul, rezultatul |
| Rezultatul din cedări | Pe an: venituri `7583`, cheltuieli `6583`, câștig net, pe fișă |
| Imputări | Pe persoană: sume, TVA, încasate sau reținute |
| Materiale recuperate | Din casări: cantități, valori, valorificare |

## 11. Ecrane principale

Machetele sunt în [ecrane/m5-iesiri/](ecrane/m5-iesiri/).

| Ecran | Conținut principal | Machetă |
| --- | --- | --- |
| Factura de vânzare a mijlocului fix | Ecranul de factură din Vânzări cu tranzacția `FV-MF`: linia legată de MF 000203 și panoul fișei | `m5-01-factura-fv-mf` |
| Ieșire prin vânzare | Formularul `IMF 000031` generat din factură: valorile la ieșire pe regim, rezultatul, notele | `m5-02-iesire-vanzare` |
| Casare | `IMF 000032`: comisia, aprobarea, recuperările trimise în stoc, procesul-verbal | `m5-03-casare` |
| Lipsă imputată | `IMF 000034` din inventar: imputarea, TVA-ul, legătura cu lichidarea angajatului | `m5-04-lipsa-imputata` |
| Registrul ieșirilor | Ieșirile anului 2026 pe tranzacții, cu rezultatul | `m5-05-registru-iesiri` |

## 12. Criterii de acceptare și teste cheie

| # | Criteriu | Test |
| --- | --- | --- |
| M5-A1 | Validarea `FV EXD 10302` creează `IMF 000031` în aceeași tranzacție; dacă ieșirea eșuează, factura rămâne ciornă și numărul fiscal nu se consumă | Test de integrare (cu V5) |
| M5-A2 | Valorile din 4.1 la ban, cu cota din decembrie scrisă de ieșire și excluderea din `AML 2026-12` | Test de integrare (cu M3) |
| M5-A3 | O fișă pe o factură în ciornă nu poate fi aleasă pe a doua (`65603`) | Test de integrare |
| M5-A4 | Casarea MF 000118 creează `NIR-REC` în S2 cu 850,00 și nota `3028` = `7588` | Test de integrare (cu S2) |
| M5-A5 | Lipsa imputată din 4.3 dă cele trei note și evenimentul `ImputareInregistrata` | Test de integrare |
| M5-A6 | Ieșirea părintelui fără componente pe document e refuzată (`65110`) | Test de integrare |
| M5-A7 | `IMF-GRP` creează ciorna `MFI-GRP` în firma primitoare cu datele și atașamentele | Test de integrare |
| M5-A8 | Stornarea facturii în aceeași lună readuce fișa în starea anterioară, cu jurnalul inversat | Test de integrare (cu V6) |
| M5-A9 | Concordanța `ASSET_SALES` e 0 după 1.000 de facturi `FV-MF` validate concurent | Test de concurență |

## Decizii

| # | Întrebare | Decizia | Stare |
| --- | --- | --- | --- |
| 1 | De unde pornește vânzarea | Din Vânzări (`FV-MF`), cu ieșirea generată în aceeași tranzacție prin port | ✓ Aprobat autonom (27.11.2026) |
| 2 | Amortizarea lunii ieșirii | Inclusă, scrisă de documentul de ieșire dacă `AML` nu e încă validat | ✓ Aprobat autonom |
| 3 | Rezerva din reevaluare la ieșire | Transferată integral în `1175` în nota ieșirii | ✓ Aprobat autonom |
| 4 | Materialele recuperate la casare | Intrare în stoc prin portul S2 (`NIR-REC`), contul `7588` | ✓ Aprobat autonom |
| 5 | Lipsa la inventar | Generată din M7; imputată (`4282` + TVA) sau neimputabilă (marcată nedeductibilă) | ✓ Aprobat autonom |
| 6 | Transferul în grup | Vânzare cu factură + ciornă de intrare automată la primitor, cu datele tehnice și atașamentele | ✓ Aprobat autonom |
| 7 | Ieșirea parțială | Nu; întâi descompunere (M4), apoi ieșirea fișei rezultate | ✓ Aprobat autonom |
| 8 | Ieșirea părintelui | Cu toate componentele active, adăugate automat | ✓ Aprobat autonom |
| 9 | Trecerea în OB | Doar fișe amortizate complet sau cu decizie motivată; deschide evidența M6 | ✓ Aprobat autonom |
| 10 | Stornarea | Doar în luna deschisă, fără amortizare ulterioară; la vânzare doar prin stornarea facturii | ✓ Aprobat autonom |
