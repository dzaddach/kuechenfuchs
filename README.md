# Küchenfuchs

**Clever kochen. Günstiger einkaufen.**

Küchenfuchs ist eine geplante selbst gehostete Progressive Web App zur Rezeptverwaltung, Essensplanung und Einkaufsoptimierung anhand aktueller Supermarktangebote.

## Status

**P0 – lokale Compose-Basis:** Abgeschlossen und nach `main` gemergt.

**P1 – iPhone-first UI-Grundlage:** Abgeschlossen und nach `main` gemergt. Rezepte und Editor waren zunächst Beispieldaten im nicht-persistenten Demo-Modus.

**P2 – Anmeldung und Haushaltszugriff:** Abgeschlossen und nach `main` gemergt.

**P3 – Manuelle Rezeptverwaltung:** Implementierung auf `feature/p3-recipes`; noch nicht gemergt. Rezepte werden haushaltsbezogen in PostgreSQL gespeichert und können gesucht, bearbeitet und archiviert werden.

## Zielbild

- Rezepte verwalten, importieren und mit KI erstellen
- Wochenplan und synchronisierte Einkaufsliste
- Angebote ausgewählter Lebensmittelmärkte erfassen und mit Rezeptzutaten abgleichen
- Einkaufsempfehlungen unter Berücksichtigung von Preisen, Packungsgrößen und Einkaufsstopps
- Auf eigenem Linux-Server per Docker Compose betreiben
- Integration mit ChatGPT via abgesicherter API und KI-Assistent in der PWA

## Vorgehen

Die Architektur und der Implementierungsplan wurden geprüft. Arbeitspakete benötigen jeweils eine ausdrückliche Freigabe.

## Lokale Entwicklung (P0–P3)

Voraussetzungen: Docker mit Docker Compose v2.29 oder neuer. Die Entwicklung läuft über Compose; PostgreSQL erhält keinen Host-Port. Caddy ist lokal standardmäßig unter `http://localhost:8080` erreichbar und benötigt weder öffentliche Domain noch öffentliches TLS-Zertifikat. Ist Port 8080 belegt, `HTTP_PORT` in `.env` ändern.

```sh
cp .env.example .env
# .env lokal befüllen: POSTGRES_PASSWORD setzen.
# Für den erstmaligen Administrator zusätzlich INITIAL_ADMIN_EMAIL,
# INITIAL_ADMIN_PASSWORD und INITIAL_HOUSEHOLD_NAME setzen.
# INITIAL_ADMIN_PASSWORD muss mindestens 14 Zeichen haben und Groß-/Kleinbuchstaben,
# Zahl sowie Sonderzeichen enthalten. Beispielwert erzeugen:
openssl rand -base64 32
docker compose up --build --detach
docker compose --profile bootstrap run --rm bootstrap
```

Die API führt beim Stackstart die EF-Core-Migrationen aus. Der einmalige Bootstrap-Befehl legt Administrator und Haushalt in einem separaten, nach erfolgreichem Lauf entfernten Container an. Danach die Werte von `INITIAL_ADMIN_EMAIL`, `INITIAL_ADMIN_PASSWORD` und `INITIAL_HOUSEHOLD_NAME` aus `.env` löschen und die API ohne diese Konfiguration neu erstellen:

```sh
# .env bearbeiten: Bootstrap-Werte leeren oder entfernen, speichern.
docker compose up --detach --force-recreate api
```

Der API-Dienst enthält diese Bootstrap-Variablen grundsätzlich nicht. Das Datenbankvolume behält Benutzer und Haushalt; erneutes Starten der API ohne Bootstrap-Werte löscht oder ändert sie nicht. Das Bootstrap-Profil lässt sich ohne konfigurierte Zugangsdaten nicht erfolgreich ausführen, wenn die Datenbank noch leer ist. `.env` ist lokal und darf nicht committet werden. Den vollständigen sicheren Ablauf und seine Tests beschreibt [docs/deployment.md](docs/deployment.md).

Die Beispielwerte sind ausdrücklich ausschließlich für lokale Entwicklung gedacht und nicht als produktive Secrets geeignet. Compose verlangt `POSTGRES_PASSWORD`; für Produktion muss ein eigenes sicheres Secret gesetzt werden. Für Start, Stopp, Neustart, Migrationen und Backups siehe [docs/deployment.md](docs/deployment.md).

Die P0-Smoke-Tests laufen mit `./scripts/test-p0.sh`, P2-Authentifizierungstests mit `./scripts/test-p2-auth.sh` und P3-Rezepttests mit `bash scripts/test-p3-recipes.sh`. Die Skripte bauen und starten einen isolierten Test-Stack und entfernen ihn danach wieder.

## Anmeldung und Rezeptverwaltung lokal prüfen

Die empfohlene Ansicht läuft im vollständigen Stack hinter Caddy. Vor dem erstmaligen Start das oben beschriebene Bootstrap-Verfahren ausführen; danach sind die Bootstrap-Werte aus `.env` entfernt.

```sh
docker compose up --build --detach
# Oberfläche: http://localhost:8080
docker compose stop
# Später fortsetzen:
docker compose start
# Änderungen neu bauen:
docker compose up --build --detach
```

Für reine Angular-Entwicklung ohne API oder Service Worker:

```sh
cd frontend
npm ci
npm start
# http://localhost:4200
```

Frontend-Build und UI-Komponententests:

```sh
cd frontend
npm run build
npm test
```

