# 02.03 Vânzări · Note de inspirație din „Windows Client”

Stare: **propunere, de discutat cu Aurelian** (27.11.2026). Nu e încă document de arhitectură.

Sursa: `D:\Projects\ERPEnterprise\ERPEnterprise\Docs\arhitectura\Windows Client` (calculatorul lui Aurelian, citit doar), același folder folosit la Achiziții ([04-achizitii/INSPIRATIE_WINDOWS_CLIENT.md](../04-achizitii/INSPIRATIE_WINDOWS_CLIENT.md)). E o instalare de client Windows a unui ERP comercial românesc (Charisma Enterprise). Sursele lizibile folosite aici:

- `Help.chm` (manualul de utilizare), extras cu unealta noastră [`_unelte/chm/chmx.py`](../../../_unelte/chm/chmx.py) (cititor CHM cu decodor LZX, Python pur). Capitolele citite: „Vânzări” (cereri de ofertă, oferte, comenzi, confirmări de comenzi, facturi de vânzare, proforme, storno, facturi de mijloace fixe, facturi de export, avize de expediție, avize retur și storno, configurarea discounturilor), „Liste de prețuri”, „Document de rezervare stoc” (dispoziția de livrare) și „Avansuri clienți”.
- `000520 - uspEFactura_GetInvoiceData.sql`: cum se construiește e-Factura emisă (UBL) dintr-o factură de vânzare.
- `000523 - uspETransport_GenerateXML.sql`: notificarea e-Transport.

Preluăm **idei de comportament**, nu cod, nume de tabele sau ecrane. Totul se reproiectează pe regulile noastre (monolit modular .NET + MediatR, SQL Server cu colația `Latin1_General_100_CS_AS_SC_UTF8`, Dapper + proceduri stocate, fără ORM, Bune practici). Motorul de tranzacții și legături e deja în [Nucleu §3.6](../01-nucleu/NUCLEU.md), validat la Achiziții.

## 1. Fluxul de vânzări acoperit acolo

Cerere de ofertă de la client → Ofertă către client → Comandă de la client → Confirmare de comandă → Aviz de expediție de vânzare → Factură de vânzare. Plus: factură proforma, factură storno (generată din factură, parțial pe cantitate, sau manuală cu cantități negative), factură de vânzare a mijloacelor fixe, factură de export (în valută), aviz retur de la client (generat din avizul de vânzare, face intrare în stoc), aviz storno, dispoziția de livrare către gestiune (cantitate solicitată și rezervată, cu delegat), reglarea și închiderea avansurilor de la clienți.

Fiecare document se face „manual” (tranzacția de înregistrare) sau „automat” (tranzacție de generare din sursă), cu același asistent în trei pași: alegi tranzacția → documentele sursă → liniile, cu cantitatea rămasă propusă și editabilă. Tranzacțiile numite în manual: „Emitere ofertă către client”, „Generare ofertă din cererea de ofertă a clientului”, „Înregistrare comandă de la client”, „Generare comandă din oferta către client”, „Generare confirmare din comanda clientului”, „Generare factură storno din factura de vânzare”, „Generare aviz retur de la client din avizul de expediție de vânzare”.

## 2. Ce am observat, pe zone

| Zonă | Ce face acolo |
| --- | --- |
| Antetul documentului | Număr, dată, client, agent de vânzare, gestiune, prefix + serie + număr fiscal, mod de plată, valută și curs, perioadă de valabilitate (ofertă, comandă), „autorul plății” (plătitor diferit de client), „destinatar livrare” (adresă de livrare), persoane, observații, detalii de expediție |
| Linia | Articol, cantitate, **preț de listă** (din lista de prețuri a documentului, fără TVA) și **preț unitar** (prețul final, după discount), cotă TVA din articol, valori calculate; data livrării pe linie (comandă) |
| Stocul pe linie | La articolele stocabile, linia arată „cantitatea disponibilă” pe gestiune, pe rânduri de stoc; utilizatorul scrie cantitatea pe rândul ales, iar totalul urcă pe linie |
| Liste de prețuri | Denumire, valabilitate de la–până la, valută, tip (de ieșire pentru vânzări, de intrare pentru achiziții, pentru gestiunile la preț de vânzare), preț fără TVA sau cu TVA inclus (cel fără TVA se calculează), activă, implicită; pe articol: UM, preț și data de la care e valabil; generarea unei liste noi dintr-una veche |
| Discounturi | Valabilitate; procentual sau valoric; **automat** (de sistem, obligatoriu, nu se poate scoate de pe document; poate fi marcat „promoție”, iar un parametru face promoțiile exclusive) sau **opțional** (la alegerea utilizatorului; „editabil” între 0 și procentul maxim); **pe linie** (se însumează) sau **pe antet** (în cascadă peste cele de pe linii); **explicit** (linie separată de discount) sau **inclus în preț** (diferența dintre prețul de listă și prețul unitar); baza (valoarea facturii sau valoarea fără accize); tipurile de documente pe care se aplică; filtre (de exemplu total factură > 1.000 lei); grupurile de utilizatori care îl pot folosi. Pe document: „discount manual” cu procent, care anulează discounturile configurate |
| Centre de cost | După validare, cheltuielile și veniturile facturii se repartizează pe centre de cost, procentual sau valoric, manual, după o regulă de alocare sau „copiază prima linie pe toate” |
| e-Factura emisă | Numărul trimis poate fi cel intern sau cel din seria fiscală (parametru); storno trimis ca factură negativă (380) sau ca notă de credit (381), după parametru; cod CPV pe articol sau pe linie (pentru autorități publice); cod GLN pe client și pe adresa de livrare; autofactura (vânzătorul și clientul se inversează); contul bancar al firmei; facturile emise extern se marchează și nu se trimit |
| e-Transport | Tipul operațiunii și scopul ei pe document; persoanele fizice fără CUI au un identificator generic |

