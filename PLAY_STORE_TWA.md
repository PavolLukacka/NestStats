# Google Play cez Bubblewrap

NestStats je možné zabaliť ako Android aplikáciu cez **Trusted Web Activity**. Android aplikácia potom slúži ako tenký shell okolo verejnej HTTPS verzie webu.

## Predpoklady

- aplikácia musí byť verejne dostupná cez HTTPS, napríklad Render doména,
- web musí mať `manifest.webmanifest`,
- web musí mať ikony 192x192 a 512x512,
- Google Play vyžaduje aktuálny target API level. Podľa aktuálnych Google Play pravidiel musia nové appky a update appky cieliť minimálne Android 15, teda API level 35,
- musíš mať Google Play Developer účet.

## Súbory pripravené v projekte

| Súbor | Úloha |
|---|---|
| `wwwroot/manifest.webmanifest` | PWA manifest pre Bubblewrap |
| `wwwroot/service-worker.js` | základný service worker pre statické súbory |
| `wwwroot/icons/icon-192.png` | Android/PWA ikona |
| `wwwroot/icons/icon-512.png` | Android/PWA ikona |

Manifest je dostupný na:

```text
https://TVOJA-DOMENA/manifest.webmanifest
```

## Inštalácia Bubblewrap

Na vývojovom počítači potrebuješ Node.js, JDK a Android SDK. Potom:

```powershell
npm install -g @bubblewrap/cli
bubblewrap doctor
```

`bubblewrap doctor` ťa prevedie chýbajúcimi Android nástrojmi.

## Inicializácia Android projektu

Použi verejnú URL manifestu. Príklad:

```powershell
bubblewrap init --manifest=https://neststats.onrender.com/manifest.webmanifest
```

Odporúčané hodnoty:

```text
Application name: NestStats
Short name: NestStats
Package ID: sk.neststats.app
Start URL: /
Display mode: standalone
Orientation: portrait-primary
Theme color: #1e9e6e
Background color: #f9f8f6
```

Package ID musí byť stabilné. Po publikovaní na Google Play ho už nebudeš normálne meniť.

## Digital Asset Links

Trusted Web Activity musí preukázať, že Android aplikácia patrí k webovej doméne. Po vytvorení Android projektu získaš SHA-256 fingerprint podpisového kľúča.

Bubblewrap vie fingerprint vypísať napríklad cez:

```powershell
bubblewrap fingerprint
```

Potom vytvor súbor:

```text
wwwroot/.well-known/assetlinks.json
```

Obsah bude vyzerať približne takto:

```json
[
  {
    "relation": ["delegate_permission/common.handle_all_urls"],
    "target": {
      "namespace": "android_app",
      "package_name": "sk.neststats.app",
      "sha256_cert_fingerprints": [
        "AA:BB:CC:DD:..."
      ]
    }
  }
]
```

Po nasadení musí byť dostupný na:

```text
https://TVOJA-DOMENA/.well-known/assetlinks.json
```

## Build pre Google Play

Pre Google Play potrebuješ Android App Bundle:

```powershell
bubblewrap build
```

Výstupom bude `.aab`, ktorý nahráš do Google Play Console.

## Čo ešte treba pred odoslaním

- doplniť Google Play listing texty,
- pripraviť screenshots pre mobil,
- nastaviť privacy policy URL,
- skontrolovať, či login cez Google/Facebook funguje aj v TWA,
- v Google OAuth Console pridať redirect URI pre produkčnú doménu,
- rozhodnúť, či bude aplikácia verejná, interná alebo closed testing.

## Poznámka k prihlasovaniu

TWA otvorí web pod tvojou HTTPS doménou. OAuth redirect URI teda ostáva webové:

```text
https://TVOJA-DOMENA/signin-google
```

Ak Google login funguje v prehliadači na produkčnej doméne, mal by fungovať aj v TWA.