Backend-Tests und P3-Ende-zu-Ende-Test gegen eine frische Compose-Datenbank:

```sh
dotnet test backend/Kuechenfuchs.Api.Tests/Kuechenfuchs.Api.Tests.csproj --configuration Release
bash scripts/test-p3-recipes.sh
```

Der P3-Test prüft Migrationen auf einer frischen Datenbank, Anmeldung, CSRF-Negativfälle für Rezept- und Favoritenschreibzugriffe, Favoritenpersistenz nach API-Neustart, Haushaltsisolation, Suche, Mengen-/Einheitenvalidierung, ETag-Konflikte, Archivierung und Rezeptpersistenz. Testdaten und Bootstrap-Zugangsdaten sind temporär und werden durch das Skript bereinigt.

### P3-Prüfergebnis (11. Oktober 2026)

| Prüfung | Ergebnis |
|---|---|
| API Release-Build | Bestanden, 0 Warnungen / 0 Fehler |
| Backend-Unit-Tests | Bestanden, 5 Tests |
| Angular-Tests | Bestanden, 13 Tests |
| Angular-Produktionsbuild | Bestanden, ohne Budgetwarnungen |
| P3 Compose-Integration | Bestanden: frische Migration, CRUD, Suche, CSRF, Haushaltstrennung, Favoriten-Persistenz, parallele Updates und Neustart-Persistenz |
| P0-Smoke-Tests | Bestanden; wegen belegtem Standardport mit `HTTP_PORT=54394` |
| P2-Authentifizierungsregression | Bestanden: Bootstrap, CSRF, Cookies, Haushaltstrennung, Logout und Ablauf |

### Manuelle P3-Prüfanleitung

1. Den Compose-Stack wie oben beschrieben starten und mit dem eingerichteten Benutzer anmelden.
2. Unter **Meine Rezepte → Neues Rezept** ein Rezept mit 2 Basisportionen anlegen. Für eine Zutat `250 g` eintragen, eine zweite Zutat ohne Menge lassen und mindestens einen Zubereitungsschritt hinzufügen.
3. Speichern, das Rezept in der Liste öffnen und kontrollieren, dass Zutaten, Schritte, Kategorie und Tags wieder geladen werden.
4. Bei „Mengen anzeigen für“ `4` Portionen wählen: Die Anzeige für `250 g` muss `500 g` zeigen. Die gespeicherte Grundmenge bleibt `250 g`.
5. Das Herzsymbol auf der Rezeptkarte aktivieren, die Seite neu laden und prüfen, dass der Favorit erhalten bleibt und der Favoritenfilter das Rezept findet.
6. Das Rezept ändern, erneut speichern und die Änderung nach einem API-/Stack-Neustart prüfen. Anschließend archivieren; es darf standardmäßig nicht mehr in der Rezeptliste erscheinen.

Die Rezeptliste, separate Rezeptdetailansicht und der Editor arbeiten mit der authentifizierten API. Änderungen werden im aktiven Haushalt gespeichert; Zutatenmengen dürfen unbekannt sein und lassen sich in der Detailansicht für eine andere Portionszahl skalieren. Gespeicherte Grundmengen bleiben unverändert. Favoriten werden dauerhaft haushaltsweit gespeichert und nach einem erneuten Laden wieder angezeigt. Das Archivieren ist reversibel; die Standardliste blendet archivierte Rezepte aus. Der Login verwendet serverseitige ASP.NET-Core-Identity-Cookies mit CSRF-Schutz für alle schreibenden Cookie-authentifizierten API-Aufrufe. Die Service-Worker-Registrierung ist im Development-Server deaktiviert. Der Produktionsbuild über Compose liefert Manifest, Icons und einen Service Worker für App-Shell und statische Dateien aus, jedoch keine Offline-Datensynchronisation. Auf iPhone/iPad wird die Installation in Safari über „Zum Home-Bildschirm“ angeboten; Installation auf einem echten Gerät setzt einen sicheren HTTPS-Ursprung voraus. Das lokale Compose-Setup nutzt HTTP und eignet sich daher nicht als iOS-Installationsziel.

Responsive Kontrolle: Browser-Viewport auf **390 × 844 px** (iPhone-Größe) und auf mindestens **1280 × 800 px** (Desktop) stellen. Auf Mobilgeräten die Navigation unten, Safe-Area-Abstände, Rezeptkarten und den Editor prüfen. Unter Desktop sollte die Seitenleiste angezeigt werden. Es werden keine externen kostenpflichtigen APIs oder Remote-Foodbilder geladen.

### P1 UI-Screenshots

Die folgenden Screenshots zeigen den responsiven Rezeptbereich und den Demo-Editor:

![Rezepte auf iPhone-Breite](docs/screenshots/p1-recipes-iphone.png)

![Rezepteditor auf iPhone-Breite](docs/screenshots/p1-editor-iphone.png)

![Rezeptübersicht auf dem Desktop](docs/screenshots/p1-recipes-desktop.png)

**Auftrag und Regeln:** [CLAUDE.md](CLAUDE.md)  
**Produktanforderungen:** [docs/PROJECT_BRIEF.md](docs/PROJECT_BRIEF.md)

## Hinweis

Externe Angebotsdatenquellen müssen vor der produktiven Nutzung hinsichtlich Zuverlässigkeit, Nutzungsrechten und Regionalität geprüft werden. Es dürfen keine Preise oder Integrationen erfunden werden.
