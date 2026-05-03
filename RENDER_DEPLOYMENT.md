# Render Docker deployment

Tento projekt je pripravený na deploy cez Render ako Docker web service.

## Čo je dôležité

- `Dockerfile` buildne ASP.NET Core aplikáciu a spustí `NestStats2.dll`.
- Kontajner počúva na `0.0.0.0:${PORT:-10000}`, čo sedí s Render web service pravidlami.
- `.dockerignore` vynecháva lokálne secrets, build výstupy, databázy, diplomovku, RYSTATS a firmware build priečinky.
- Reálne kľúče sa nenahrávajú do image. Nastavujú sa až v Render Dashboard cez Environment Variables.

## Render nastavenie

1. Pushni repozitár na GitHub.
2. V Render vytvor nový **Web Service**.
3. Vyber GitHub repo `NestStats`.
4. Ako runtime vyber **Docker**.
5. Dockerfile path nechaj:

```text
./Dockerfile
```

6. Do Environment Variables nastav minimálne:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Data Source=/app/App_Data/neststats-auth.db
Supabase__Url=https://YOUR_PROJECT.supabase.co
Supabase__AnonKey=YOUR_SUPABASE_ANON_KEY
Authentication__RequireConfirmedAccount=false
```

Voliteľné OAuth a email premenné:

```text
Authentication__Google__ClientId=...
Authentication__Google__ClientSecret=...
Authentication__Facebook__ClientId=...
Authentication__Facebook__ClientSecret=...
Email__FromAddress=...
Email__SmtpHost=...
Email__SmtpUser=...
Email__SmtpPassword=...
AdminBootstrap__Email=...
AdminBootstrap__Password=...
AdminBootstrap__DisplayName=NestStats Admin
```

## SQLite upozornenie

NestStats používa lokálnu SQLite databázu pre ASP.NET Identity. V Render kontajneri je filesystem bez persistentného disku dočasný. Ak chceš, aby účty ostali zachované po redeploy/reštarte, pridaj v Render službe **persistent disk** namountovaný na:

```text
/app/App_Data
```

Ak disk nepoužiješ, aplikácia sa spustí, ale používateľské účty a lokálne Identity dáta sa môžu po redeploy stratiť.

## Bezpečnostná poznámka

Nikdy nevkladaj `appsettings.Local.json` do Docker image ani do GitHubu. Pre Render používaj Environment Variables. `.dockerignore` už tento súbor blokuje.
