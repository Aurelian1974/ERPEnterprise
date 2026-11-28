# 02.03 · Vânzări

**✓ Complet și validat (27.11.2026).** Modulul e documentat în 7 părți, V1–V7, toate validate de Aurelian pe 27.11.2026, după modelul Achizițiilor (02.04): câte un document detaliat pe parte, cu ecranele lui și o listă de decizii validate de Aurelian. Toate documentele de vânzare se creează prin tranzacții configurabile, cu legături pe linie și cantitatea rămasă ([Nucleu §3.6](../01-nucleu/NUCLEU.md)). Note de inspirație din ERP-ul folosit anterior: [INSPIRATIE_WINDOWS_CLIENT.md](INSPIRATIE_WINDOWS_CLIENT.md).

Schema SQL `sales`, prefix de cod `Erp.Sales`, plaja de erori SQL `52000–52999` pe părți: V1 `52000–52099`, V2 `52100–52199`, V3 `52200–52349`, V4 `52350–52499`, V5 `52500–52699`, V6 `52700–52799`, V7 `52800–52899`, rezervă `52900–52999`.

| # | Parte | Document | Stare |
| --- | --- | --- | --- |
| V1 | Configurare: tipuri de documente, tranzacțiile livrate, grupuri de consum, condițiile comerciale ale clientului, catalogul clientului, agenți și zone, setări | [V1-CONFIGURARE.md](V1-CONFIGURARE.md) | ✓ Validat (27.11.2026) |
| V2 | Prețuri și discounturi | [V2-PRETURI-DISCOUNTURI.md](V2-PRETURI-DISCOUNTURI.md) | ✓ Validat (27.11.2026) |
| V3 | Oferte, comenzi și rezervări: oferta (alternative, opționale, cadru, revizii, acceptare), comanda (manuală, din ofertă, importată), confirmarea cu credit, aprobare pe valoare, rezervare și dată promisă, proforma și avansul | [V3-OFERTE-COMENZI.md](V3-OFERTE-COMENZI.md) | ✓ Validat (27.11.2026) |
| V4 | Livrare: dispoziția de livrare (loturi fixate, diferențe), planul zilei, avizul (din comandă, din dispoziție, fără comandă), e-Transport, confirmarea livrării și refuzul, stornarea avizului nefacturat, consignația, avizele nefacturate | [V4-LIVRARE.md](V4-LIVRARE.md) | ✓ Validat (27.11.2026) |
| V5 | Facturare și e-Factura: factura din avize și facturarea în masă, factura cu livrare, factura din comandă și fără sursă, avansul și regularizarea, facturarea recurentă, factura de consignație, categoriile de TVA, scadențele, e-Factura cu corecția după respingere | [V5-FACTURARE.md](V5-FACTURARE.md) | ✓ Validat (27.11.2026) |
| V6 | Retururi, storno și reduceri ulterioare | [V6-RETURURI-STORNO.md](V6-RETURURI-STORNO.md) | ✓ Validat (27.11.2026) |
| V7 | Rapoarte, marjă, comisioane și trasabilitate | [V7-RAPOARTE.md](V7-RAPOARTE.md) | ✓ Validat (27.11.2026) |

## Ecrane

Machetele sunt în [ecrane/](ecrane/), câte un folder pe parte, generate cu uneltele din [`_unelte/machete-vanzari/`](../../../_unelte/machete-vanzari/) (HTML + PNG). Seed-ul nomenclatoarelor: [`seed/nomenclatoare-vanzari/`](../../seed/nomenclatoare-vanzari/).

| Parte | Ecrane |
| --- | --- |
| V1 | `v1-01-tranzactii`, `v1-02-asistent-aviz`, `v1-03-client-conditii-credit`, `v1-04-catalog-client`, `v1-05-agenti-zone`, `v1-06-configurare-vanzari`, `v1-07-drumul-comenzii`, `v1-08-nomenclatoare-vanzari` |
| V2 | `v2-01-liste-preturi`, `v2-02-modificare-pret`, `v2-03-regula-discount`, `v2-04-bonus-marfa`, `v2-05-comanda-preturi`, `v2-06-de-unde-vine-pretul`, `v2-07-rabat-rulaj`, `v2-08-aprobare-pret`, `v2-09-configurare-preturi`, `v2-10-lista-partener-istoric` |
| V3 | `v3-01-lista-oferte`, `v3-02-oferta`, `v3-03-acceptare-oferta`, `v3-04-lista-comenzi`, `v3-05-comanda`, `v3-06-data-promisa`, `v3-07-confirmare`, `v3-08-import-comenzi`, `v3-09-revizii`, `v3-10-proforma`, `v3-11-aprobare-valoare`, `v3-12-configurare` |
| V4 | `v4-01-plan-livrare`, `v4-02-dispozitie`, `v4-03-aviz`, `v4-04-generare-avize`, `v4-05-etransport`, `v4-06-lista-avize`, `v4-07-confirmare-livrare`, `v4-08-consignatie`, `v4-09-avize-nefacturate`, `v4-10-configurare-livrare` |
| V5 | `v5-01-lista-facturi`, `v5-02-factura`, `v5-03-facturare-masa`, `v5-04-factura-avans-regularizat`, `v5-05-facturi-avans`, `v5-06-efactura`, `v5-07-factura-livrare`, `v5-08-facturare-recurenta`, `v5-09-configurare-facturare` |
| V6 | `v6-01-lista-retururi`, `v6-02-retur`, `v6-03-nota-credit-retur`, `v6-04-storno-refacturare`, `v6-05-diferenta-pret`, `v6-06-decont-rabat`, `v6-07-lista-note-credit`, `v6-08-configurare-retururi` |
| V7 | `v7-01-tablou-bord`, `v7-02-vanzari-marja`, `v7-03-tinte`, `v7-04-extras-comision`, `v7-05-nivel-servire`, `v7-06-drumul-documentului`, `v7-07-jurnal-exceptii`, `v7-08-configurare-comisioane` |

Vezi documentul principal: [MODULE_PRINCIPALE.md](../MODULE_PRINCIPALE.md).
