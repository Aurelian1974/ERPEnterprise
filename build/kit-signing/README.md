# Cheia de semnare a kitului

Cheia **privată** de semnare a kitului (ECDSA P-256) **nu stă în Git** (`SEC-02`, INSTALARE_ACTUALIZARE.md 4.3). Este diferită de cheia de semnare a licențelor și de certificatul Authenticode.

| Fișier | Unde stă | Folosit de |
| --- | --- | --- |
| `kit-private.pem` (PKCS#8, de preferat criptat) | În afara depozitului, pe mașina de release, cu acces restrâns | `package-kit.ps1 -SigningKeyPath` |
| `kit-public.pem` (SubjectPublicKeyInfo) | Lângă cheia privată; poate fi publică | `package-kit.ps1 -PublicKeyPath`, încorporată în `ErpSetup.exe` (`-p:KitPublicKeyFile`) |

Dacă cheia privată e criptată (`ENCRYPTED PRIVATE KEY`), parola se dă doar în sesiunea curentă:

```powershell
$env:ERP_KIT_SIGNING_KEY_PASSWORD = Read-Host -MaskInput 'Parola cheii kitului'
```

Generarea perechii (o singură dată, pe mașina de release):

```powershell
$key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$password = Read-Host -MaskInput 'Parola cheii kitului'
$pbe = [System.Security.Cryptography.PbeParameters]::new('Aes256Cbc', 'SHA256', 600000)
[IO.File]::WriteAllText('K:\kit-signing\kit-private.pem', $key.ExportEncryptedPkcs8PrivateKeyPem($password, $pbe))
[IO.File]::WriteAllText('K:\kit-signing\kit-public.pem', $key.ExportSubjectPublicKeyInfoPem())
```

Semnătura din `manifest.sig` este Base64 al semnăturii IEEE P1363 (`r||s`, 64 de octeți) peste octeții exacți ai `manifest.json`.

**Kit de test.** `package-kit.ps1 -TestKit` generează o pereche efemeră în `artifacts/kit-signing-test/` (ignorat de Git), construiește în `Debug` și nu semnează Authenticode. Un kit de test nu se livrează.