## 3. Ce propunem să preluăm

| # | Idee | Cum ar arăta la noi | Parte |
| --- | --- | --- | --- |
| P1 | Toate documentele de vânzare prin tranzacții, manuale sau generate, cu legături pe linie și cantitatea rămasă | Tranzacții livrate pe comportamentele din cod (Nucleu §3.6), ca la Achiziții A1; grupuri de consum proprii vânzărilor (`ORDER`, `DELIVER`, `INVOICE`, `RETURN`, `CREDIT`…) | V1 |
| P2 | Preț de listă + preț unitar pe linie, cu discountul ca diferență | Pe linie: `ListPrice`, discounturile aplicate (cu sursa lor) și `UnitPrice`; prețul de listă vine din `sales.fn_VariantPrice` (Nucleu §3.3.1, regula 9) | V2 |
| P3 | Configurarea discounturilor (automat sau opțional, linie sau antet în cascadă, explicit sau inclus, filtre, roluri, promoții exclusive) | Reguli de discount tipizate (condiții pe client, grupă, articol, familie, cantitate, valoare), fără SQL; ordinea de aplicare fixată; discountul manual cu plafon pe rol și aprobare peste plafon | V2 |
| P4 | Liste de prețuri cu valabilitate, valută, preț cu sau fără TVA, implicită, generare din altă listă | `sales.PriceList` + linii pe variantă, cu dată de valabilitate pe linie, precizie până la 6 zecimale (P7), copiere cu ajustare procentuală; lista cu amănuntul folosită de Stocuri S1 (`RetailPriceListId`) | V2 |
| P5 | Plătitor diferit de client și adresă de livrare pe document | Pe antet: client (cumpărător), plătitor (partida din F3), punct de livrare (adresă din `core.Partner`), cu valori implicite din condițiile clientului | V1, V3 |
| P6 | Cantitatea disponibilă pe linie, pe rânduri de stoc | Pe linia de aviz sau factură cu livrare, panou de disponibil din Stocuri (S5), cu loturile propuse FEFO și termenul minim cerut de client (S4) | V4 |
| P7 | Dispoziția de livrare către gestiune, cu delegat și cantitate solicitată și rezervată | Dispoziție de livrare (pregătire) din comandă, înaintea avizului; devine sarcina de picking când există WMS (02.06) | V4 |
| P8 | Confirmarea comenzii | La noi nu e document separat: e o stare a comenzii plus confirmarea tipărită sau trimisă clientului; diferențele se fac prin revizie (ca la A3) | V3 |
| P9 | Storno și retur generate din sursă, parțial pe cantitate; manual cu cantități negative | Retur de la client din aviz sau factură (intrare în stoc), factura storno din factură sau din retur, limitate de rămasul de stornat; storno manual doar cu drept | V6 |
| P10 | Factura de export în valută; factura de mijloace fixe | Export și livrare intracomunitară ca tranzacții ale facturii (regim TVA, mențiuni, curs); vânzarea de mijloace fixe rămâne a modulului de mijloace fixe, cu factura emisă de Vânzări | V5 |
| P11 | Repartizarea pe centre de cost după validare | Aceleași șabloane de repartizare ca la Achiziții A5, pe venituri | V5 |
| P12 | Avansuri de la clienți, regularizare și închidere | Factura de avans și regularizarea pe factura finală (stornarea avansului pe linie separată), cu partida în F3 | V5 |
| P13 | e-Factura emisă: număr fiscal, storno ca 380 negativ sau 381, CPV, GLN, autofactură, facturi emise extern | Setări de firmă pentru e-Factura; CPV ca proprietate de articol, GLN pe adresa de livrare; trimiterea prin Integrări (02.14) | V5 |
| P14 | e-Transport cu tipul și scopul operațiunii | Pe aviz (sau factura cu livrare): UIT cerut prin Integrări, cu codurile din nomenclatoare separate, cu seed | V4 |
| P15 | Agent de vânzare și zone comerciale; modalitățile de plată | Agentul și zona ca date de vânzări pe client (nu doar proprietăți libere de partener), cu portofoliul agentului și drepturi pe portofoliu; modalitățile de plată în nomenclator | V1 |
| P16 | Cererea de ofertă primită de la client | Opțională: se poate înregistra ca ofertă în ciornă; nu propunem document separat (oportunitățile țin de CRM 02.12) | V3 |

## 4. Ce nu preluăm

- **SQL sau filtre scrise ca text în configurare** (filtrele de discount, parametrii de inițializare cu nume libere, de tipul `ExclusivPromotion`, `DiscCalcTypeId`): la noi condițiile sunt câmpuri tipizate, iar setările sunt coloane în setările de vânzări ale firmei.
- Coduri de tip de document numerice „magice” în proceduri (de exemplu 1007 pentru storno): la noi tipul și comportamentul sunt enumerări în cod.
- `NOLOCK`, id-uri pe intervale per sediu, mesaje de eroare ca text: avem RCSI, `IDENTITY`/`SEQUENCE` și coduri `THROW` (`52000–52999` pentru Vânzări).
- Avize și facturi storno ca tipuri de document separate cu ecran propriu: la noi stornarea e o tranzacție a aceluiași tip de document, cu legătură la sursă.
