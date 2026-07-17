# Deployment

Diese Anleitung bringt den Bot vom lokalen Test bis auf den eigenen Server.

## 1. Voraussetzungen

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Ein Azure-Konto (für die kostenlose **Azure Bot**-Registrierung – die
  Registrierung ist kostenlos, gehostet wird auf **eurem** Server)
- Ein öffentlich per HTTPS erreichbarer Endpunkt für den Server
- Microsoft Teams mit Recht, eigene Apps hochzuladen (Custom App Upload /
  „Apps hochladen" muss im Teams-Admin-Center erlaubt sein)

## 2. Azure-Bot-Registrierung

1. Im [Azure-Portal](https://portal.azure.com) eine **Azure Bot**-Ressource
   anlegen (Typ „Multi Tenant" für den einfachsten Start).
2. Unter **Configuration** eine **Microsoft App ID** erzeugen und im zugehörigen
   App-Registrierungs-Blade ein **Client Secret** anlegen. Beides notieren.
3. **Messaging endpoint** setzen auf: `https://<eure-domain>/api/messages`
4. Unter **Channels** den **Microsoft Teams**-Kanal hinzufügen.

## 3. Konfiguration

Werte aus Schritt 2 setzen – per `appsettings.json`, Umgebungsvariablen oder
User Secrets. **Secrets nicht** ins Repo committen; Umgebungsvariablen sind auf
dem Server vorzuziehen:

```bash
export MicrosoftAppType=MultiTenant
export MicrosoftAppId=<eure-app-id>
export MicrosoftAppPassword=<euer-client-secret>
# MicrosoftAppTenantId nur bei SingleTenant nötig
export ConnectionStrings__Sqlite="Data Source=/var/lib/teamspoll/polls.db"
```

Beim ersten Start wird `polls.db` automatisch angelegt.

## 4. Lokaler Test (ohne Teams)

```bash
cd src/TeamsPoll.Server
dotnet run
```

Mit dem [Bot Framework Emulator](https://github.com/microsoft/BotFramework-Emulator)
`http://localhost:3978/api/messages` öffnen (App-ID/Passwort leer) und eine
Umfrage schreiben: `Mittagessen? | Pizza | Salat | Sushi`.

## 5. Auf dem eigenen Server hosten

### Variante A – direkt als Dienst

Self-contained veröffentlichen und als systemd-Service/Windows-Dienst betreiben:

```bash
cd src/TeamsPoll.Server
dotnet publish -c Release -o /opt/teamspoll
# Start:
ASPNETCORE_URLS="http://0.0.0.0:3978" /opt/teamspoll/TeamsPoll.Server
```

Davor einen Reverse Proxy (nginx/IIS/Caddy) mit **HTTPS** setzen, der auf
`http://localhost:3978` weiterleitet. Teams akzeptiert nur HTTPS-Endpunkte mit
gültigem Zertifikat.

### Variante B – Docker

`Dockerfile` (Beispiel) im Server-Verzeichnis:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 3978
ENV ASPNETCORE_URLS=http://0.0.0.0:3978

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/TeamsPoll.Server/TeamsPoll.Server.csproj -c Release -o /app/out

FROM base AS final
COPY --from=build /app/out .
# polls.db in ein Volume legen, damit Umfragen Neustarts überleben:
VOLUME ["/data"]
ENV ConnectionStrings__Sqlite="Data Source=/data/polls.db"
ENTRYPOINT ["dotnet", "TeamsPoll.Server.dll"]
```

```bash
docker build -t teamspoll .
docker run -d -p 3978:3978 -v teamspoll-data:/data \
  -e MicrosoftAppId=<...> -e MicrosoftAppPassword=<...> teamspoll
```

### Test/Übergang – Tunnel

Für schnelle Tests ohne feste Domain einen Tunnel nutzen
([dev tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/) oder
ngrok) und dessen HTTPS-URL als Messaging-Endpoint eintragen.

## 6. Teams-App-Paket bauen

1. Icons erzeugen (einmalig, liegen schon bei; neu bauen mit
   `python3 manifest/make_icons.py`).
2. In `manifest/manifest.json` ersetzen:
   - `REPLACE_WITH_BOT_APP_ID` → eure Microsoft App ID (an **beiden** Stellen)
   - `REPLACE_WITH_YOUR_SERVER_DOMAIN` → eure Server-Domain (ohne `https://`)
3. Die drei Dateien **flach** (ohne Unterordner) zippen:

```bash
cd manifest
zip -j ../teamspoll-app.zip manifest.json color.png outline.png
```

## 7. In Teams installieren

1. Teams → **Apps** → **Verwalten Sie Ihre Apps** → **App hochladen** →
   **Eine benutzerdefinierte App hochladen**.
2. `teamspoll-app.zip` wählen.
3. Bot zu einem Team/Chat hinzufügen. Beim Hinzufügen erscheint automatisch die
   Hilfekarte. Danach: `Frage? | A | B` schreiben – fertig.

## 8. Betrieb

- **Health-Check:** `GET https://<domain>/healthz` → `{"status":"ok"}`.
- **Backup:** regelmäßig die `polls.db` (bzw. das Volume) sichern.
- **Logs:** Standard-.NET-Logging (Konsole/`journald`). Fehler pro Turn werden von
  `AdapterWithErrorHandler` abgefangen, der Bot bleibt online.
- **Update auf PostgreSQL:** bei steigender Last – nur `PollRepository` betroffen.
