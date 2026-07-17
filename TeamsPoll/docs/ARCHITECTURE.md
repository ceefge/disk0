# Architektur

## Überblick

```
                         ┌──────────────────────────────────────┐
   Microsoft Teams       │        TeamsPoll.Server (.NET 8)      │
   (Client / Dienst)     │                                      │
        │                │  POST /api/messages                  │
        │  Activity      │        │                             │
        ├───────────────►│   CloudAdapter ──► PollBot           │
        │  (Nachricht /  │        │              │              │
        │   Card-Action) │        │              ├─► PollCardFactory (Adaptive Cards)
        │                │        │              │              │
        │◄───────────────┤   Antwort/Card       └─► PollRepository ──► SQLite (polls.db)
        │  (Card-Update) │                                      │
                         └──────────────────────────────────────┘
```

Der Bot ist **zustandslos pro Turn**. Der gesamte Kontext einer Aktion steckt in
den `data`-Feldern der Adaptive Card (`pollId`, `optionId`) und in der Datenbank.
Dadurch braucht es keinen In-Memory-Sitzungsspeicher, und mehrere Instanzen
können sich – sobald sie dieselbe DB nutzen – eine Last teilen.

## Warum ein Bot mit Adaptive Cards?

Für das „WhatsApp-Gefühl" (inline abstimmen, Ergebnis wächst live) ist in Teams
das **Universal Action Model** der richtige Baustein:

- Optionen sind `Action.Execute`-Aktionen mit einem `verb` (`vote`).
- Beim Tippen sendet Teams eine `adaptiveCard/action`-Invoke-Activity an den Bot.
- Der Bot verbucht die Stimme und gibt **dieselbe Karte neu gerendert** zurück –
  Teams ersetzt die Karte in der Nachricht.
- Das `refresh`-Feld mit den `userIds` der Teilnehmer sorgt dafür, dass Teams die
  Karte bei den anderen automatisch nachlädt, sobald sie sie ansehen.

Alternative Ansätze (und warum nicht):

| Ansatz                     | Bewertung |
|----------------------------|-----------|
| Tab-App (eigene Web-UI)    | Mehr Freiheit, aber Nutzer müssen einen Tab öffnen – kein Inline-Feeling. |
| Message Extension          | Gut zum *Erstellen*, ersetzt aber nicht das Live-Voting in der Nachricht. |
| **Bot + Universal Actions**| Inline, live, minimaler Klickweg → gewählt. |

## Komponenten

### `Program.cs`
Verkabelt Bot Framework (`CloudAdapter`, `ConfigurationBotFrameworkAuthentication`),
registriert `PollRepository` (Singleton) und `PollBot` (transient) und stellt die
Endpunkte `/api/messages` und `/healthz` bereit. Beim Start wird das DB-Schema
angelegt.

### `Bot/PollBot.cs`
`TeamsActivityHandler` mit zwei Einstiegspunkten:

- `OnMessageActivityAsync` – Schnellbefehl parsen und Umfrage als neue Nachricht
  posten, oder Hilfe zeigen.
- `OnAdaptiveCardInvokeAsync` – Dispatch nach `verb`:
  `createform` → Formular, `create` → Umfrage anlegen, `vote` → Stimme togglen,
  `refresh` → neu rendern, `close` → schließen (nur Ersteller:in).

### `Cards/PollCardFactory.cs`
Baut die Karten als `JObject` (Newtonsoft), weil Teams-spezifische Felder
(`Action.Execute`, `refresh`, `msteams.width`) im typisierten AdaptiveCards-Modell
fehlen. Der Fortschrittsbalken wird als Monospace-Blockgrafik (`█`/`░`) gerendert –
robust über alle Teams-Clients hinweg, ohne clientabhängige Layout-Tricks.

### `Data/PollRepository.cs`
Kapselt **alle** SQL-Zugriffe. Kernstück ist `ToggleVoteAsync`:

- **Einfachauswahl:** erneutes Tippen der aktuellen Option nimmt die Stimme
  zurück; Tippen einer anderen Option verschiebt die Stimme.
- **Mehrfachauswahl:** jede Option wird unabhängig getoggelt.

Der Primärschlüssel `(OptionId, UserId)` verhindert Doppelstimmen auf DB-Ebene.

## Datenmodell

```
Polls(Id PK, Question, CreatedByName, CreatedById, ConversationId,
      ActivityId, AllowMultiple, Anonymous, Closed, CreatedAt)
Options(Id PK, PollId FK→Polls, Text, OrderIndex)
Votes(PollId FK, OptionId FK, UserId, UserName, VotedAt,
      PRIMARY KEY(OptionId, UserId))
```

`UserId` ist die stabile Teams-/AAD-Kennung aus `activity.from.id` und dient auch
der Rechteprüfung beim Schließen.

## Skalierung & Weiterentwicklung

- **Mehr Last / mehrere Instanzen:** SQLite gegen PostgreSQL tauschen. Nur
  `PollRepository` (Verbindungsaufbau + SQL-Dialekt) ist betroffen; der restliche
  Code bleibt unverändert.
- **Proaktive Updates:** Über die gespeicherte `ActivityId` lässt sich die Karte
  per `UpdateActivityAsync` auch ohne Nutzerinteraktion aktualisieren (z. B. bei
  Ablauf einer Frist).
- **Fristen/Erinnerungen:** `CreatedAt` + optionales `ClosesAt` plus ein
  Hintergrund-Timer, der Umfragen automatisch schließt.
- **Export:** Ergebnisse als CSV/Excel über einen zusätzlichen HTTP-Endpunkt.

## Sicherheit

- Eingehende Requests an `/api/messages` werden von Bot Framework über die
  konfigurierten App-Credentials authentifiziert (JWT der Bot-Connector-Dienste).
- Alle SQL-Zugriffe sind parametrisiert (keine String-Konkatenation).
- Nur die Ersteller:in kann eine Umfrage schließen (Prüfung in `ClosePollAsync`).
