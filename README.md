# Küchenfuchs

**Clever kochen. Günstiger einkaufen.**

Küchenfuchs ist eine geplante selbst gehostete Progressive Web App zur Rezeptverwaltung, Essensplanung und Einkaufsoptimierung anhand aktueller Supermarktangebote.

## Status

**Planungsphase.** Noch keine Implementierung freigegeben.

## Zielbild

- Rezepte verwalten, importieren und mit KI erstellen
- Wochenplan und synchronisierte Einkaufsliste
- Angebote ausgewählter Lebensmittelmärkte erfassen und mit Rezeptzutaten abgleichen
- Einkaufsempfehlungen unter Berücksichtigung von Preisen, Packungsgrößen und Einkaufsstopps
- Auf eigenem Linux-Server per Docker Compose betreiben
- Integration mit ChatGPT via abgesicherter API und KI-Assistent in der PWA

## Vorgehen

1. Claude Code erstellt zunächst einen überprüfbaren Architektur- und Implementierungsplan.
2. Der Plan wird gemeinsam mit ChatGPT geprüft.
3. Erst nach ausdrücklicher Freigabe wird in kleinen, testbaren Schritten implementiert.

**Auftrag und Regeln:** [CLAUDE.md](CLAUDE.md)  
**Produktanforderungen:** [docs/PROJECT_BRIEF.md](docs/PROJECT_BRIEF.md)

## Hinweis

Externe Angebotsdatenquellen müssen vor der produktiven Nutzung hinsichtlich Zuverlässigkeit, Nutzungsrechten und Regionalität geprüft werden. Es dürfen keine Preise oder Integrationen erfunden werden.
