# 02.04 Achiziții · Note de inspirație din „Windows Client”

Stare: **propunere, de discutat cu Aurelian** (26.11.2026). Nu e încă document de arhitectură.

Sursa: `D:\Projects\ERPEnterprise\ERPEnterprise\Docs\arhitectura\Windows Client` (calculatorul lui Aurelian, citit doar). Folderul este o instalare de client Windows a unui ERP comercial românesc (Charisma Enterprise, Delphi + .NET Framework, SQL Server). Conține în principal binare; sursele lizibile folosite aici sunt:

- `Help.chm` (manualul de utilizare): capitolele „Tipuri de tranzacții”, „Achiziții” (cereri de aprovizionare, necesar, cereri de ofertă, oferte, comenzi, confirmări, NIR, avize, facturi, storno, import, DVI), „Catalog articole”, „Liste de prețuri”, „Note contabile din documente primare”.
- `000522 - uspEFactura_GeneratePurchaseInvoice.sql` și procedurile e-Factura vecine: cum devine o factură primită prin e-Factura o factură de furnizor, cu legături la documentele sursă.

Preluăm **idei de comportament**, nu cod, nume de tabele sau ecrane. Totul se reproiectează pe regulile noastre (modular monolith .NET + MediatR, SQL Server cu colația `Latin1_General_100_CS_AS_SC_UTF8`, Dapper + proceduri stocate, fără ORM, Bune practici).

## 1. Cum funcționează „tranzacțiile” acolo

**Orice document se creează printr-o tranzacție** (tip de tranzacție configurabil). Tipul de document spune *ce* este documentul (Factură de achiziție, NIR); tranzacția spune *cum* a apărut și *ce face*. De exemplu, NIR-ul are 7 tranzacții: „Înregistrare NIR” (manuală), „Generare NIR din Aviz furnizor”, „din Factura furnizor”, „din Factură de achiziție + facturi auxiliare”, „din Factură de import cu DVI”, „din factură cu TVA inclus în preț” etc.

Ce configurează o tranzacție:

| Grup | Ce conține |
|---|---|
| General | Cod, denumire (apare pe document), activă, manuală sau automată, tipul documentului destinație, contabilă (apare la contare), permite centre de cost, permite linii manuale pe un document generat |
| Documente sursă | Tipurile de documente din care se poate genera; unul e principal; „are detalii” (contează liniile și cantitățile) sau doar antetul (contează valoarea, de exemplu plata unei facturi); ce se verifică drept disponibil (cantitate pe linie, valoare pe linie sau pe antet, în valută sau lei); dacă se permite depășirea disponibilului; în ce stări trebuie să fie sursa (de regulă „Validat”) |
| Mapări de câmpuri | Ce câmp din sursă trece în ce câmp din destinație (furnizor, articol, cantitate, UM, cotă TVA, preț) |
| Comportament stoc | Fără efect, intrare, ieșire, rezervare, ieșire din rezervare; bifa „tranzacție retur”; cazuri speciale de preț de intrare (retur de ieșire, transfer în doi pași) |
| Contabilitate | Vederile de antet și linie din care modelul de contare ia datele |
| Drepturi | Grupurile de utilizatori care pot folosi tranzacția și tranzacția implicită a grupului pentru fiecare tip de document |
| Altele | Proprietăți, tranzacții echivalente (la calculul disponibilului), filtru pe documentele sursă (de exemplu doar facturi auxiliare cu articole de tip Transport), procedură la finalizare |

**Legătura sursă → destinație se ține pe linie**: (document sursă, linia sursă, cantitatea sursă) → (document destinație, linia destinație, cantitatea preluată). Din ea rezultă:

- „cantitatea rămasă” pe orice linie sursă (comandată 1.000, recepționată 990, rămân 10);
- lista „documente sursă” afișată pe NIR și drumul invers (comandă → avize → NIR → facturi);
- limita la retur și storno: nu se poate returna mai mult decât s-a recepționat.

**Generarea are trei pași** (același asistent peste tot): alegi tranzacția → alegi documentele sursă disponibile (filtrate pe partener și stare) → alegi liniile, cu cantitatea rămasă propusă și editabilă → „Generează”.

**Fiecare schimbare de stare e păstrată** într-un istoric (cine, când, ce stare).

## 2. Fluxul de achiziții acoperit acolo

