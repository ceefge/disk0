# Teams-App-Paket

Diese drei Dateien bilden das in Teams hochladbare App-Paket:

- `manifest.json` – App-Definition (Bot, Scopes, Metadaten)
- `color.png` – 192×192 Farb-Icon
- `outline.png` – 32×32 transparentes Umriss-Icon

## Platzhalter ersetzen

Vor dem Packen in `manifest.json` ersetzen:

| Platzhalter                       | Wert                                        |
|-----------------------------------|---------------------------------------------|
| `REPLACE_WITH_BOT_APP_ID`         | Microsoft App ID der Azure-Bot-Registrierung (2×) |
| `REPLACE_WITH_YOUR_SERVER_DOMAIN` | Domain des Servers, z. B. `polls.consiness.de` |

## Paket bauen

```bash
zip -j teamspoll-app.zip manifest.json color.png outline.png
```

`-j` ist wichtig: die Dateien müssen **ohne Unterordner** im ZIP liegen.

## Icons neu erzeugen

```bash
python3 make_icons.py
```

Erzeugt `color.png` und `outline.png` neu (nur Python-Standardbibliothek nötig).
Ersetze sie gern durch eigene Grafiken im Corporate Design – Maße beibehalten.
