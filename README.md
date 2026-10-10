# Küchenfuchs

**Clever kochen. Günstiger einkaufen.**

Küchenfuchs ist eine geplante selbst gehostete Progressive Web App zur Rezeptverwaltung, Essensplanung und Einkaufsoptimierung anhand aktueller Supermarktangebote.

## Status

**P0 – lokale Compose-Basis:** Implementiert auf einem Feature-Branch und zur unabhängigen Prüfung als Pull Request vorgesehen. P1 ist nicht begonnen oder freigegeben.

## Zielbild

- Rezepte verwalten, importieren und mit KI erstellen
- Wochenplan und synchronisierte Einkaufsliste
- Angebote ausgewählter Lebensmittelmärkte erfassen und mit Rezeptzutaten abgleichen
- Einkaufsempfehlungen unter Berücksichtigung von Preisen, Packungsgrößen und Einkaufsstopps
- Auf eigenem Linux-Server per Docker Compose betreiben
- Integration mit ChatGPT via abgesicherter API und KI-Assistent in der PWA

## Vorgehen

Die Architektur und der Implementierungsplan wurden geprüft. Arbeitspaket P0 wurde separat freigegeben; weitere Pakete benötigen jeweils eine ausdrückliche Freigabe.

## Lokale Entwicklung (P0)

Voraussetzungen: Docker mit Docker Compose v2.29 oder neuer. Die Entwicklung läuft über Compose; PostgreSQL erhält keinen Host-Port. Caddy ist lokal standardmäßig unter `http://localhost:8080` erreichbar und benötigt weder öffentliche Domain noch öffentliches TLS-Zertifikat. Ist Port 8080 belegt, `HTTP_PORT` in `.env` ändern.

```sh
cp .env.example .env
docker compose up --build --detach
```

Die Beispielwerte sind ausdrücklich ausschließlich für lokale Entwicklung gedacht und nicht als produktive Secrets geeignet. Compose verlangt `POSTGRES_PASSWORD`; für Produktion muss ein eigenes sicheres Secret gesetzt werden. Für Start, Stopp, Neustart, Logs und automatisierte P0-Tests siehe [docs/deployment.md](docs/deployment.md).

Die P0-Smoke-Tests lassen sich mit `./scripts/test-p0.sh` ausführen. Sie bauen die Images, starten einen isolierten Test-Stack und entfernen diesen danach wieder.

**Auftrag und Regeln:** [CLAUDE.md](CLAUDE.md)  
**Produktanforderungen:** [docs/PROJECT_BRIEF.md](docs/PROJECT_BRIEF.md)

## Hinweis

Externe Angebotsdatenquellen müssen vor der produktiven Nutzung hinsichtlich Zuverlässigkeit, Nutzungsrechten und Regionalität geprüft werden. Es dürfen keine Preise oder Integrationen erfunden werden.
