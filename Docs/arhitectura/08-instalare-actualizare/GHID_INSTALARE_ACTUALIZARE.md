# Ghid de instalare și actualizare ERP

19 noiembrie 2026 · Aurelian, Claude · **Validat de Aurelian (19.11.2026)** · rescris de Claude prin delegare, aliniat la versiunea 3 a [specificației](INSTALARE_ACTUALIZARE.md)

Ghid practic pentru cine construiește kitul, cine îl livrează și cine instalează, actualizează sau recuperează ERP-ul la client. Sursa de adevăr rămâne [specificația installer-ului](INSTALARE_ACTUALIZARE.md); ghidul trimite la secțiunile ei și nu le înlocuiește. La o contradicție, prevalează specificația.

**Stare (19.11.2026).** Ghidul descrie comportamentul **țintă**, cel din specificația 3. Codul din depozit implementează încă versiunea anterioară (o aplicație de consolă cu loginuri SQL cu parolă); el se adaptează pe sarcinile T1–T13 (specificația, secțiunea 13). Până la terminarea lor:

| Parte | Stare |
| --- | --- |
| Construirea, semnarea și verificarea kitului (`build/package-kit.ps1`) | Implementat pentru versiunea anterioară; se extinde în T13 |
| Generatorul de licențe `ErpLicenseGen` | De făcut (T1, T2) |
| Fereastra `ErpSetup.exe` | De făcut (T9) |
| Linia de comandă `ErpSetup.Cli.exe` | Există ca `ErpSetup.exe` de consolă (`check`, `install`); se mută și se adaptează (T8) |
| Actualizarea și revenirea | De făcut (T11) |
| Repară, Recuperează, Resetează accesul, Cheia de recuperare, Diagnostic, Dezinstalează, Stare | De făcut (T12) |
| Instalarea cap-coadă pe mașini virtuale Windows (IA-03) | **Neverificată** |

---

## 1. Cine face ce

| Rol | Cine | Ce face | Drepturi necesare |
| --- | --- | --- | --- |
| Producătorul (release) | Echipa noastră (Aurelian) | Construiește și semnează kitul, emite licențele, livrează kitul | Mașina de release, cu cheile kitului și certificatul Authenticode (3.1); calculatorul de emitere a licențelor (3.4) |
| Operatorul installer-ului | Echipa noastră de la distanță sau IT-ul clientului, după acest ghid | Rulează `ErpSetup.exe`: instalare, actualizare, reparare, recuperare | Administrator local pe serverul de aplicație **și** `sysadmin` pe SQL Server, **doar pe durata operației** (specificația 5.1, D17) |
| Administratorul SQL al clientului | IT-ul clientului | Dă temporar `sysadmin` operatorului, pornește SQL Server Agent, alege discurile de date, log și backup; opțional, creează el baza goală (specificația 5.6) | — |
| Administratorul Active Directory | IT-ul clientului | Pentru SQL pe alt calculator: creează contul gMSA al aplicației (5.2) | Domain Admins sau drepturi delegate pe conturile de serviciu |
| IT-ul clientului | IT-ul clientului | Numele DNS al site-ului, certificatul HTTPS (dacă nu se folosește cel intern), firewall-ul de rețea, copierea backup-urilor în afara serverului, păstrarea cheii de recuperare | — |
| Administratorul ERP | Persoana desemnată de client | Primește datele contului `administrator`, schimbă parola, activează 2FA, rulează asistentul de configurare din browser | Contul `administrator` (specificația 5.10) |

Aplicația nu are nicio parolă SQL: se conectează cu identitățile Windows ale pool-ului IIS și ale serviciului, sau cu contul gMSA (specificația 5.8). După instalare sau actualizare, administratorul SQL **retrage** `sysadmin` operatorului; aplicația nu are nevoie de el și nu e nici `db_owner` (`SP-19`).

---

## 2. Kitul: unul singur, pentru toate operațiile

Fiecare versiune livrată are **un singur kit**, `ERP-<Versiune>-kit.zip` (de exemplu `ERP-2027.1.0-kit.zip`), cu aplicația completă, toate scripturile bazei și installer-ul în ambele forme (specificația 7). Același kit:

- face o instalare nouă;
- actualizează o instalare mai veche (doar de la `minUpgradeFrom` în sus; dintr-o versiune mai veche, în trepte, cu kiturile intermediare, cod 72);
- repară sau recuperează o instalare de **aceeași** versiune (cod 74 altfel).

**Ce garantează kitul:** `manifest.json` conține SHA-256 pentru fiecare fișier și e semnat (`manifest.sig`); executabilele sunt semnate Authenticode. Installer-ul verifică totul înainte de orice altă acțiune (C-05): un kit modificat se oprește cu codul 20 sau 21. **Nu modificați nimic în kit**; fișierele de răspunsuri se țin în afara folderului kitului.

**Licența nu face parte din kit.** Este un fișier `.lic` separat, emis pentru fiecare client (specificația 3). Același fișier merge pentru orice versiune lansată până la data „actualizări incluse” a licenței, dacă aceasta e completată (specificația 4.5).

**Păstrați la client kitul versiunii instalate:** e necesar pentru „Repară” și „Recuperează”.

---

## 3. Producătorul: kitul și licențele

### 3.1 Mașina de release (o singură dată)

| Necesar | Detalii |
| --- | --- |
| Windows, PowerShell 7.4+ | `package-kit.ps1` are `#Requires -Version 7.4` |
| .NET SDK 10.0.401 (sau o versiune 10.0.4xx mai nouă) | Fixat în `global.json` |
| Node.js din `.nvmrc` și cheia Syncfusion | Interfața React se construiește în kit (07, ST4) |
| Git, cu depozitul clonat și tagurile versiunilor livrate | Tagurile `AAAA.L.patch` sau `vAAAA.L.patch` (`GIT-05`) servesc la `minUpgradeFrom` și la verificarea `MIG-02` |
| Windows SDK (`signtool.exe`) | Semnarea Authenticode, cu marcă de timp (`-TimestampUrl`) |
| Certificatul de semnare a codului, cu cheie privată, în `CurrentUser\My` al contului care construiește | Se dă prin amprentă (`-CodeSigningCertThumbprint`). Un certificat EV reduce avertismentele SmartScreen la primele descărcări |
| Cheile kitului: activă 1 și de rezervă 2 | Generate o singură dată, **în afara depozitului**: [build/kit-signing/README.md](../../../build/kit-signing/README.md). Cheia privată 2 stă doar offline; ambele chei publice se încorporează în installer (specificația 7, D16) |
| `dotnet-hosting-10.0.<x>-win.exe` | Descărcat de la Microsoft; numele trebuie să aibă exact forma `dotnet-hosting-<versiune>-win.exe` |

