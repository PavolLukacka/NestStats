# NestStats firmware nodes

Tento priečinok obsahuje edge nody pre NestStats. Zdrojové súbory sú pripravené ako verejné šablóny, preto v nich nie sú žiadne reálne Wi-Fi heslá, Supabase kľúče ani sériové čísla systémov.

Pred nahratím do zariadenia nahraď hodnoty:

```cpp
"CHANGE_ME_WIFI_SSID"
"CHANGE_ME_WIFI_PASSWORD"
"https://CHANGE_ME.supabase.co"
"CHANGE_ME_SUPABASE_ANON_KEY"
"CHANGE_ME_SYSTEM_SN"
```

`CHANGE_ME_SYSTEM_SN` musí sedieť s hodnotou `sn_number` v tabuľke `SYSTEM` a v telemetrických tabuľkách Supabase.

## Pravidlá pre GitHub

- Do repozitára necommituj reálne Wi-Fi údaje.
- Do firmware nikdy nedávaj Supabase `service_role` key.
- Pre verejné šablóny používaj iba `anon` key a iba vtedy, keď má Supabase nastavené vhodné pravidlá.
- Build výstupy ako `build/`, `.pio/`, `sdkconfig` a `managed_components/` sú ignorované cez `.gitignore`.
- Ak si vytvoríš `secrets.h`, `wifi_credentials.h` alebo podobný súbor, ostane ignorovaný.

## Odporúčaný postup

1. Vyber firmware podľa úlohy nodu.
2. Doplň Wi-Fi, Supabase URL, Supabase anon key a `SN_NUMBER`.
3. Nahraj firmware do ESP32 alebo podporovanej dosky.
4. Otvor sériový monitor a skontroluj Wi-Fi, Modbus a HTTP status.
5. V Supabase over, že pribúdajú riadky pre rovnaké `sn_number`.
6. Až potom povoľ výkonovú reguláciu SSR alebo batériového meniča.
