# Küchenfuchs – API-Design

**Stand:** 10. Oktober 2026 · **Status:** Entwurf; OpenAPI-Vertrag entsteht erst nach Freigabe

## Konventionen

- Basis `/api/v1`; JSON in UTF-8, UUID-IDs, Datumswerte ISO 8601 mit Offset/UTC.
- REST-Ressourcen nach Haushalt getrennt: `/households/{householdId}/...`. Die API prüft Mitgliedschaft bei jedem Aufruf.
- Dezimalwerte als JSON-Zahlen mit definierter Präzision; `amount: null` bedeutet unbekannt/„nach Geschmack“, nicht null Menge.
- `ETag`/`If-Match` bei veränderlichen Rezepten und Listen; bei veralteter Version `412 Precondition Failed` oder einheitlich gewähltes `409 Conflict` – final im OpenAPI-Vertrag entscheiden.
- Fehler als RFC 9457 Problem Details, ohne Stacktrace oder Geheimnisse. Validierungsfehler nennen sichere Feldpfade.
- Listenoperationen idempotent gestalten, insbesondere Toggle/Erledigt-Änderungen nicht als blinden „toggle“-Befehl modellieren: `PATCH { isChecked: true }`.

## MVP-Endpunkte

| Methode und Pfad | Zweck | Wesentliche Regeln |
|---|---|---|
| `GET /api/v1/health/live` | Prozess-Liveness | Keine internen Details |
| `GET /api/v1/health/ready` | Bereitschaft inkl. Datenbankverbindung | Authentifizierte oder Proxy-begrenzte Betriebsroute |
| `GET /api/v1/me` | Angemeldeter Benutzer und verfügbare Haushalte | Keine fremden Haushaltsdaten |
| `GET /api/v1/households/{hid}/recipes` | Rezeptliste, Suche/Paginierung | Haushaltsmitgliedschaft prüfen |
| `POST /api/v1/households/{hid}/recipes` | Rezeptentwurf anlegen | Eingaben, Portionen, Zutaten und Einheiten serverseitig validieren |
| `GET /api/v1/households/{hid}/recipes/{rid}` | Rezept inkl. Zutaten/Schritte lesen | `ETag` ausgeben |
| `PUT /api/v1/households/{hid}/recipes/{rid}` | Rezept ändern | `If-Match` erforderlich; konkurrierende Änderung nicht still überschreiben |
| `DELETE /api/v1/households/{hid}/recipes/{rid}` | Rezept archivieren/löschen | MVP vorzugsweise Soft-Delete bzw. nachvollziehbare Archivierung |
| `GET /api/v1/households/{hid}/foods` | Lebensmittel suchen | Paginierung, normalisierte Daten |
| `GET /api/v1/households/{hid}/meal-plans?from=&to=` | Zeitraumplan lesen | Zeitraumgrenzen validieren |
| `PUT /api/v1/households/{hid}/meal-plans/{date}` | Einträge für Datum ersetzen/aktualisieren | Eindeutige Idempotenz und Versionierung |
| `POST /api/v1/households/{hid}/shopping-lists` | Liste aus ausgewählten Plan-/Rezeptpositionen erzeugen | Skalierung und Aggregation nach dokumentierten Regeln; Ursprung erhalten |
| `GET /api/v1/households/{hid}/shopping-lists/{lid}` | Liste mit Einträgen/Version lesen | ETag und Listenstatus |
| `POST /api/v1/households/{hid}/shopping-lists/{lid}/items` | Manuellen Posten hinzufügen | Freitext erlaubt, Lebensmittel-Zuordnung optional |
| `PATCH /api/v1/households/{hid}/shopping-lists/{lid}/items/{itemId}` | Menge, Notiz oder erledigt-Status ändern | Partielle Änderung, `If-Match`; offline Konflikt explizit liefern |
| `GET /api/v1/households/{hid}/offers?store=&region=&validAt=` | Angebote abfragen | Ergebnis mit Quelle, Erfassungszeit, Gültigkeit, Bedingungen und Mock-Kennzeichnung |
| `POST /api/v1/households/{hid}/recommendations` | Einkaufsempfehlung erstellen | Eindeutige Kriterien; nicht bekannte Preise bleiben unbekannt; deterministisch testbar |
| `GET /api/v1/households/{hid}/recommendations/{id}` | Empfehlung samt Erklärung abrufen | Jede Zuordnung und Kostengrundlage nachvollziehbar |

