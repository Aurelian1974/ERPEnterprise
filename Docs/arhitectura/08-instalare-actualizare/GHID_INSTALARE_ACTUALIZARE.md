# Ghid de instalare și actualizare ERP

15 noiembrie 2026 · **Propus, de revizuit de Aurelian** · Specificația: [08 Instalare și actualizare](INSTALARE_ACTUALIZARE.md)

Ghid practic pentru cine construiește kitul, cine îl livrează și cine îl instalează sau actualizează la client. Sursa de adevăr rămâne [specificația installer-ului](INSTALARE_ACTUALIZARE.md); ghidul trimite la secțiunile ei și nu le înlocuiește. La o contradicție, prevalează specificația.

**Stare (15.11.2026):**

| Parte | Stare |
| --- | --- |
| Construirea kitului (`build/package-kit.ps1`), semnarea și verificarea lui | Implementat (T3, T4) |
| `ErpSetup.exe check` și `ErpSetup.exe install`, inclusiv reluarea unei instalări întrerupte | Implementat (T5–T13); instalarea cap-coadă pe Windows Server (IA-01, IA-08) **nu** a fost încă verificată pe o mașină virtuală dedicată |
| `upgrade`, `rollback` | **De făcut** (T14); secțiunea 7 descrie comportamentul specificat |
| `repair`, `reset-admin`, `uninstall`, `diagnose`, `status` | **De făcut** (T15); acum se termină cu codul 99 („nu este încă implementată”) |
| `CITESTE-MA.txt`, `THIRD-PARTY-NOTICES.txt` în kit | **De făcut** (T16); fără ele, `package-kit.ps1` refuză un kit livrabil |
| Canalul de livrare a kitului către client | **Nedecis**; secțiunea 4 conține doar o propunere |

---

## 1. Cine face ce

| Rol | Cine | Ce face | Drepturi necesare |
| --- | --- | --- | --- |
| Producătorul (release) | Echipa noastră (Aurelian) | Construiește și semnează kitul, emite licența, livrează kitul | Mașina de release cu cheia de semnare a kitului și certificatul Authenticode (secțiunea 3) |
| Operatorul installer-ului | Echipa noastră de la distanță (VPN, TeamViewer) sau IT-ul clientului, după acest ghid | Pregătește fișierul de răspunsuri, rulează `check`, `install`, apoi `upgrade` | Administrator local pe serverul de aplicație **și** `sysadmin` pe SQL Server, **doar pe durata rulării** (specificația 1, 3.3, C-01, C-12) |
| Administratorul SQL al clientului | IT-ul clientului | Dă temporar `sysadmin` operatorului, pornește SQL Server Agent, alege discurile de date, log și backup | — |
| IT-ul clientului | IT-ul clientului | Certificatul HTTPS, DNS-ul pentru `hostName`, firewall-ul de rețea, copierea backup-urilor în afara serverului | — |
| Administratorul ERP | Persoana desemnată de client | Primește datele contului `administrator`, schimbă parola, activează 2FA, rulează asistentul de configurare din browser | Contul `administrator` creat de installer (8.8) |

De ce `sysadmin`: installer-ul creează baza, loginurile, joburile SQL Agent cu proprietarul `sa` și folderele de backup (`xp_create_subdir`); fără `sysadmin` se oprește la verificarea C-12 cu codul 12 (decizie 15.11.2026). După instalare sau actualizare, administratorul SQL **retrage** `sysadmin`; aplicația nu are nevoie de el (contul ei nu are nici `db_owner`, `SP-19`).

Conturile create de installer (specificația 3.3): `erp_<instanță>_app` (aplicația), `erp_<instanță>_mig` (migrații, **dezactivat** între rulări), pool-ul `IIS AppPool\ERP-<Instanță>-Host`, serviciul `NT SERVICE\ErpWorker-<Instanță>`. Parolele lor sunt generate și nu se dau nimănui.

---

## 2. Kitul: un singur tip, pentru instalare și actualizare

