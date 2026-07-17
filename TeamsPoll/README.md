# Team-Umfragen – Polly-Ersatz für Microsoft Teams

Ein selbst gehosteter Bot, mit dem sich in Teams **so einfach wie bei WhatsApp**
abstimmen lässt: Frage stellen, alle tippen auf ihre Antwort, das Ergebnis
aktualisiert sich live für alle. Eigenentwicklung als Ablösung von **Polly**.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4) ![Bot Framework](https://img.shields.io/badge/Bot%20Framework-4.22-0078D4) ![Storage](https://img.shields.io/badge/storage-SQLite-003B57)

## Wie es sich anfühlt

**Schnell per Nachricht** – den Bot in einem Kanal/Chat anschreiben:

```
Wo essen wir Freitag? | Italiener | Asiate | Kantine
```

→ Der Bot postet eine Umfragekarte. Jeder tippt auf eine Option, ein Balken
zeigt live die Verteilung, darunter (optional) wer abgestimmt hat.

**Oder per Formular** – Button „➕ Neue Umfrage" öffnet ein Eingabeformular mit
Optionen für Mehrfach- und anonyme Abstimmung.

Zusätze im Schnellbefehl:

| Zusatz     | Wirkung                     |
|------------|-----------------------------|
| `--multi`  | Mehrfachauswahl erlauben    |
| `--anon`   | Anonyme Abstimmung          |

Beispiel: `Team-Event – welche Termine passen? | Mo | Di | Mi --multi`

## Funktionsumfang (MVP)

- ✅ Umfrage per Nachricht **oder** interaktivem Formular erstellen
- ✅ Abstimmen mit einem Tippen, erneutes Tippen nimmt die Stimme zurück
- ✅ Einfach- und Mehrfachauswahl
- ✅ Anonyme oder namentliche Abstimmung (WhatsApp-Stil: zeigt, wer wählte)
- ✅ Live-Ergebnisse mit Fortschrittsbalken und Prozentwerten
- ✅ Automatische Aktualisierung für Teilnehmer (`refresh`) + Button „Aktualisieren"
- ✅ Umfrage schließen (nur durch Ersteller:in)
- ✅ Persistenz in SQLite (übersteht Neustarts)
- ✅ Selbst hostbar auf einem eigenen Windows-/Linux-Server oder in Docker

## Aufbau

```
TeamsPoll/
├─ src/TeamsPoll.Server/        ASP.NET Core Bot-Server (.NET 8)
│  ├─ Program.cs                DI, Bot-Adapter, /api/messages, /healthz
│  ├─ Bot/PollBot.cs            Nachrichten + Card-Actions (vote/refresh/close/create)
│  ├─ Bot/AdapterWithErrorHandler.cs
│  ├─ Cards/PollCardFactory.cs  Adaptive Cards (Umfrage, Formular, Hilfe)
│  ├─ Data/PollRepository.cs    SQLite-Zugriff, Vote-Toggle-Logik
│  └─ Models/                   Poll, PollOption, Vote/Results
├─ manifest/                    Teams-App-Paket (manifest.json + Icons)
└─ docs/                        Architektur & Deployment
```

## Schnellstart (lokal mit Bot Framework Emulator)

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
cd src/TeamsPoll.Server
dotnet restore
dotnet run
```

Server läuft auf `http://localhost:3978`. Im
[Bot Framework Emulator](https://github.com/microsoft/BotFramework-Emulator)
`http://localhost:3978/api/messages` öffnen (App-ID/Passwort leer lassen) und
z. B. `Kaffee? | Ja | Nein` schreiben.

## In Teams bringen

Kurzfassung (Details in [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)):

1. **Azure Bot** registrieren → `MicrosoftAppId` + Passwort erzeugen.
2. Werte in `appsettings.json` (oder als Umgebungsvariablen / User Secrets) setzen.
3. Server öffentlich erreichbar machen (eigener Server + HTTPS, oder Tunnel wie
   `dev tunnels`/ngrok für Tests). Messaging-Endpoint der Bot-Registrierung auf
   `https://<domain>/api/messages` setzen.
4. In `manifest/manifest.json` die Platzhalter ersetzen, `manifest.json` +
   `color.png` + `outline.png` als ZIP packen und in Teams
   („Apps → Verwalten → Hochladen") installieren.

## Technische Eckpunkte

- **Universal Action Model** (`Action.Execute` + `refresh`): Die Karte wird beim
  Abstimmen für alle aktualisiert – das erzeugt das „WhatsApp-Gefühl".
- **SQLite** als Standard – null Setup, dateibasiert. Für mehr Last ist der
  Wechsel auf PostgreSQL im `PollRepository` gekapselt (siehe Architektur-Doku).
- **Zustandslos pro Turn**: Der Bot hält keinen Sitzungsspeicher; alles steckt in
  der Karte (`pollId`/`optionId`) und der Datenbank → beliebig skalierbar hinter
  einem Load Balancer, sobald die DB geteilt wird.

## Lizenz

(c) consiness – interne Eigenentwicklung.