### 3.2 Construirea unei versiuni

Procedura e aceeași pentru prima versiune și pentru orice versiune ulterioară; diferă doar numărul versiunii.

1. Verificați că `main` are CI-ul verde (build, teste unitare, Pester, teste SQL de integrare).
2. Alegeți versiunea `AAAA.L.patch`, mai mare decât ultima livrată (de exemplu `2027.1.0`, apoi `2027.1.1` pentru o corecție).
3. Construiți kitul:

   ```powershell
   $env:ERP_KIT_SIGNING_KEY_PASSWORD = Read-Host -MaskInput 'Parola cheii kitului'
   ./build/package-kit.ps1 -Version 2027.1.0 `
       -SigningKeyPath K:\kit-signing\kit-private.pem -PublicKeyPath K:\kit-signing\kit-public.pem `
       -CodeSigningCertThumbprint <amprenta> -HostingBundlePath D:\Prereq\dotnet-hosting-10.0.3-win.exe
   Remove-Item Env:ERP_KIT_SIGNING_KEY_PASSWORD
   ```

   Opțional: `-ReleaseDate AAAA-LL-ZZ` (implicit azi, UTC; contează pentru licențele cu „actualizări incluse”, specificația 4.5), `-MinUpgradeFrom` (implicit calculat din taguri), `-PreviousTag`, `-TimestampUrl`, `-OutputDir` (implicit `artifacts\`).

4. Scriptul face, în ordine, și se oprește la prima eroare:
   - verifică scripturile SQL: numele (`MIG-01`), prefixe unice, `CREATE OR ALTER`, fără `dbo.`, UTF-8 fără BOM și că **nicio migrare deja livrată** nu a fost modificată sau ștearsă (`MIG-02`);
   - publică `Erp.Host`, `Erp.Worker`, `ErpSetup.exe` și `ErpSetup.Cli.exe` (`Release`), cu versiunea și ultima migrare încorporate;
   - copiază scripturile, șabloanele, `CITESTE-MA.txt`, `THIRD-PARTY-NOTICES.txt` și Hosting Bundle-ul;
   - semnează Authenticode binarele proprii;
   - generează și semnează `manifest.json`, apoi arhivează `artifacts\ERP-2027.1.0-kit.zip`.
5. Verificați kitul pe o mașină virtuală de test: instalare nouă prin `ErpSetup.Cli.exe install --unattended` (IA-03), apoi actualizare de la versiunea anterioară, inclusiv cu o migrare defectă intenționat (IA-10).
6. Puneți tagul pe commitul construit și publicați-l: `git tag 2027.1.0` și `git push origin 2027.1.0`. Fără tag, următorul kit nu poate verifica `MIG-02` și calculează greșit `minUpgradeFrom`.
7. Calculați amprenta arhivei pentru livrare: `Get-FileHash .\artifacts\ERP-2027.1.0-kit.zip -Algorithm SHA256`.

**Reguli pentru o versiune de actualizare** (verificate parțial automat la pasul 4): migrațiile livrate nu se modifică niciodată, se adaugă altele noi (`MIG-02`, `MIG-03`); migrațiile merg doar înainte; obiectele programabile și datele livrate trebuie să poată rula de oricâte ori (`MIG-04`, `MIG-06`); modificările pe tabele mari sunt compatibile în urmă (`MIG-05`).

### 3.3 Kitul de test

`./build/package-kit.ps1 -Version 2026.0.1 -TestKit` construiește în `Debug`, cu o pereche de chei generată pe loc în `artifacts\kit-signing-test\`, fără Authenticode și fără Hosting Bundle. Se folosește doar în CI și la teste (`--dev-skip-signature`, disponibil numai în `Debug`). **Un kit de test nu se livrează niciodată**: installer-ul lui are alte chei publice decât kiturile de producție.

### 3.4 Generatorul de licențe

`ErpLicenseGen.exe` se publică separat, nu face parte din kit: `dotnet publish src/Tools/Erp.LicenseGen -c Release`. Rulează doar pe calculatorul de emitere (al lui Aurelian).

**Prima configurare** (butonul „Chei”, specificația 3.5):

1. Generează cheile 1 (activă) și 2 (de rezervă).
2. Cere o parolă pentru copia de siguranță și scrie fișierul cu ambele chei private. **Copiați-l pe două suporturi offline** (de exemplu două stick-uri USB criptate cu BitLocker To Go), păstrate în locuri diferite; parola, separat. Cheia 2 nu rămâne pe calculator.
3. Copiați textul C# afișat în `src/Shared/Erp.Licensing/LicenseKeys.cs` și faceți commit; o versiune ERP acceptă numai cheile compilate în ea.

**Emiterea unei licențe:** completați ecranul, „Validează”, „Generează licența”. Deschideți fișierul generat cu „Deschide o licență” înainte de a-l trimite. Registrul `licente-emise.csv` se copiază pe aceleași suporturi offline, după fiecare licență emisă.

**Rotirea cheilor:** specificația 3.5. Dacă se pierde calculatorul de emitere, se importă cheia 1 din copia offline; dacă cheia 1 e compromisă, se trece pe cheia 2 și se reemit licențele din registru.

---

## 4. Livrarea kitului către client

Decizia D32 (propusă de Claude, validată de Aurelian, 19.11.2026):

- kitul (`ERP-<Versiune>-kit.zip`) se transmite printr-un link de descărcare cu acces limitat (de exemplu un link de partajare cu expirare și parolă) sau direct pe server, în sesiunea de la distanță;
- amprenta SHA-256 a arhivei se transmite **pe alt canal** (de exemplu email, dacă kitul a mers prin link), iar operatorul o compară cu `Get-FileHash` înainte de dezarhivare;
- fișierul de licență se transmite separat de kit;
- clientul păstrează kitul versiunii instalate (pentru „Repară” și „Recuperează”).

Verificarea amprentei e o protecție în plus pentru descărcare; installer-ul verifică oricum semnătura manifestului și hash-urile fiecărui fișier.

---

## 5. Pregătirea serverului (înainte de instalare)

### 5.1 Cerințele

Lista completă este verificarea C-01…C-20 (specificația 5.4); ecranul 4 (sau `ErpSetup.Cli.exe check`) o rulează fără să modifice nimic. Pe scurt, IT-ul clientului pregătește:

| Ce | Cerință |
| --- | --- |
| Serverul de aplicație | Windows Server 2019, 2022 sau 2025, sau Windows 11; Windows 10 22H2 doar cu avertisment (fără actualizări de securitate din 14.10.2025, în afara ESU). 64 de biți; ≥ 4 nuclee, ≥ 8 GB RAM (16 GB recomandat); ≥ 5 GB liberi; ora sincronizată cu SQL Server (< 2 minute) |
| IIS și Hosting Bundle | Se instalează automat dacă lipsesc. Dacă Windows cere repornire, installer-ul o cere și se redeschide singur. Instalarea Hosting Bundle repornește IIS: anunțați utilizatorii altor site-uri de pe același server |
| SQL Server | 2022 sau mai nou, Standard sau Enterprise (Developer și Express doar pentru test); **SQL Server Agent pornit și pe Automatic** (în afară de Express); acces de pe serverul de aplicație pe portul instanței (implicit 1433) |
| Discurile SQL | Date, log și **backup** (de preferat alt disc decât datele); spațiu pentru date ≥ 2 × dimensiunea estimată. Administratorul SQL poate crea el baza goală, cu collation-ul `Latin1_General_100_CS_AS_SC_UTF8`, pe discurile alese; installer-ul o preia (specificația 5.6) |
| SQL pe alt calculator | Ambele calculatoare în același domeniu; contul gMSA al aplicației, creat de administratorul AD cu cel puțin o zi înainte (5.2) |
| Certificatul HTTPS | Implicit, installer-ul generează unul intern; partea publică se distribuie apoi pe calculatoarele utilizatorilor (6.4). Alternativ, un certificat din `LocalMachine\My`, cu cheie privată, adresa în SAN, valabil cel puțin 30 de zile (de exemplu emis de AD CS-ul clientului) |
| DNS | Adresa site-ului (de exemplu `erp.grupulexemplu.local`) indică spre serverul de aplicație |
| Rețea | Utilizatorii → server: 443 (și 80, dacă se folosește redirecționarea); server → internet, ieșire 443: ANAF și BNR (avertisment dacă lipsește); internet → server: **nimic** |
| Drepturi | Contul operatorului: administrator local + `sysadmin` temporar (secțiunea 1) |
| Licența | Fișierul `.lic` copiat pe server, în afara folderului kitului |
| Cheia de recuperare | Un suport extern (stick USB criptat) și un loc în managerul de parole al clientului, pentru parola ei (6.3) |

### 5.2 Contul gMSA al aplicației (SQL Server pe alt calculator)

Secțiune pentru administratorul Active Directory și administratorul SQL ai clientului. Se aplică doar când SQL Server rulează pe alt calculator decât serverul de aplicație. Fundamentul tehnic e în specificație, 5.8 (D21).

#### 5.2.1 Ce este și de ce e nevoie de el

O instalare ERP folosește trei feluri de calculatoare:

```
Calculatoarele utilizatorilor ──HTTPS (browser)──▶ Serverul de aplicație ──SQL (Windows auth)──▶ Serverul SQL
   oricâte, oriunde în rețea                        site IIS ERP-PROD +
   NU se conectează la SQL                          serviciul ErpWorker-PROD
```

Doar serverul de aplicație vorbește cu SQL Server, și o face **fără parolă SQL**, cu identitatea Windows sub care rulează site-ul și serviciul. Contul Windows al utilizatorilor nu ajunge niciodată la SQL Server; drepturile lor le verifică aplicația.

Un **gMSA** (*group Managed Service Account*) este un cont Active Directory destinat serviciilor, nu oamenilor:

- parola o generează și o schimbă **Active Directory**, automat, implicit la 30 de zile; nu o știe, nu o tastează și nu o păstrează nimeni;
- îl pot folosi **numai** calculatoarele permise explicit;
- numele se termină cu `$`, de exemplu `GRUPULEXEMPLU\erp-prod$`.

| Configurare | Cum se prezintă aplicația la SQL Server | Consecință |
| --- | --- | --- |
| SQL pe **același** calculator cu serverul de aplicație | Conturi virtuale locale (`IIS APPPOOL\ERP-PROD`, `NT SERVICE\ErpWorker-PROD`) | Corect; **nu e nevoie de gMSA** |
| SQL pe alt calculator, **cu gMSA** (recomandat) | `GRUPULEXEMPLU\erp-prod$`, folosit numai de ERP | Doar aplicația ERP are acces la bază |
| SQL pe alt calculator, fără gMSA | Contul calculatorului, `GRUPULEXEMPLU\SRV-ERP$` | **Orice** alt program sau site IIS de pe serverul de aplicație care iese în rețea cu contul calculatorului are acces la baza ERP. Installer-ul acceptă varianta, cu avertisment (C-15) |

#### 5.2.2 Cerințe

| Cerință | Detalii | Cum se verifică |
| --- | --- | --- |
| Domeniu Active Directory | Serverul de aplicație și serverul SQL în același domeniu | `(Get-CimInstance Win32_ComputerSystem).Domain` pe fiecare |
| Controler de domeniu Windows Server 2012 sau mai nou | Necesar pentru conturile gMSA | `Get-ADDomainController -Filter * \| Select Name, OperatingSystem` |
| Cheia rădăcină KDS | O singură dată pe pădurea AD, folosită de toate conturile gMSA | `Get-KdsRootKey` (dacă întoarce ceva, există deja) |
| Drepturi pentru administratorul AD | Domain Admins sau drept delegat de creare a obiectelor `msDS-GroupManagedServiceAccount` în containerul ales | — |
| Modulul PowerShell ActiveDirectory pe serverul de aplicație | Pentru verificarea finală (5.2.3, pasul 4). Windows Server: `Install-WindowsFeature RSAT-AD-PowerShell`. Windows 10/11: `Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0` | `Get-Module -ListAvailable ActiveDirectory` |

**Convenția de nume:** `erp-<instanță>` cu litere mici, de exemplu `erp-prod`, `erp-test`; **maximum 15 caractere** (limita numelui de cont Windows). **Câte un cont gMSA pentru fiecare instalare**: instalarea de test nu trebuie să poată accesa baza de producție.

#### 5.2.3 Pașii

Exemplul folosește domeniul `grupulexemplu.local` (NetBIOS `GRUPULEXEMPLU`), serverul de aplicație `SRV-ERP` și instalarea `PROD`. Înlocuiți valorile cu cele reale.

**Pasul 1 — Cheia rădăcină KDS** (administratorul AD, pe un controler de domeniu; doar dacă `Get-KdsRootKey` nu întoarce nimic):

```powershell
Add-KdsRootKey -EffectiveImmediately
```

Cheia devine folosibilă după replicarea între controlerele de domeniu, **până la 10 ore**: planificați pasul cu o zi înainte de instalare. Varianta care o face folosibilă imediat (`-EffectiveTime ((Get-Date).AddHours(-10))`) se folosește doar într-un laborator cu un singur controler de domeniu, niciodată în producție.

**Pasul 2 — Grupul calculatoarelor permise** (administratorul AD). Recomandat: un grup de securitate, nu calculatorul direct. La înlocuirea serverului se schimbă doar membrii grupului, nu contul.

```powershell
New-ADGroup -Name 'ERP-PROD-Servere' -GroupScope Global -GroupCategory Security `
    -Description 'Calculatoarele care pot folosi contul gMSA erp-prod (ERP, instalarea PROD)'
Add-ADGroupMember -Identity 'ERP-PROD-Servere' -Members 'SRV-ERP$'
```

**Pasul 3 — Contul gMSA** (administratorul AD):

```powershell
New-ADServiceAccount -Name 'erp-prod' `
    -DNSHostName 'erp-prod.grupulexemplu.local' `
    -PrincipalsAllowedToRetrieveManagedPassword 'ERP-PROD-Servere' `
    -KerberosEncryptionType AES128, AES256 `
    -Description 'ERP PROD: site-ul IIS și serviciul ErpWorker-PROD'
```

- `-DNSHostName` e un atribut cerut de comandă; nu trebuie să existe o înregistrare DNS cu acest nume.
- `-KerberosEncryptionType AES128, AES256` exclude criptarea RC4, considerată slabă.
- Contul se creează implicit în containerul `Managed Service Accounts`; cu `-Path` se poate alege altă unitate organizațională.
- **Nu adăugați contul în niciun alt grup** și nu îi dați alte drepturi în AD: nu are nevoie.

**Pasul 4 — Serverul de aplicație** (administrator local pe `SRV-ERP`). Calculatorul află că e membru în grupul nou abia după reînnoirea tichetelor Kerberos, deci reporniți serverul sau goliți tichetele contului de sistem:

```powershell
klist -li 0x3e7 purge                         # sau o repornire a serverului
Install-ADServiceAccount -Identity erp-prod   # verifică și pregătește folosirea contului pe acest calculator
Test-ADServiceAccount -Identity erp-prod      # trebuie să întoarcă True
```

**Pasul 5 — Predarea către operatorul installer-ului:** numele contului, `GRUPULEXEMPLU\erp-prod$`. Pe ecranul 1 al `ErpSetup.exe`, după „Testează conexiunea”, se alege „Cont gMSA” și se scrie acest nume; installer-ul îl verifică pe loc (C-15). În fișierul de răspunsuri: `"appIdentity": "Gmsa"` și `"gmsaAccount": "GRUPULEXEMPLU\\erp-prod$"`.

**Administratorul SQL nu are nimic de făcut** pentru acest cont: installer-ul creează loginul, cu dreptul `sysadmin` temporar al operatorului.

#### 5.2.4 Ce face installer-ul cu contul și ce drepturi primește

Contul primește strict ce îi trebuie aplicației:

| Unde | Ce primește | Pasul |
| --- | --- | --- |
| IIS | Identitatea pool-ului `ERP-PROD` (`SpecificUser`, parolă goală; IIS îl adaugă singur în `IIS_IUSRS`) | I-06 |
| Serviciul Windows | Contul de pornire al `ErpWorker-PROD` și dreptul local „Log on as a service” | I-06 |
| Sistemul de fișiere | Citire pe programul din `C:\Program Files\Erp\PROD\` și pe configurarea din `C:\ProgramData\Erp\PROD\`, scriere pe jurnale | I-07 |
| Certificatul instalării | Citire pe cheia privată a certificatului „ERP-PROD Data Protection” | I-08 |
| SQL Server | Loginul `[GRUPULEXEMPLU\erp-prod$]`; în baza `ERP_PROD`, utilizatorul `erp_app`, membru în rolurile de modul (`role_core`, `role_fin` …); **fără** `db_owner` și fără `sysadmin` | I-11 |
| SQL Server, `msdb` | `SELECT` pe `dbo.backupset`, pentru afișarea ultimului backup în aplicație | I-11 |
| Active Directory | Nimic în plus | — |

**Rețeaua.** Serverul de aplicație trebuie să ajungă la serverul SQL pe portul instanței: TCP 1433 pentru instanța implicită; pentru o instanță cu nume, portul ei TCP și UDP 1434 (SQL Server Browser), sau un port TCP fix configurat de administratorul SQL. Comunicarea cu controlerele de domeniu (Kerberos, LDAP) există deja, fiind un calculator din domeniu. Conexiunea spre SQL e criptată (`Encrypt = Mandatory`, C-10).

**Kerberos pentru SQL Server** (recomandat, nu obligatoriu). Dacă serviciul SQL Server are SPN-ul înregistrat (`MSSQLSvc/<server>.<domeniu>:1433`), conexiunea se autentifică prin Kerberos; altfel se folosește NTLM, care funcționează, dar e mai slab. Administratorul SQL verifică cu `setspn -L <contul serviciului SQL Server>` și caută SPN-uri dublate cu `setspn -X`.

#### 5.2.5 Verificarea după instalare

```powershell
# Pe serverul de aplicație: identitatea pool-ului și a serviciului
Import-Module WebAdministration
Get-ItemProperty 'IIS:\AppPools\ERP-PROD' -Name processModel | Select-Object identityType, userName
Get-CimInstance Win32_Service -Filter "Name='ErpWorker-PROD'" | Select-Object Name, StartName, State
```

Ambele trebuie să arate `GRUPULEXEMPLU\erp-prod$`.

```sql
-- Pe SQL Server: sesiunile aplicației folosesc contul gMSA (ApplicationName = ERP-<Instanță>-<Componentă>)
SELECT s.login_name, s.program_name, s.auth_scheme
FROM sys.dm_exec_sessions AS s
JOIN sys.dm_exec_connections AS c ON c.session_id = s.session_id
WHERE s.program_name LIKE N'ERP-PROD-%';
```

Rezultatul așteptat: `login_name = GRUPULEXEMPLU\erp-prod$`, `program_name` `ERP-PROD-Host` și `ERP-PROD-Worker`, `auth_scheme` `KERBEROS` (sau `NTLM` fără SPN). Ecranul „Stare” al installer-ului trebuie să arate `database: Healthy`.

#### 5.2.6 Probleme frecvente

| Simptom | Cauza probabilă | Ce faceți |
| --- | --- | --- |
| `Test-ADServiceAccount` întoarce `False` | Calculatorul nu are încă tichetele cu noul grup; cheia KDS nu e încă folosibilă; calculatorul nu e în grup | `klist -li 0x3e7 purge` sau repornire; așteptați replicarea cheii KDS (până la 10 ore); verificați `Get-ADGroupMember ERP-PROD-Servere` |
| `New-ADServiceAccount` eșuează cu „Key does not exist” | Cheia rădăcină KDS lipsește sau nu e încă folosibilă | Pasul 1, apoi așteptați |
| Serviciul nu pornește, eroarea 1069 („logon failure”) | O politică de grup din domeniu definește „Log on as a service” și înlocuiește dreptul local dat de installer | Adăugați `GRUPULEXEMPLU\erp-prod$` în acea politică de grup |
| Pool-ul IIS se oprește singur; în Event Log, WAS 5021 („identity of application pool is invalid”) | Calculatorul nu poate obține parola contului (nu e în grup sau tichetele sunt vechi) | Ca la primul rând, apoi „Repară” |
| Jurnalul aplicației: „Login failed for user 'GRUPULEXEMPLU\SRV-ERP$'” | Pool-ul sau serviciul rulează cu contul virtual, nu cu gMSA (de exemplu după o modificare manuală) | „Repară” |
| Jurnalul aplicației: „Login failed for user 'NT AUTHORITY\ANONYMOUS LOGON'” | Problemă Kerberos: SPN dublat sau greșit pentru SQL Server | Administratorul SQL: `setspn -X`, corectarea SPN-ului |
| Jurnalul aplicației: „Login failed for user 'GRUPULEXEMPLU\erp-prod$'” | Loginul SQL lipsește, e dezactivat sau utilizatorul din bază nu mai e legat de el (după o restaurare) | „Repară” (recreează loginul și reface legătura) |

#### 5.2.7 Ciclul de viață al contului

- **Parola:** se schimbă automat; nu e nevoie de nicio acțiune și nicio repornire.
- **Înlocuirea serverului de aplicație:** noul calculator se adaugă în grupul `ERP-PROD-Servere`, se face pasul 4 pe el, apoi „Recuperează o instalare existentă” (secțiunea 9.1). La final, vechiul calculator se scoate din grup.
- **Mai multe instalări** (`PROD`, `TEST`): câte un cont și câte un grup pentru fiecare.
- **Retragerea instalării:** după „Dezinstalează”, loginul SQL al contului rămâne dezactivat. Dacă instalarea nu mai revine, administratorul SQL șterge loginul, iar administratorul AD contul (`Remove-ADServiceAccount -Identity erp-prod`) și grupul.
- **Audit:** tot ce face aplicația în SQL Server apare sub numele `erp-prod$`, separat de orice alt program; util la investigații.

**Fără domeniu sau fără gMSA.** Pe calculatoare care nu sunt într-un domeniu, gMSA nu există; atunci SQL Server trebuie să fie pe același calculator cu serverul de aplicație (specificația 5.1). Dacă există domeniu, dar clientul nu vrea un gMSA, installer-ul folosește contul calculatorului, cu avertismentul din 5.2.1.

---

## 6. Instalarea nouă

### 6.1 Pașii

1. **Copiați și verificați kitul.** Verificați amprenta (secțiunea 4), apoi dezarhivați `ERP-<Versiune>-kit.zip` într-un folder local (de exemplu `C:\Kit\ERP-2027.1.0-kit\`).
2. **Porniți `ErpSetup.exe`** cu dublu-clic și confirmați cererea de administrator (UAC).
3. **Ecranul 0:** „Instalare nouă”. Dacă aveți un fișier de răspunsuri de la o instalare anterioară (de exemplu pentru mașina de test), „Încarcă un fișier de răspunsuri…” completează ecranele următoare.
4. **Ecranul 1, Baza de date:** serverul SQL și numele bazei, apoi „Testează conexiunea”. Pentru SQL pe alt calculator, alegeți identitatea aplicației (5.2). Folderul de backup e pe serverul SQL. Restul setărilor de backup sunt în „Avansat”; valorile implicite sunt cele recomandate.
5. **Ecranul 2, Site web:** numele instalării (de exemplu `PROD`), adresa, portul, certificatul.
6. **Ecranul 3, Licență:** alegeți fișierul `.lic`; verificați că lista CUI-urilor e cea a clientului.
7. **Ecranul 4, Verificare:** corectați orice ✗ și reveniți. Citiți avertismentele ⚠ și bifați „Am citit avertismentele”. Opțional, „Salvează răspunsurile…”.
8. **Ecranul 5:** „Instalează”. Durează 5–15 minute; fiecare pas își arată starea.
9. **Ecranul 6:**
   - **notați parola temporară** a administratorului și predați-o administratorului ERP pe un canal sigur; nu se mai afișează niciodată și nu se scrie nicăieri;
   - **„Salvează cheia de recuperare…”**: alegeți un suport extern și o parolă de minimum 12 caractere (6.3);
   - pentru certificatul intern, notați calea fișierului `.cer` afișată;
   - „Închide” devine activ după bifa „Am notat parola” și salvarea cheii.

### 6.2 Dacă instalarea se oprește

Mesajul arată pasul, cauza, codul (specificația 6.3) și ce trebuie făcut. Corectați cauza și apăsați „Reîncearcă”. Dacă ați închis installer-ul, porniți-l din nou: ecranul 0 oferă „Reia instalarea”, care continuă de unde a rămas, cu aceeași bază. Instalarea nouă nu are revenire automată, iar baza creată nu se șterge (specificația 5.5).

Dacă oprirea a avut loc după crearea administratorului și parola nu a fost afișată, folosiți „Resetează accesul administratorului” (secțiunea 10).

**Jurnalul:** `C:\ProgramData\Erp\<Instanță>\logs\setup\` (înainte de crearea instanței, `%TEMP%\ErpSetup\`); butonul „Vezi jurnalul” îl deschide. Nu conține parole.

### 6.3 Cheia de recuperare: de ce și unde

Cheia de recuperare (fișierul `.pfx` + parola) e singura cale prin care, după pierderea serverului de aplicație, ERP-ul recuperat mai poate citi datele criptate: cheile 2FA ale utilizatorilor, parola SMTP, certificatul ANAF și celelalte valori criptate (specificația 5.12). Backup-ul bazei **nu ajunge** fără ea.

- Păstrați fișierul `.pfx` pe cel puțin două suporturi, **separat de backup-urile bazei** (cine are și backup-ul, și cheia, și parola, poate citi tot).
- Păstrați parola în managerul de parole al clientului sau într-un plic sigilat, în alt loc decât fișierul.
- Verificați o dată pe an că fișierul și parola sunt bune: ecranul 0 › „Cheia de recuperare…” › „Verifică o cheie existentă” (sau `ErpSetup.Cli.exe verify-recovery-key`). Nu modifică nimic.
- Dacă se pierde, exportați imediat o cheie nouă din ecranul 0 („Cheia de recuperare…”) sau cu `ErpSetup.Cli.exe export-recovery-key`.

### 6.4 După instalare

- Administratorul ERP se autentifică la `https://<adresă>/`, schimbă parola, activează 2FA și rulează asistentul de configurare (firme, utilizatori, SMTP, certificatul ANAF).
- Administratorul SQL retrage `sysadmin` operatorului.
- IT-ul clientului organizează copierea backup-urilor din folderul de backup **în afara serverului SQL** (installer-ul nu o face; un backup pe același server nu protejează de pierderea lui).
- Pentru certificatul intern: IT-ul distribuie fișierul `C:\ProgramData\Erp\<Instanță>\certificat-erp.cer` în „Trusted Root Certification Authorities” pe calculatoarele utilizatorilor, de preferință prin politică de grup. Certificatul expiră după 2 ani; administratorii văd un avertisment cu 60 de zile înainte, iar „Repară” îl reînnoiește (urmat de o nouă distribuire).
- Păstrați kitul și `C:\ProgramData\Erp\<Instanță>\install\answers.json`.

**Mai multe instanțe pe același server** (de exemplu `PROD` și `TEST`): fiecare cu nume de instalare, adresă și bază diferite; „Instalare nouă” de pe ecranul 0 adaugă una nouă.

---

## 7. Instalarea neasistată, din linia de comandă

Pentru noi (teste pe mașini virtuale, mai multe instalări identice) și pentru IT-ul clientului care o cere. Comenzile și codurile: specificația, secțiunea 6.

1. Pregătiți fișierul de răspunsuri: fie salvat de expert la o instalare anterioară (`install\answers.json`), fie din `templates\answers.example.json` din kit, copiat **în afara kitului** (de exemplu `C:\Kit\answers-PROD.json`). Listați în `acceptedWarnings` avertismentele pe care le acceptați explicit (de exemplu `"C-11"` pentru SQL Developer).
2. Deschideți PowerShell **ca administrator** și treceți în folderul kitului. În PowerShell, comanda se scrie cu `.\` în față.
3. Verificați cerințele, fără modificări:

   ```powershell
   .\ErpSetup.Cli.exe check --answers C:\Kit\answers-PROD.json
   ```

4. Instalați:

   ```powershell
   $env:ERP_RECOVERY_KEY_PASSWORD = [Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'Parola cheii de recuperare')).Password
   .\ErpSetup.Cli.exe install --answers C:\Kit\answers-PROD.json --unattended --recovery-key-out E:\erp-PROD-recuperare.pfx
   Remove-Item Env:ERP_RECOVERY_KEY_PASSWORD
   ```

   Fără `--unattended`, comanda cere confirmarea și parola cheii cu ecou ascuns și afișează parola administratorului o singură dată. Cu `--unattended`, parola administratorului se scrie în `C:\ProgramData\Erp\<Instanță>\install\initial-admin.txt` (doar Administrators și SYSTEM), șters automat după prima schimbare a parolei (specificația 5.10).

5. Codul de ieșire 0 (sau 1, cu avertismentele acceptate) înseamnă reușită. Orice alt cod: specificația 6.3 și secțiunea 11 de mai jos.

---

## 8. Actualizarea

1. Programați actualizarea **în afara programului de lucru**; installer-ul afișează durata estimată înainte de confirmare. Utilizatorii văd pagina „Aplicația se actualizează” pe durata ei.
2. Copiați pe server și dezarhivați kitul **versiunii noi** într-un folder nou (secțiunea 4). Nu e nevoie de fișierul de răspunsuri: setările se citesc din `instance.json`.
3. Cu `sysadmin` temporar pe SQL, porniți `ErpSetup.exe` din kitul nou. Ecranul 0 arată versiunea instalată și versiunea kitului; alegeți „Actualizează”, verificați și confirmați. Din linia de comandă: `.\ErpSetup.Cli.exe upgrade --instance PROD`.
4. Installer-ul verifică versiunile și licența, pune aplicația în mentenanță, face un **backup de siguranță verificat** și un snapshot al bazei, copiază programul nou lângă cel vechi, rulează migrațiile, comută IIS și serviciul și verifică health-check-ul. **La orice eșec revine automat** la versiunea veche, cu baza readusă din snapshot sau din backup (cod 70: instalarea funcționează pe versiunea veche).
5. Cod 71 (și revenirea a eșuat): aplicația rămâne în mentenanță; urmați pașii afișați pe ecran (specificația 10.2) și trimiteți-ne pachetul de diagnostic.
6. Dacă installer-ul a fost întrerupt (de exemplu cădere de curent), ecranul 0 permite doar „Revino la versiunea anterioară” (cod 73 pentru orice altă comandă); după revenire, porniți din nou actualizarea.
7. Cod 41: licența nu include actualizări pentru data de lansare a kitului; cereți o licență prelungită sau folosiți un kit mai vechi.

Actualizarea nu creează și nu modifică utilizatori. Se păstrează ultimele 3 versiuni de program și ultimele 3 backup-uri de actualizare. După actualizare, administratorul SQL retrage `sysadmin`.

---

## 9. Recuperarea după pierderi

Toate scenariile presupun: backup-urile bazei copiate în afara serverului (6.4), cheia de recuperare și parola ei (6.3), kitul versiunii instalate (secțiunea 2).

### 9.1 Serverul de aplicație pierdut, SQL Server intact

1. Pregătiți noul server de aplicație (secțiunea 5). Pentru SQL pe alt calculator cu gMSA: administratorul AD adaugă noul calculator în grupul calculatoarelor permise (de exemplu `ERP-PROD-Servere`), apoi pasul 4 din 5.2.3 pe noul server.
2. Porniți `ErpSetup.exe` din kitul **versiunii bazei** → „Recuperează o instalare existentă”.
3. Ecranul 1: serverul SQL și baza; installer-ul recunoaște instalarea și versiunea ei. Ecranul 2: site-ul. Apoi cheia de recuperare și parola.
4. La final, raportul listează loginurile SQL vechi (ale calculatorului vechi); administratorul SQL le dezactivează.
5. Dacă adresa site-ului s-a schimbat, actualizați DNS-ul; dacă s-a folosit certificatul intern, distribuiți noul `.cer`.

### 9.2 Serverul SQL pierdut

1. Administratorul SQL instalează SQL Server (aceeași versiune sau mai nouă) și restaurează baza din backup-uri: ultimul backup complet, apoi backup-urile de log în ordine, până la momentul dorit. Exemplu:

   ```sql
   RESTORE DATABASE [ERP_PROD] FROM DISK = N'B:\SQLBackup\ERP_PROD\FULL\ERP_PROD_FULL_20270314_010000.bak'
       WITH NORECOVERY, CHECKSUM,
            MOVE N'ERP_PROD_data' TO N'D:\SQLData\ERP_PROD.mdf',
            MOVE N'ERP_PROD_log'  TO N'L:\SQLLog\ERP_PROD_log.ldf';
   RESTORE LOG [ERP_PROD] FROM DISK = N'B:\SQLBackup\ERP_PROD\LOG\ERP_PROD_LOG_20270314_011500.trn' WITH NORECOVERY;
   -- ... fiecare backup de log, în ordine ...
   RESTORE LOG [ERP_PROD] FROM DISK = N'B:\SQLBackup\ERP_PROD\LOG\ERP_PROD_LOG_20270314_104500.trn'
       WITH STOPAT = N'2027-03-14T10:40:00', RECOVERY;
   ```

   Collation-ul și proprietățile extinse ERP se păstrează în backup.
2. Porniți SQL Server Agent.
3. Pe serverul de aplicație, `ErpSetup.exe` → „Repară” (dacă serverul SQL are același nume) sau „Recuperează o instalare existentă” (dacă s-a schimbat). Installer-ul recreează loginurile aplicației, reface legătura utilizatorilor din bază și joburile de backup. Cheia de recuperare se cere doar la „Recuperează” și doar dacă serverul de aplicație e nou.
4. Primul backup complet pornește automat: restaurarea a întrerupt lanțul backup-urilor de log.

### 9.3 Ambele servere pierdute

9.2 (restaurarea bazei), apoi 9.1 (recuperarea pe noul server de aplicație, cu cheia de recuperare).

### 9.4 Fără cheia de recuperare

Recuperarea continuă doar după confirmarea explicită că datele criptate se pierd. După ea:

- administratorul folosește „Resetează accesul administratorului” (2FA se reactivează la autentificare);
- fiecare utilizator cu 2FA are nevoie de o resetare a 2FA de la administrator;
- administratorul reintroduce parola SMTP și certificatul ANAF în Configurare;
- valorile criptate cu cheile vechi rămân ilizibile;
- exportați imediat o cheie de recuperare nouă.

---

## 10. Celelalte acțiuni

Toate pornesc din ecranul 0 al `ErpSetup.exe` (sau din linia de comandă, specificația 6.1) și cer administrator local + `sysadmin` temporar.

| Acțiune | Când |
| --- | --- |
| **Stare** (informațiile din ecranul 0; `status`) | Verificare rapidă: versiune, health-check, ultimul backup, expirarea licenței și a certificatului, data cheii de recuperare |
| **Repară** (`repair`) | Ceva a fost stricat în IIS, serviciu, drepturi, configurare sau joburi; certificatul intern expiră; trebuie încărcată o licență când nimeni nu se poate autentifica. Cere kitul **aceleiași** versiuni |
| **Recuperează o instalare existentă** (`recover`) | Secțiunea 9 |
| **Resetează accesul administratorului** (`reset-admin`) | Niciun administrator nu se mai poate autentifica (parolă pierdută, telefon 2FA pierdut, cont blocat). Parola nouă se afișează o singură dată |
| **Cheia de recuperare…** (`export-recovery-key`, `verify-recovery-key`) | Cheia s-a pierdut, se schimbă locul de păstrare, sau verificarea anuală (6.3) |
| **Pachet de diagnostic** (`diagnose`) | Pentru suport: ZIP fără secrete și fără date de business |
| **Dezinstalează** (`uninstall`) | Elimină site-ul, serviciul și programul; **păstrează** baza, backup-urile, joburile de backup și certificatul instalării (specificația 11.6) |

---

## 11. Coduri de ieșire: ce faceți

Lista completă: specificația 6.3. Fereastra afișează același cod în mesajele de eroare.

| Cod | Ce faceți |
| --- | --- |
| 0 / 1 | Reușit (1 = cu avertismente; citiți-le) |
| 2 | Anulat sau installer-ul rulează deja; închideți cealaltă fereastră și încercați din nou |
| 3 | Comandă sau opțiune greșită; `ErpSetup.Cli.exe --help` |
| 10 | Porniți installer-ul ca administrator, pe un Windows suportat |
| 11 | Fișierul de răspunsuri e invalid sau lipsește o opțiune; mesajul indică fiecare câmp |
| 12 | Cerințe neîndeplinite: corectați fiecare ✗; la `--unattended`, listați în `acceptedWarnings` avertismentele acceptate |
| 20 / 21 | Kit nesemnat, modificat sau incomplet: descărcați-l din nou și verificați amprenta |
| 30 | Eroare SQL; detaliile sunt în jurnal; după corectare, „Reîncearcă” |
| 31 | Baza există și are date sau aparține altei instalări: alt nume de bază, sau „Recuperează” dacă e baza acestei instalări |
| 32 | Baza existentă are alt collation decât `Latin1_General_100_CS_AS_SC_UTF8`; cereți administratorului SQL o bază nouă |
| 40 | Licență invalidă sau expirată; cereți o licență nouă |
| 41 | Licența nu include actualizări pentru acest kit; cereți prelungirea sau folosiți un kit mai vechi |
| 50 | Eroare IIS, serviciu, certificat sau fișiere; detaliile sunt în jurnal |
| 60 | Aplicația nu a pornit corect; installer-ul afișează verificările eșuate și ultimele rânduri din jurnale |
| 70–73 | Actualizare: secțiunea 8 |
| 74 | Kitul nu are versiunea instalării sau a bazei; folosiți kitul versiunii cerute |
| 80 | Cheia de recuperare lipsește, nu corespunde sau parola e greșită (secțiunea 9) |

---

## 12. Ce nu se face

- Nu se modifică fișierele din kit și nu se folosește un kit de test la client.
- Nu se scriu parole în fișierul de răspunsuri, în linia de comandă sau în variabile de mediu la nivel de sistem.
- Nu se creează și nu se modifică manual baza, loginurile, joburile sau site-ul IIS; pentru refacere se folosește „Repară”.
- Nu se lasă `sysadmin` operatorului după terminarea operației.
- Nu se șterge `C:\ProgramData\Erp\<Instanță>\` și nu se șterge certificatul „ERP-<Instanță> Data Protection” din `LocalMachine\My`.
- Nu se păstrează cheia de recuperare în același loc cu backup-urile bazei.

---

## 13. Dezvoltare locală, fără installer

Pentru dezvoltare, aplicația se leagă direct la o bază locală; installer-ul nu e necesar. Configurarea de dezvoltare nu se publică și nu ajunge în kit.

1. **Baza**, pe serverul SQL de dezvoltare (implicit `Valeria`, baza `ERP_DEV`, cu contul Windows curent, care trebuie să fie `sysadmin` pe acea instanță):

   **De obicei nu trebuie rulat nimic:** `Erp.Host` pornit în Debug, în mediul `Development`, creează baza dacă lipsește și o ține la zi la fiecare salvare a unui script SQL ([07, „Mediul de dezvoltare”](../07-stack-tehnologic/STACK_TEHNOLOGIC.md)). Parola administratorului după o reconstruire se pune o singură dată în user-secrets: `dotnet user-secrets set "DevDatabase:AdminPassword" "<parola>" --project src/Host/Erp.Host`.

   Fără `Erp.Host`, aceeași sincronizare din linia de comandă:

   ```powershell
   ./build/dev-database.ps1 -CreateAdmin            # afișează o singură dată parola temporară a lui administrator
   ./build/dev-database.ps1                         # aplică doar scripturile schimbate; reface baza dacă o migrare aplicată s-a modificat
   ./build/dev-database.ps1 -Server <server> -Database <bază>
   ```

   Rulează aceiași pași SQL ca installer-ul: baza case sensitive (`Latin1_General_100_CS_AS_SC_UTF8`), bootstrap, migrații DbUp, obiecte programabile, date livrate și, cu `-CreateAdmin`, administratorul inițial. **Nu** creează loginurile aplicației, joburile SQL Agent sau licența.

   `./build/dev-database.ps1 -SampleData` șterge toată structura organizatorică din `ERP_DEV` (firme, grupuri, puncte de lucru, locații, conturi bancare, serii, rolurile pe firme și intrările lor de audit) și încarcă Grupul Exemplu din macheta 03: 2 grupuri, 4 firme, 5 puncte de lucru, 12 locații, conturi bancare și seriile 2026 (scriptul `build/dev-sample-data.sql`, prin procedurile aplicației; refuză orice bază care nu se numește `ERP_DEV*`). Util și după testele E2E, care adaugă date.

2. **Aplicația:** `dotnet run --project src/Host/Erp.Host` (`https://localhost:7180`) și `dotnet run --project src/Worker/Erp.Worker`. În mediul `Development` (din `Properties/launchSettings.json`) citesc `appsettings.Development.json`: serverul `Valeria`, baza `ERP_DEV`, autentificare Windows. Pentru alt server sau altă bază, fără să modificați fișierul din Git:

   ```powershell
   dotnet user-secrets set "Database:Server" "<server>" --project src/Host/Erp.Host
   dotnet user-secrets set "Database:Name" "<bază>" --project src/Host/Erp.Host
   ```

   Host și Worker folosesc același `UserSecretsId`, deci setarea se aplică amândurora. Dacă browserul nu acceptă certificatul local: `dotnet dev-certs https --trust` (o singură dată pe mașină; confirmați fereastra Windows).

   Deschideți `https://localhost:7180/`: fără sesiune, aplicația trimite browserul la `/account/login`. La prima autentificare a administratorului se cer parola nouă și apoi activarea 2FA (aplicație TOTP, de exemplu Microsoft Authenticator); după aceea `/` arată interfața React (dacă a fost construită, pasul 3), altfel o pagină provizorie.