Nu există un „kit de instalare” și un „kit de update” diferite. Fiecare versiune livrată are **un singur kit**, `ERP-<Versiune>-kit.zip` (de exemplu `ERP-2027.1.0-kit.zip`), care conține aplicația completă, toate scripturile bazei și installer-ul (structura: specificația 4.1). Același kit:

- face o instalare nouă: `ErpSetup.exe install --answers <fișier>`;
- actualizează o instalare existentă: `ErpSetup.exe upgrade --instance <INSTANȚĂ>` (T14);
- repară o instalare de **aceeași** versiune: `ErpSetup.exe repair --instance <INSTANȚĂ>` (T15).

La actualizare, installer-ul rulează doar migrațiile noi (DbUp ține evidența în `core.SchemaVersion`), reface obiectele programabile și datele livrate și copiază binarele noi într-un folder separat `versions\<Versiune>\`. Câmpul `minUpgradeFrom` din `manifest.json` spune din ce versiune se poate actualiza direct (versiunile livrate în ultimele 12 luni); dintr-o versiune mai veche, actualizarea se face în trepte, cu kiturile intermediare (cod 72 altfel, specificația 10.1).

**Ce garantează kitul:** `manifest.json` conține SHA-256 pentru fiecare fișier și e semnat (`manifest.sig`, ECDSA P-256); `ErpSetup.exe` și DLL-urile proprii sunt semnate Authenticode. Installer-ul verifică totul înainte de orice altă acțiune (specificația 4.3): un kit modificat se oprește cu codul 20 sau 21. **Nu modificați nimic în kit**, nici fișierele de șablon; fișierul de răspunsuri se ține în afara folderului kitului.

**Licența nu face parte din kit.** Este un fișier `.lic` separat, emis de instrumentul intern de licențe pentru fiecare client (modulele, CUI-urile grupului, limitele, expirarea; specificația 8.9). Același fișier merge pentru orice versiune până la expirare.

---

## 3. Construirea kitului (producătorul)

### 3.1 Mașina de release (o singură dată)

| Necesar | Detalii |
| --- | --- |
| Windows, PowerShell 7.4+ | `package-kit.ps1` are `#Requires -Version 7.4` |
| .NET SDK 10.0.401 (sau o versiune 10.0.4xx mai nouă) | Fixat în `global.json` |
| Git, cu depozitul clonat și tagurile versiunilor livrate | Tagurile `AAAA.L.patch` sau `vAAAA.L.patch` (`GIT-05`) servesc la `minUpgradeFrom` și la verificarea `MIG-02` |
| Windows SDK (`signtool.exe`) | Semnarea Authenticode |
| Certificatul de semnare a codului, cu cheie privată, în `CurrentUser\My` al contului care construiește | Se dă prin amprentă (`-CodeSigningCertThumbprint`); `signtool /sha1` caută în acest magazin |
| Cheia de semnare a kitului, `kit-private.pem` și `kit-public.pem` | Generată o singură dată, **în afara depozitului**: [build/kit-signing/README.md](../../../build/kit-signing/README.md). Cheia publică se încorporează în `ErpSetup.exe`; dacă o pierdeți, kiturile noi nu mai pot fi verificate de installer-ele vechi și invers |
| `dotnet-hosting-10.0.<x>-win.exe` | Descărcat de la Microsoft; numele trebuie să aibă exact forma `dotnet-hosting-<versiune>-win.exe` |
| Cheile publice de licență | `src/Shared/Erp.Licensing/Keys/<keyId>.pem`. **Acum folderul e gol**, deci orice licență reală e refuzată; cheia publică de producție trebuie adăugată înainte de primul kit livrabil |

### 3.2 Construirea unei versiuni (prima versiune sau o actualizare)

Procedura e aceeași pentru prima versiune și pentru orice versiune ulterioară; diferă doar numărul versiunii.

