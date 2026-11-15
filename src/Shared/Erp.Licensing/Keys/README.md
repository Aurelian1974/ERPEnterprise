# Cheile publice ale licențelor

Fiecare fișier `<keyId>.pem` (SubjectPublicKeyInfo, ECDSA P-256) este încorporat în `Erp.Licensing` și verifică licențele cu acel `keyId` (INSTALARE_ACTUALIZARE.md, 8.9). Cheile **private** stau numai la instrumentul intern de emitere a licențelor, nu în Git (`SEC-02`), și sunt diferite de cheia kitului.

Folderul este gol până se adaugă cheia de producție (de exemplu `lic-2026-01.pem`); fără ea, orice licență este refuzată cu „cheie necunoscută”.