3. **Interfața React** (`src/Web/erp-web`, Node.js din `.nvmrc`; cheia Syncfusion din variabila `SYNCFUSION_LICENSE` a utilizatorului, activată la `npm install`):

   ```powershell
   cd src/Web/erp-web
   npm install           # o dată și după schimbarea package.json
   npm run dev           # http://localhost:5173, cu reîncărcare la salvare; API-ul și /account/* merg la Erp.Host
   npm run build         # construiește în src/Host/Erp.Host/wwwroot, servită de Erp.Host pe https://localhost:7180
   npm test              # teste Vitest; npm run lint și npm run typecheck pentru verificări
   npm run e2e           # Playwright pe https://localhost:7180 (Host pornit, interfața construită)
   npm run api           # după schimbarea API-ului: regenerează clientul din OpenAPI (Host pornit în Development)
   ```

   `npm run e2e` resetează înainte utilizatorii de test `e2e.operator` (rol STANDARD) și `e2e.admin` (rol ADMIN la nivelul instalării, pentru firme și serii): `./build/dev-database.ps1 -E2EUser <nume> -E2ERole STANDARD|ADMIN -PasswordFile <fișier>` (parolă temporară nouă, fără 2FA obligatoriu, numai în baza de dezvoltare). Fiecare rulare adaugă în `ERP_DEV` un grup, o firmă, un cont bancar și o serie cu coduri unice. Prima dată: `npx playwright install chromium`.