1. Verificați că `main` are CI-ul verde (build, teste unitare, Pester, teste SQL de integrare).
2. Alegeți versiunea `AAAA.L.patch`, mai mare decât ultima livrată (de exemplu `2027.1.0`, apoi `2027.1.1` pentru o corecție).
3. Construiți kitul:

   ```powershell
   $env:ERP_KIT_SIGNING_KEY_PASSWORD = Read-Host -MaskInput 'Parola cheii kitului'
   ./build/package-kit.ps1 -Version 2027.1.0 `
       -SigningKeyPath K:\kit-signing\kit-private.pem -PublicKeyPath K:\kit-signing\kit-public.pem `
       -CodeSigningCertThumbprint <amprenta> -HostingBundlePath D:\Prereq\dotnet-hosting-10.0.3-win.exe
   ```

   Opțional: `-ReleaseDate AAAA-LL-ZZ` (implicit azi, UTC), `-MinUpgradeFrom` (implicit calculat din taguri), `-PreviousTag` (implicit tagul celei mai mari versiuni mai mici), `-OutputDir` (implicit `artifacts\`).

4. Scriptul face, în ordine (și se oprește la prima eroare):
   - verifică scripturile SQL: numele (`MIG-01`), prefixe unice, `CREATE OR ALTER`, fără `dbo.`, UTF-8 fără BOM și că **nicio migrare deja livrată** (prezentă la tagul anterior) nu a fost modificată sau ștearsă (`MIG-02`);
   - publică `Erp.Host`, `Erp.Worker` și `ErpSetup.exe` (`Release`), cu versiunea și ultima migrare încorporate;
   - copiază scripturile, șabloanele, `CITESTE-MA.txt`, `THIRD-PARTY-NOTICES.txt` și Hosting Bundle-ul;
   - semnează Authenticode binarele proprii;
   - generează și semnează `manifest.json`, apoi arhivează `artifacts\ERP-2027.1.0-kit.zip`.
5. Verificați kitul: dezarhivați-l într-un folder nou și rulați `ErpSetup.exe check --answers <fișier de test>` pe un server de test; pentru o versiune nouă, rulați și o actualizare de test de la versiunea anterioară (după T14).
6. Puneți tagul pe commitul construit și publicați-l: `git tag 2027.1.0` și `git push origin 2027.1.0`. Fără tag, următorul kit nu poate verifica `MIG-02` și calculează greșit `minUpgradeFrom`.
7. Calculați amprenta arhivei pentru livrare: `Get-FileHash .\artifacts\ERP-2027.1.0-kit.zip -Algorithm SHA256`.

**Reguli pentru o versiune de actualizare** (verificate parțial automat la pasul 4): migrațiile livrate nu se modifică niciodată, se adaugă altele noi (`MIG-02`, `MIG-03`); migrațiile merg doar înainte, fără scripturi de întoarcere; obiectele programabile și datele livrate trebuie să poată rula de oricâte ori (`MIG-04`, `MIG-06`).

### 3.3 Kitul de test

`./build/package-kit.ps1 -Version 2026.0.1 -TestKit` construiește în `Debug`, cu o pereche de chei generată pe loc în `artifacts\kit-signing-test\`, fără Authenticode și fără Hosting Bundle. Se folosește doar în CI și la teste (`ErpSetup.exe ... --dev-skip-signature`). **Un kit de test nu se livrează niciodată**: installer-ul lui are altă cheie publică decât kiturile de producție.

---

## 4. Livrarea kitului către client

**Nedecis.** Specificația nu stabilește canalul de livrare. Propunere, de validat de Aurelian:

- kitul (`ERP-<Versiune>-kit.zip`) se transmite printr-un link de descărcare cu acces limitat sau direct pe server, în sesiunea de la distanță;
- amprenta SHA-256 a arhivei se transmite pe alt canal (de exemplu email), iar operatorul o compară cu `Get-FileHash` înainte de dezarhivare;
- fișierul de licență se transmite separat de kit;
- se păstrează local, la client, kitul versiunii instalate (necesar pentru `repair` și pentru procedura manuală după codul 71, specificația 10.6).

Verificarea amprentei e o protecție în plus; installer-ul verifică oricum semnătura și hash-urile fiecărui fișier.

---

## 5. Pregătirea serverului (înainte de instalare)

Lista completă este verificarea C-01…C-23 (specificația 7); comanda `check` o rulează fără să modifice nimic. Pe scurt, IT-ul clientului pregătește:

| Ce | Cerință |
| --- | --- |
| Serverul de aplicație | Windows Server 2019, 2022 sau 2025, 64 de biți; ≥ 4 nuclee, ≥ 16 GB RAM (8 GB = avertisment); ≥ 5 GB liberi pentru `InstallRoot` și `DataRoot`; TLS 1.2 activ; ora sincronizată cu SQL Server (< 2 minute) |
| IIS și Hosting Bundle | Se instalează automat dacă lipsesc și `prerequisites.installIisFeatures` / `installHostingBundle` sunt `true` (implicit). Dacă Windows cere repornire, reporniți și rulați aceeași comandă din nou |
| SQL Server | 2022 sau mai nou, Standard sau Enterprise (Express e refuzat: nu are SQL Agent; Developer doar pentru test); **SQL Server Agent pornit și pe Automatic**; acces de pe serverul de aplicație pe portul instanței (implicit 1433) |
| Discurile SQL | Căile de date, log și **backup** (`backup.backupRoot`, de preferat alt disc decât datele); spațiu pentru date ≥ 2 × dimensiunea inițială |
| Certificatul HTTPS | În `LocalMachine\My`, cu cheie privată, valabil cel puțin 30 de zile, cu `hostName` în SAN sau CN, EKU Server Authentication. Pentru test se poate folosi `site.allowSelfSignedCertificate = true` (avertisment) |
| DNS | `hostName` (de exemplu `erp.grupulexemplu.local`) indică spre serverul de aplicație |
| Rețea | Utilizatorii → server: 443 (și 80 doar pentru redirect); server → internet, ieșire 443: ANAF și BNR (avertisment dacă lipsește); internet → server: **nimic** (specificația 3.4) |
| Drepturi | Contul operatorului: administrator local + `sysadmin` temporar (secțiunea 1) |
| Licența | Fișierul `.lic` copiat pe server, de exemplu lângă fișierul de răspunsuri |

---

## 6. Instalarea nouă

1. **Copiați și verificați kitul.** Dezarhivați `ERP-<Versiune>-kit.zip` într-un folder local (de exemplu `C:\Kit\ERP-2027.1.0-kit\`), după verificarea amprentei (secțiunea 4).
2. **Pregătiți fișierul de răspunsuri.** Copiați `templates\answers.example.json` **în afara kitului** (de exemplu `C:\Kit\answers-PROD.json`) și completați-l. Obligatorii: `site.hostName`, `site.certificateThumbprint` (sau `allowSelfSignedCertificate`), `sql.server`, `backup.backupRoot`, `license.filePath`; restul au valori implicite (specificația 6.2). Fișierul **nu conține parole**.
3. **Deschideți PowerShell ca administrator** (clic dreapta pe PowerShell → „Executare ca administrator”) și treceți în folderul kitului: `cd C:\Kit\ERP-2027.1.0-kit`. `ErpSetup.exe` este o aplicație de consolă și primește comanda pe linia de comandă; nu se pornește cu dublu-clic. Pornit așa, afișează doar ajutorul și așteaptă Enter pentru a închide fereastra. În PowerShell, comanda se scrie cu `.\` în față (`.\ErpSetup.exe ...`); fără el, PowerShell răspunde „The term 'ErpSetup.exe' is not recognized”.
4. **Verificați cerințele** (nu modifică nimic):

   ```powershell
   .\ErpSetup.exe check --answers C:\Kit\answers-PROD.json
   ```

   Corectați tot ce e „Blocant” și rulați din nou până la cod 0 (sau 1, doar avertismente acceptate).
5. **Instalați**, interactiv:

   ```powershell
   .\ErpSetup.exe install --answers C:\Kit\answers-PROD.json
   ```

   Installer-ul cere, cu ecou ascuns, parola SQL a operatorului (doar dacă `sql.operatorAuthentication = "Sql"`) și, opțional, parola inițială a administratorului (gol = se generează). După verificarea cerințelor afișează rezumatul și cere confirmarea (`d` = da). Apoi rulează pașii I-04…I-24 (specificația 8.1), câte un rând pe pas.

   Fără interacțiune (de exemplu dintr-un script), parolele vin din variabile de mediu setate **doar în sesiunea curentă** (specificația 6.3). Forma de mai jos merge și în Windows PowerShell 5.1, și în PowerShell 7:

   ```powershell
   # doar la sql.operatorAuthentication = "Sql"
   $env:ERP_SQL_OPERATOR_PASSWORD = [Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'Parola SQL a operatorului')).Password
   .\ErpSetup.exe install --answers C:\Kit\answers-PROD.json --unattended
   Remove-Item Env:ERP_SQL_OPERATOR_PASSWORD
   ```

   Opțional, `ERP_ADMIN_INITIAL_PASSWORD` dă parola inițială a administratorului; fără ea, parola se generează.

6. **Datele administratorului.** La final se afișează **o singură dată** adresa, utilizatorul `administrator` și parola temporară. Predați-le administratorului ERP pe un canal sigur. Ele nu se scriu în jurnal. Excepție: cu `--unattended` și parolă generată, parola se scrie și în `C:\ProgramData\Erp\<Instanță>\install\initial-admin.txt` (doar Administrators și SYSTEM), șters automat după prima schimbare a parolei.
7. **După instalare:**
   - administratorul ERP se autentifică la `https://<hostName>/`, schimbă parola, activează 2FA și rulează asistentul de configurare (firme, utilizatori, SMTP, certificatul ANAF);
   - administratorul SQL retrage `sysadmin` operatorului;
   - IT-ul clientului organizează copierea backup-urilor din `backupRoot` **în afara serverului** (installer-ul nu o face);
   - păstrați kitul și fișierul de răspunsuri (fără parole) pentru `repair` și pentru actualizări.

