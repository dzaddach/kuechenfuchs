# Küchenfuchs – Implementierungsplan

**Status:** Planvorschlag, keine Umsetzung freigegeben
**Leitlinie:** Jedes Paket endet in einem baubaren und testbaren Stand. Reihenfolge ist klein, sequenziell und so angelegt, dass der Rezept→Einkauf→Mock-Angebot-Durchstich früh funktioniert.

## Voraussetzungen vor Beginn

- Auftraggeber bestätigt MVP-Umfang, technische Ausgangswerte und Authentifizierungsweg.
- Repository-/Toolchain-Entscheidungen und Zielumgebung sind geprüft; keine echten Angebotsquellen bis zur separaten Quellenfreigabe.
- Anforderungen in [open-decisions.md](./open-decisions.md) sind als „entschieden“, „vertagt“ oder „blockierend“ markiert.

## Arbeitspakete

| Nr. | Paket und Abhängigkeit | Lieferumfang | Tests | Akzeptanzkriterien |
|---|---|---|---|---|
| 0 | Freigabe und minimale technische Basis (keine Abhängigkeit) | Verbindliche Entscheidungen, Repository-Konventionen, CI-Grundgerüst und dokumentierter Entwicklerstart; kein Produktfeature | Clean checkout, Build und Tests automatisiert | Frischer Checkout baut/testet reproduzierbar; keine Secrets; Startanleitung vollständig |
| 1 | API-/UI-Skelett (0) | Angular-App, ASP.NET-Core-API, Reverse-Proxy-Konfiguration für lokale Entwicklung, Health-Endpunkte; noch keine Live-Dienste erforderlich | Frontend Build/Test, API Unit-Test, Health-Integrationstest | PWA und API laufen lokal; definierte Health-Antwort; keine Geschäftslogik |
| 2 | Haushaltsidentität und Persistenz (1) | PostgreSQL/EF-Core, erste Migration, Nutzer-/Haushaltszugriff, Authentifizierung, OpenAPI/Problem Details | Migration von leerer DB; Autorisierungstests inkl. fremder Haushalt; API Integrationstests | Nicht angemeldete/fremde Zugriffe abgewiesen; Migration und Restore-Anleitung nachvollziehbar |
| 3 | Lebensmittel, Einheiten und Rezeptpflege (2) | `Food`, `Unit`, Rezept, Zutatenzeile, Schritte; CRUD; UI zum Anlegen/Ändern/Lesen | Domain-/Mapping-/API-Tests; ungültige Mengen/Portionen; UI-Komponententests | Rezept speichern und wieder öffnen; Zutat bleibt von Produkt/Angebot getrennt; Portionenskalierung korrekt |
| 4 | Wochenplan und Einkaufslisten (3) | Planpositionen/Portionen; Liste aus Rezepten erzeugen; Mengenaggregation gemäß Einheitenregeln; manuelle Einträge und Abhaken | Aggregations-Tests für gleiche/verschiedene Dimensionen, fehlende Mengen und Portionen; API Integrationstest | Einkaufliste entsteht reproduzierbar; keine unzulässige Konversion; Herkunft bleibt sichtbar |
| 5 | Mock-Provider und Produkt-Matching (4) | synthetische Fixture, Provider-Schnittstelle, Produkt↔Lebensmittel-Match, zeitliche Gültigkeit und Angebotsbedingungen | Deterministische Tests: gültig/abgelaufen, Filiale, kein Preis, Coupon, kein Match | UI kennzeichnet Mock; keine erfundenen echten Preise; fehlende Daten bleiben unbekannt |
| 6 | Empfehlung und vollständiger MVP-Durchstich (5) | einfache regelbasierte Einkaufsoption: bekannte Packungskosten/Marktstopps, offene Positionen und erklärbare Kostenbasis | Ende-zu-Ende-Szenarien mit Fixture; Summen-/Packungsrundung; unvollständige Preise | Akzeptanzpfad aus Produktanforderungen gelingt: Rezept anlegen→speichern→Zutaten/Portionen berechnen→Liste→Mock-Angebote→begründete Empfehlung |
| 7 | PWA-Offline und Konflikte (6) | installierbare PWA, App-Shell-/bewusster Daten-Cache, Offline-Lesezugriff, ausstehende Listenänderungen, ETag-Konflikte | Service-Worker- und UI-Tests; Offline-Wiedereintritt; parallele Versionsänderungen | Offlinezustand erkennbar; keine stillen verlorenen Updates; veraltete Angebote nicht als aktuell empfohlen |
| 8 | Betriebsreife (7) | Docker Compose Zielkonfiguration, Proxy/TLS-Vorgaben, Secret-Dateien, Logs/Health, Backup/Restore und Updateanleitung | Container-Build; Smoke-Test; Backup-Restore-Übung; Konfigurationsprüfung | Nur Proxy öffentlich; DB intern; erfolgreicher Restore belegt; Secrets fehlen in Git/Image |
| 9 | KI-Rezeptentwürfe (6; vor Produktion Datenschutz-/Kostenentscheid) | serverseitiger Anbieteradapter, Schema-/Domainvalidierung, Kostenlimits, UI-Vorschau und expliziter Speicherschritt | gefälschter Client ohne Netz/Kosten; Fehler-/Timeout-/Budgettests; Speichern nur nach Bestätigung | Kern-MVP funktioniert ohne KI; Schlüssel verborgen; kein automatisches Speichern |
| 10 | ChatGPT-Actions (2, 8, separate Auth-Entscheidung; nach MVP) | schmales OpenAPI-Schema, nutzerbezogene OAuth-Authentifizierung, Entwurf und ausdrücklich bestätigter Rezept-Speicheraufruf | Token-/Scope-/Mandantentests; abgelaufener/widerrufener Token; keine Fremdhaushaltszugriffe | Keine anonymen Schreibvorgänge oder geteilten API-Schlüssel; Action kann ohne Auth nicht speichern |
| 11 | Reale Angebotsquelle (5, 6, individuelle Quellenfreigabe) | genau ein Provider für freigegebene Pilotregion, dokumentierter Adapter und Deaktivierungsmöglichkeit | Fixture-basierte Contract Tests, Quellqualitäts-/Ablauf-/Regionsfälle, Last- und Ausfalltest | Belege, Nutzungsfreigabe, Aktualität und Datenfelder vor Nutzung dokumentiert; Mock bleibt vollständiger Fallback |

