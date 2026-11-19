# Cheile publice ale licențelor

Fiecare fișier `<keyId>.pem` (SubjectPublicKeyInfo, ECDSA P-256) este încorporat în `Erp.Licensing` și verifică licențele cu acel `keyId` (versiunea anterioară a specificației). **Se înlocuiește în T1** cu `LicenseKeys.cs` (cheile publice 1 și 2 și cheile AES mascate) și cu formatul binar criptat din INSTALARE_ACTUALIZARE.md, secțiunile 3.4–3.5. Cheile **private** stau numai la instrumentul intern de emitere a licențelor, nu în Git (`SEC-02`), și sunt diferite de cheia kitului.

Folderul este gol până se adaugă cheia de producție (de exemplu `lic-2026-01.pem`); fără ea, orice licență este refuzată cu „cheie necunoscută”.