Das MVP kann Pläne zunächst nur im UI oder als vereinfachte Tabelle führen, wenn der Rezept→Einkauf→Mock-Ablauf erhalten bleibt. Eine Endpunktfamilie soll nicht implementiert werden, bevor sie im akzeptierten Arbeitspaket benötigt wird.

## KI- und externe ChatGPT-Endpunkte

### PWA-KI-Assistent (nach MVP)

`POST /api/v1/households/{hid}/ai/recipe-drafts` nimmt eine begrenzte Anfrage an (Prompt, gewünschte Portionen, optionale Zutaten/Allergene). Antwort ist ein **nicht gespeicherter** Entwurf mit validiertem Schema, Kennzeichnung unsicherer Felder und gegebenenfalls Fehler-/Refusal-Status. Die anschließende Speicherung erfolgt ausschließlich mit dem normalen `POST .../recipes` nach sichtbarer Nutzerbestätigung.

### ChatGPT-Actions (spätere Freigabe)

- `GET /api/v1/chatgpt/recipes/schema` oder statisch publiziertes OpenAPI-Dokument für die Action; nur freigegebene Operations.
- `POST /api/v1/chatgpt/recipes/drafts` liefert einen strukturierten Entwurf, schreibt nichts dauerhaft.
- `POST /api/v1/chatgpt/recipes` speichert nur ein vom Nutzer ausdrücklich bestätigtes Rezept; Action-Beschreibung muss Bestätigung vor Aufruf verlangen.
- Authentifizierung nutzerbezogen über OAuth 2.0; eng begrenzte Scopes wie `recipes:read` und `recipes:write`. Keine Haushalts-übergreifenden Sammelkonten oder globalen Bearer-Schlüssel.
- Token kurzlebig, widerrufbar und gehasht gespeichert; `state`, feste Redirect-URI-Allowlist, Rate Limits und Audit-Ereignis für Speichervorgänge.
- Vor Implementierung prüfen, welche OAuth-Flows/Anforderungen ChatGPT Actions aktuell unterstützen und ob sie mit der vorgesehenen Auth-Komponente vereinbar sind.

## Authentifizierung und Sicherheit

- PWA: Empfehlung Same-Origin, sichere `HttpOnly`, `Secure`, angemessen `SameSite`-Cookies; CSRF-Schutz für schreibende Cookie-authentifizierte Aufrufe. Keine langlebigen Bearer-Tokens in `localStorage`.
- ChatGPT-Client: separate OAuth-Integration und Scopes; eine angemeldete PWA-Session ist nicht übertragbar.
- Jeder Endpunkt autorisiert Haushaltsmitgliedschaft, Ressourcenzugriff und Rolle serverseitig. IDs sind nicht geheim und erlauben keinen Zugriff.
- Validierung von Payload-Größen, Zeichensatz, Datumsbereichen, Mengen, URI-Längen und Paginierungsgrenzen; Schutz vor Mass Assignment durch DTO-Allowlist.
- Begrenzte Rate Limits für Anmeldung, KI und externe Integrationen. Keine vertraulichen Inhalte in Logs.
- URL-Rezeptimport ist **nicht** Teil des MVP. Vor Aufnahme: nur HTTP(S), DNS/IP-Prüfung gegen private/loopback/link-local/reservierte Netze, Redirects begrenzen und nach jedem Redirect neu validieren, Egress-Beschränkung, Antwortgrößen-/Zeitlimits, Content-Type-Prüfung, Schutz vor DNS-Rebinding. URL nicht einfach an einen unbeschränkten Server-HTTP-Client übergeben.
- CORS nicht wildcard; bei Same-Origin kein breites Cross-Origin-Fenster. TLS-only, sichere Standardheader.
- APIs für Quellen und OpenAI dürfen keine Zugangsdaten an den Browser ausgeben. Externe Anbieterfehler als expliziter Zustand darstellen, nicht als leeres erfolgreiches Ergebnis tarnen.

## Offline-Synchronisation und Konflikte

Die erste Offline-Funktion ist lesender Zugriff auf bewusst zwischengespeicherte Rezepte/Listen plus lokale Markierungen als „noch nicht synchronisiert“. Keine sensiblen API-Antworten blind im Cache ablegen. Schreibänderungen nutzen Client-Operation-ID und Version/ETag; bei Versionskonflikt `409` mit aktuellem Stand und Nutzerentscheidung. Keine „last write wins“-Zusicherung.