4. **Ce e normal în dezvoltare:** licența se citește din bază (specificația 4.1); fără o licență validă, în `Development` toate modulele sunt active, iar `/health/ready` raportează `license: Unhealthy`. O licență se încarcă din Administrare › Configurare › Licență; ea trebuie semnată cu o cheie din `LicenseKeys.cs`, gol până la configurarea cheilor de producție (3.4). Până la T4, cheile Data Protection stau în profilul utilizatorului; după T4, în bază. Protecția de versiune acceptă în dezvoltare orice bază migrată, cu un avertisment în jurnal.

5. **Porturi în dezvoltare** (fixe; `strictPort` la Vite, deci un conflict oprește pornirea în loc să schimbe portul):

   | Serviciu | Adresă | Definit în |
   | --- | --- | --- |
   | Erp.Host (API, `/account/*`, copia construită a interfeței) | `https://localhost:7180` | `src/Host/Erp.Host/Properties/launchSettings.json` |
   | Interfața React cu reîncărcare la salvare (proxy către Host) | `http://localhost:5173` | `src/Web/erp-web/vite.config.ts` |
   | Erp.Worker | niciunul (nu ascultă pe rețea) | — |
   | SQL Server | instanța implicită, TCP 1433 | `appsettings.Development.json` |

   În dezvoltare se lucrează pe `5173`: `7180` servește interfața construită la ultimul `npm run build` și rămâne în urmă față de cod. Pornire/oprire manuală: `./build/dev-start-backend.ps1`, `./build/dev-stop-backend.ps1`, `./build/dev-start-frontend.ps1`, `./build/dev-stop-frontend.ps1`; ele refuză pornirea dacă portul e ocupat de alt proces. În producție portul HTTPS e cel ales în installer (implicit 443).
Bazele `ERP_DEV` nu se folosesc pentru teste de instalare: o instalare reală se face cu `ErpSetup.exe`, pe o mașină virtuală Windows.