Cerere de aprovizionare (pe departament, cantitate solicitată și aprobată) → Necesar de aprovizionare → Cerere de ofertă → Ofertă furnizor → Comandă furnizor → Confirmare comandă → Aviz de expediție → Factură de achiziție → NIR. Plus: factură proforma, factură storno (manuală, cu cantități negative, sau generată din factură; dacă factura avea NIR, storno-ul face ieșire din gestiune), aviz de retur la furnizor, factură de import, factură de transport extern ca „factură auxiliară”, DVI cu taxe pe cod vamal și repartizarea facturilor auxiliare în costul articolelor, apoi NIR din factura de import cu DVI. Pe facturile de servicii, liniile nestocabile se repartizează pe centre de cost, manual sau după reguli de alocare.

## 3. e-Factura primite → factură de furnizor

1. Factura descărcată se descompune într-o zonă de lucru (antet, linii, plăți, proprietăți pe linie) cu erori și avertizări.
2. Potrivirea automată: furnizorul după CUI; articolul prin **catalogul furnizorului** (codul și denumirea furnizorului legate de articolul nostru și de UM), în cascadă: cod + UM, cod, denumire conținută, denumire după tipar („Chirie%contract%”); UM-urile UBL printr-o tabelă de mapare; tranzacția implicită pentru tipul de document; gestiunea implicită.
3. Utilizatorul corectează ce nu s-a potrivit și poate lega factura (sau liniile ei) de o comandă sau de un NIR.
4. La generare se verifică: nu sunt erori deschise; factura nu există deja (număr + dată + furnizor); totalurile antetului = suma liniilor (valoare, TVA, total); există curs la data facturii; luna nu e închisă operațional; tipul documentului sursă e permis de tranzacție.
5. Se creează factura cu legăturile pe linie către sursă, lotul și termenul de valabilitate din proprietățile liniei, XML-ul atașat și indexul de încărcare ANAF salvat pe document. Există un punct de extensie pentru client.
6. Reducerile: se acceptă atât preț de listă + reducere, cât și preț deja redus; se recalculează procentul.

## 4. Ce propunem să preluăm

| # | Idee | Cum ar arăta la noi |
|---|---|---|
| P1 | Tipul de tranzacție ca strat configurabil între tipul de document și comportament | Nomenclator de client pe tip de document: manual sau generat, surse permise, efect asupra stocului (prin `IStockLedger`), condiție pentru varianta modelului de contare (C2), drepturi pe roluri, implicit pe rol |
| P2 | Legături între documente pe linie, cu cantitate | Registru comun de legături (antet + linie + cantitate) din care rezultă rămasul, istoricul în ambele sensuri și limitele de retur și storno |
| P3 | Asistentul de generare în trei pași | Un singur component de interfață, refolosit de Achiziții, Vânzări, Stocuri |
| P4 | Disponibilul sursei după stare și cantitate, cu bifa „permite depășire” | Regulă pe tranzacție, cu toleranță în procente (de exemplu +5% la recepție) |
| P5 | Catalogul furnizorului și cascada de potrivire | Folosit la e-Factura primite, la importul de comenzi și la tipărirea comenzii cu codurile furnizorului |
| P6 | Zona de lucru pentru e-Factura primite, cu verificările de la §3 | Inbox de facturi primite în Achiziții; descărcarea rămâne în Integrări (02.14) |
| P7 | Facturi auxiliare și DVI repartizate în costul de achiziție | Costuri de achiziție (transport, vamă, comisioane) repartizate pe linii, care corectează costul la S3 |
| P8 | Istoricul stărilor fiecărui document | Deja parțial în audit; un jurnal de stări pe document, uniform |

## 5. Ce nu preluăm

- **SQL scris în configurare** (textul SQL pentru selectarea surselor, maparea coloanelor ținută în tabele): fragil, greu de testat, risc de securitate. La noi maparea e cod tipizat per pereche sursă → destinație, iar tranzacția doar alege dintre comportamente livrate.
- Un singur tabel generic `Document` pentru toate modulele: cuplează modulele. Păstrăm tabelele fiecărui modul; comune sunt doar legăturile (P2) și istoricul de stări (P8).
- Alocarea de id-uri pe intervale per sediu, `NOLOCK`, mesaje de eroare ca text în proceduri: avem `IDENTITY`/`SEQUENCE`, RCSI și coduri `THROW` (`53000–53999` pentru Achiziții).
- Extensii prin proceduri „Custom” apelate din proceduri de bază: la noi, extensiile vin prin evenimente și puncte de extensie documentate.
