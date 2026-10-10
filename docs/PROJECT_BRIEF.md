# Produktanforderungen – Küchenfuchs

## Vision
Private, mobile-first PWA für Rezepte, Essensplanung und günstiges Einkaufen. Beide Einstiegspunkte sind gleichwertig:
- **Rezeptorientiert:** Gerichte auswählen, Zutaten aggregieren, Einkaufsoptionen vergleichen.
- **Angebotsorientiert:** Verfügbare Angebote analysieren, passende Gerichte vorschlagen.

## Funktionen
1. **Rezepte:** Erstellen, Bearbeiten, Favoriten, Fotos, Tags, Portionen skalieren, Zubereitung, Suche; optional Import per URL unter Beachtung von Nutzungsrechten.
2. **KI:** KI-Rezeptassistent in der PWA, strukturierte Vorschläge mit Vorschau vor Speichern; externe ChatGPT-Integration zum bewussten Speichern über authentifizierte REST-API. API-Kosten und Datenschutz berücksichtigen.
3. **Planung:** Wochenplan mit Personenanzahl und Gerichten; daraus Einkaufslisten erzeugen.
4. **Einkauf:** Gleiche Zutaten und Einheiten korrekt aggregieren, manuelle Posten ergänzen, Artikel abhaken, geräteübergreifend synchronisieren. Mehrbenutzer-/Familiennutzung konzeptionell vorsehen.
5. **Angebote:** Provider-Adapter für zunächst Lidl, REWE, Aldi, EDEKA, Kaufland untersuchen; Filiale, Zeitfenster, Preis, Packungs- und Grundpreis, Provenienz. Zuerst Mock-Provider.
6. **Matching:** Normalisierte Lebensmittel getrennt von Rezept-Zutatenzeilen und konkreten Händlerprodukten. Regeln zuerst; KI nur optional bei Unklarheit. Unsicherheit sichtbar machen.
7. **Optimierung:** Reale Packungskosten, Anzahl der Märkte, optional Fahrtkosten/Vorräte berücksichtigen. Fehlende Preise offen kennzeichnen.

## Technische Präferenzen (vorbehaltlich Architekturprüfung)
- Angular / TypeScript / PWA, mobile-first mit iPhone-Schwerpunkt
- ASP.NET Core Web API / C#
- PostgreSQL / EF Core
- Docker Compose auf eigenem Linux-Server
- .NET Background Worker für Angebotsbeschaffung
- OpenAI API für KI-Funktionen
- Caddy oder Traefik als Reverse Proxy
- Keine unnötigen Microservices

## Qualitätsanforderungen
- Sicherheitskonzept für Authentifizierung und externe Eingaben (insbesondere URL-Import/SSRF)
- Keine Secrets im Repository, Umgebungsvariablen und Secret-Verwaltung
- OpenAPI, Migrationen, Logs, Health Checks, Backups/Restore und Tests
- Sinnvolle Offline-Funktionen (z. B. lesbare Rezepte und Einkaufslisten), sichere Synchronisierung
- Angebotsquellen nicht als stabil voraussetzen; rechtlich zulässige Nutzung prüfen

## Erster vollständiger Durchstich (MVP)
Rezept anlegen → Speichern → Zutaten/Portionen berechnen → Einkaufsliste erstellen → Mock-Angebote abgleichen → nachvollziehbare Einkaufsempfehlung anzeigen.

## Offene Entscheidungen
Claude soll Vorschläge inkl. Abwägungen liefern. Keine Designentscheidung allein durch eine unverifizierte Annahme verfestigen.