## Priorisierung und MVP-Schnitt

**MVP zuerst:** Pakete 0–6; Paket 7 ist ein klar begrenztes PWA-/Synchronisations-Hardening vor breiter Nutzung. Paket 8 schließt Deployment und Wiederherstellbarkeit vor produktivem Selbsthosting ab. KI, ChatGPT-Actions und echte Angebotsquellen sind bewusst nachgelagert und können unabhängig vom Kernnutzen freigegeben werden.

Keine echte Quelle in Paketen 0–8 voraussetzen. Das MVP muss bei Netzwerk-/Provider-Ausfall mit synthetischen Mock-Fixtures vollständig demonstrierbar bleiben.

## Qualitätsschranken jedes Pakets

- Tests passend zur Änderung (Unit, API-Integration und gezielte UI/E2E-Tests).
- Keine ungetestete Migration; Migrationspfad von leerer Datenbank und Backup/Restore prüfen.
- Jeder Fehlerzustand ist sichtbar; keine stillen Defaults bei Einheit, Preis, Herkunft oder Aktualität.
- Keine unreviewten Secrets, echten persönlichen Daten oder ungeklärten Nutzungsrechte.
- Aktualisierte API-/Architektur-/Betriebsdokumentation im selben Paket.
- Nach jedem Paket Build und relevante Tests aus sauberem Checkout erfolgreich.

## Abnahme des zentralen End-to-End-Pfads

Mit synthetischen Testdaten kann ein Benutzer (1) ein Rezept mit Zutaten erfassen, (2) Portionen anpassen, (3) einen Einkauf aus Rezepten erzeugen, (4) kompatible Zutaten aggregieren, (5) gültige Mock-Angebote abgleichen und (6) eine nachvollziehbare Empfehlung sehen, die fehlende Preise/Zuordnungen sichtbar lässt. Es werden keine realen Angebotspreise vorgetäuscht.