**Dacă instalarea se oprește:** citiți mesajul și codul de ieșire (tabelul din secțiunea 9), corectați cauza și rulați **exact aceeași comandă**. Installer-ul reia instalarea de unde a rămas, cu aceeași bază (specificația 8.1). Instalarea nu are rollback automat; baza creată nu se șterge. Excepție: dacă oprirea a avut loc după crearea administratorului (I-13) și înainte de final, parola temporară se pierde; contul rămâne, iar parola se resetează cu `reset-admin` (T15).

**Jurnalul:** `C:\ProgramData\Erp\<Instanță>\logs\setup\setup-<dată>.log` (înainte de crearea instanței, `%TEMP%\ErpSetup\`); calea se afișează la final. Nu conține parole.

**Mai multe instanțe pe același server** (de exemplu `PROD` și `TEST`): fișiere de răspunsuri separate, cu `instance`, `hostName` și `databaseName` diferite.

---

## 7. Actualizarea (T14, de făcut)

Comportamentul specificat (specificația 10), pe care operatorul îl va urma după implementare:

1. Programați actualizarea **în afara programului de lucru**; installer-ul afișează durata estimată înainte de confirmare. Utilizatorii văd pagina „Aplicația se actualizează” pe durata ei.
2. Copiați pe server și dezarhivați kitul **versiunii noi** (secțiunea 4), într-un folder nou. Nu e nevoie de fișierul de răspunsuri: setările se citesc din `instance.json`.
3. Ca administrator, cu `sysadmin` temporar pe SQL:

   ```powershell
   .\ErpSetup.exe upgrade --instance PROD
   ```

4. Installer-ul verifică versiunile, pune aplicația în mentenanță, face un **backup de siguranță verificat**, apoi copiază binarele noi lângă cele vechi, rulează migrațiile, comută IIS și serviciul și verifică health-check-ul. **La orice eșec revine automat** la versiunea veche, inclusiv cu restaurarea bazei din backup-ul de siguranță (cod 70: instalarea a rămas funcțională pe versiunea veche).
5. Cod 71 (și revenirea a eșuat): aplicația rămâne în mentenanță; urmați procedura afișată pe ecran (specificația 10.6) și trimiteți-ne pachetul `diagnose`.
6. Dacă installer-ul a fost întrerupt (cădere de curent), `upgrade` refuză să continue (cod 73); rulați `ErpSetup.exe rollback --instance PROD`, apoi porniți din nou actualizarea.

Actualizarea nu creează și nu modifică utilizatori. Se păstrează ultimele 3 versiuni de binare și ultimele 3 backup-uri de actualizare.

---

## 8. Celelalte comenzi (T15, de făcut)

| Comandă | Când |
| --- | --- |
| `status --instance <I>` | Verificare rapidă: versiune, health-check, ultimul backup, expirarea licenței și a certificatului |
| `repair --instance <I>` | Ceva a fost stricat în IIS, serviciu, drepturi sau configurare; cere kitul **aceleiași** versiuni. `--license <fișier>` înlocuiește licența; `--rotate-secrets` schimbă parola aplicației |
| `reset-admin --instance <I> [--user <nume>]` | Niciun administrator nu se mai poate autentifica; cere administrator local + `sysadmin` |
| `diagnose --instance <I>` | Pachet ZIP pentru suport, fără secrete și fără date de business |
| `uninstall --instance <I>` | Elimină site-ul, serviciul și binarele; **păstrează** baza, backup-urile și joburile de backup (specificația 11.4) |

---

## 9. Coduri de ieșire frecvente

Lista completă: specificația 6.4.

| Cod | Ce faceți |
| --- | --- |
| 0 / 1 | Reușit (1 = cu avertismente; citiți-le) |
| 2 | Anulat; rulați din nou când sunteți gata |
| 3 | Comandă sau opțiune greșită; `ErpSetup.exe --help` |
| 10 | Rulați PowerShell ca administrator, pe Windows Server suportat |
| 11 | Fișierul de răspunsuri sau o parolă e invalidă; mesajul indică fiecare câmp |
| 12 | Cerințe neîndeplinite: rulați `check`, corectați „Blocant”; după instalarea rolurilor IIS poate fi necesară o repornire |
| 20 / 21 | Kit nesemnat, modificat sau incomplet: descărcați-l din nou și verificați amprenta |
| 30 | Eroare SQL; detaliile sunt în jurnal; după corectare rulați aceeași comandă |
| 31 | Baza există și are date: alegeți alt `sql.databaseName` sau cereți administratorului SQL să o elimine (installer-ul nu suprascrie niciodată o bază) |
| 32 | Baza existentă are alt collation decât `Latin1_General_100_CS_AS_SC_UTF8` |
| 40 | Licență invalidă, expirată sau pentru alt produs; cereți o licență nouă |
| 50 | Eroare IIS, serviciu sau fișiere; detaliile sunt în jurnal |
| 60 | Aplicația nu a pornit corect; installer-ul afișează ultimele rânduri din jurnalele Host și Worker |
| 70–73 | Actualizare: secțiunea 7 |

---

## 10. Ce nu se face

- Nu se modifică fișierele din kit și nu se folosește un kit de test la client.
- Nu se scriu parole în fișierul de răspunsuri, în linia de comandă sau în variabile de mediu la nivel de sistem.
- Nu se creează și nu se modifică manual baza, loginurile, joburile sau site-ul IIS; pentru refacere se folosește `repair`.
- Nu se lasă `sysadmin` operatorului după terminarea comenzii.
- Nu se șterge `C:\ProgramData\Erp\<Instanță>\` (starea instalării, configurarea criptată, cheile, licența).
