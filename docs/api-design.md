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
| `PATCH /api/v1/households/{hid}/shopping-lists/{lid}/items/{itemId}` | Menge, Notiz oder erledigt-Status ändern | Partielle Änderung, `If-Match`; bei paralleler Online-Änderung aktuellen Stand explizit liefern |
| `GET /api/v1/households/{hid}/offers?store=&region=&validAt=` | Angebote abfragen | Ergebnis mit Quelle, Erfassungszeit, Gültigkeit, Bedingungen und Mock-Kennzeichnung |
| `POST /api/v1/households/{hid}/recommendations` | Einkaufsempfehlung erstellen | Eindeutige Kriterien; nicht bekannte Preise bleiben unbekannt; deterministisch testbar |
| `GET /api/v1/households/{hid}/recommendations/{id}` | Empfehlung samt Erklärung abrufen | Jede Zuordnung und Kostengrundlage nachvollziehbar |

Das MVP kann Pläne zunächst nur im UI oder als vereinfachte Tabelle führen, wenn der Rezept→Einkauf→Mock-Ablauf erhalten bleibt. Eine Endpunktfamilie soll nicht implementiert werden, bevor sie im akzeptierten Arbeitspaket benötigt wird.

## KI- und externe ChatGPT-Endpunkte

### PWA-KI-Assistent (frühe Ausbaustufe)

`POST /api/v1/households/{hid}/ai/recipe-drafts` nimmt eine begrenzte Anfrage an (Prompt, gewünschte Portionen, optionale Zutaten/Allergene). Antwort ist ein **nicht gespeicherter** Entwurf mit validiertem Schema, Kennzeichnung unsicherer Felder und gegebenenfalls Fehler-/Refusal-Status. Die anschließende Speicherung erfolgt ausschließlich mit dem normalen `POST .../recipes` nach sichtbarer Nutzerbestätigung.

### ChatGPT-Actions (frühe Ausbaustufe nach Rezept-Grundablauf)

- `GET /api/v1/chatgpt/recipes/schema` oder statisch publiziertes OpenAPI-Dokument für die Action; nur freigegebene Operations.
- `POST /api/v1/chatgpt/recipes/drafts` liefert einen strukturierten Entwurf, schreibt nichts dauerhaft.
- `POST /api/v1/chatgpt/recipes` speichert nur ein vom Nutzer ausdrücklich bestätigtes Rezept; Action-Beschreibung muss Bestätigung vor Aufruf verlangen.
- Für belastbare Bestätigung kann ein kurzlebiger, benutzergebundener Entwurf erzeugt und erst in einem separaten finalen Aufruf gespeichert werden. Entwürfe laufen ab und sind keine dauerhaft gespeicherten Rezepte.
- Authentifizierung nutzerbezogen über OAuth 2.0 Authorization Code; eng begrenzte Scopes wie `recipes:read` und `recipes:write`. Keine Haushalts-übergreifenden Sammelkonten oder globalen Bearer-Schlüssel.
- Token kurzlebig, widerrufbar und gehasht gespeichert; `state`, feste Redirect-URI-Allowlist, Rate Limits und Audit-Ereignis für Speichervorgänge.
- Küchenfuchs implementiert keinen eigenen OAuth-/OIDC-Autorisierungsserver. P2 verwendet ASP.NET Core Identity ausschließlich für den PWA-Login und die Same-Origin-Cookie-Session; daraus folgt keine ChatGPT-Actions-OAuth-Kompatibilität.
- Für Actions wird als Kandidat ein bestehender OIDC-Provider wie Keycloak geprüft. Er muss den für Actions benötigten Authorization-Code-Ablauf mit Client-Authentifizierung, freigegebenen Redirect-URIs, `state` und nutzerbezogenen Zugriffstokens unterstützen. Die Verfügbarkeit der Funktion in einem Produkt allein belegt noch keine Kompatibilität: vor Freigabe von P5a ist der konkrete Provider mit ChatGPT Actions praktisch end-to-end zu testen.
- Ein späterer Provider-Wechsel erfordert eine sichere Kontoverknüpfung zwischen der externen OIDC-Identität (`issuer` + `subject`) und dem internen Küchenfuchs-Benutzer. Die API leitet Benutzer- oder Haushaltsrechte niemals ungeprüft aus vom Client gelieferten IDs ab. Haushaltszuordnung und erlaubte Scopes werden serverseitig geprüft; Logout/Widerruf und Ablauf müssen Folgeaufrufe sperren.
- Der Action-Bearer-Token ist ausschließlich für die Küchenfuchs-API bestimmt. Vor der Autorisierung prüft die API Signatur anhand der vertrauenswürdigen Issuer-Metadaten/JWKS sowie exaktes `iss`, `aud`, `exp`/`nbf` und die erforderlichen Scopes; Schlüsselrotation muss unterstützt sein. Kein ID-Token, ungeprüfter JWT-Claim oder Token für eine andere API genügt als Berechtigungsnachweis.
- OpenIddict ist eine etablierte .NET-Bibliothek mit OIDC-Client-, Server- und Tokenvalidierungsfunktionen, aber kein betriebsfertiger Identity Provider. Das Projekt beschreibt es ausdrücklich als Framework, das für einen eigenen Server zusätzliche Implementierung (typischerweise Autorisierungsendpunkte) benötigt. Es ist daher kein Beleg für „keinen eigenen OAuth-Server“ und wird für diesen Plan nicht als fertige Serverlösung angenommen.
- ChatGPT-Action-Schreibaufrufe dürfen nur vom Nutzer bestätigte Rezeptdaten speichern. Action-Beschreibung, UI und API müssen diese Grenze übereinstimmend erzwingen; die Action darf nicht als verlässlicher Ersatz für Nutzerbestätigung interpretiert werden.

### ChatGPT-Actions und OAuth/OIDC

Die aktuelle offizielle GPT-Actions-Dokumentation beschreibt nutzerbezogenen OAuth mit Client-ID, Client-Secret, Autorisierungs- und Token-URL sowie Scope. Der Token-Austausch verwendet `authorization_code`; der Token-Endpunkt liefert ein Bearer-Access-Token und kann zusätzlich ein Refresh-Token liefern. ChatGPT übermittelt das Nutzer-Token bei Action-Aufrufen im `Authorization`-Header. `state` ist vorgeschrieben; die Callback-Redirect-URI muss beim Provider registriert sein. Die akzeptierten Callback-Hosts/URLs und etwaige Anforderungen können sich ändern und müssen vor P5a erneut mit der offiziellen Dokumentation abgeglichen werden.

**Kandidatenbewertung (11. Oktober 2026):**

| Kandidat | Belegter Umfang | Bewertung für Küchenfuchs |
|---|---|---|
| Keycloak als externer Provider | Die offizielle Server-Administration dokumentiert Realms, Clients, Benutzer und Rollen; Keycloak dokumentiert OIDC-Funktionen und Client-Policies/PKCE. | Bevorzugter Kandidat, nicht bereits freigegeben. Ein dedizierter confidential client/Realm kann die Actions-Identität getrennt führen. P5a muss Authorization Code mit Client-Authentifizierung, Refresh-Verhalten, Token-Audience/Scopes, Redirect-Allowlist praktisch prüfen sowie Identitätsverknüpfung und Haushaltsrechte in der API nachweisen. Zusätzlicher Dienst erhöht Betrieb, Updates und Backupumfang. |
| OpenIddict in der Küchenfuchs-API | Das Projekt unterstützt OIDC-Client-, Server- und Tokenvalidierungsfunktionen sowie Authorization-Code-Flows und EF Core. Das Projekt stellt klar, dass OpenIddict kein Turnkey-Produkt ist und eigener Autorisierungscode erforderlich ist. | Für ein ausdrückliches „keinen eigenen OAuth-Server entwickeln“ nicht als alleinige Lösung ausreichend. Nur neu bewerten, falls der Auftraggeber später ausdrücklich eine selbst betriebene, in der Anwendung integrierte Autorisierungsinstanz freigibt. |

P2 bleibt unabhängig davon ASP.NET-Core-Identity-basiert; es richtet weder Keycloak noch OpenIddict ein. Wenn Keycloak später gewählt wird, muss vor Implementierung entschieden werden, wie Benutzer sicher provisioniert/verknüpft werden, wie ein Nutzer Einwilligung erteilt und welche Haushalte/Scopes er freigeben darf. Ohne eindeutige, überprüfte Verknüpfung gibt es keinen Zugriff. Die PWA-Anmeldung und die Actions-Autorisierung werden nicht durch das Teilen von Passwörtern, Cookies oder globalen API-Schlüsseln gekoppelt.

**Quellen (abgerufen 11.10.2026):**

- OpenAI, [GPT Action authentication](https://developers.openai.com/api/docs/actions/authentication) — OAuth-Parameter, Authorization-Code-Austausch, Bearer-Token, `state` und Callback-Redirects.
- Keycloak, [Server Administration Guide](https://www.keycloak.org/docs/latest/server_admin/) — Konzepte für Realm, Client, Benutzer, Rollen und Einwilligung.
- Keycloak, [OIDC layers / Client policies](https://www.keycloak.org/securing-apps/oidc-layers) — OIDC- und PKCE-bezogene Client-Policies.
- OpenIddict, [Projektbeschreibung und Getting Started](https://github.com/openiddict/openiddict-core#what-is-openiddict) — Framework für OIDC Client/Server/Tokenvalidierung; kein Turnkey-Server und eigener Autorisierungscode erforderlich.

## Authentifizierung und Sicherheit

- PWA: Empfehlung Same-Origin, sichere `HttpOnly`, `Secure`, angemessen `SameSite`-Cookies; **jeder Cookie-authentifizierte schreibende API-Endpunkt** (POST, PUT, PATCH, DELETE und sonstige zustandsändernde Methoden) muss wirksam CSRF-geschützt sein. Mutationen verlangen einen gültigen serverseitig validierten Anti-Forgery-Token oder eine gleichwertige, dokumentierte CSRF-Abwehr; SameSite allein genügt nicht. Neue P3-Endpunkte müssen diesen Schutz und Ablehnung bei fehlendem/ungültigem Token in Integrationstests nachweisen. Keine langlebigen Bearer-Tokens in `localStorage`.
- ChatGPT-Client: separate OAuth-Integration und Scopes; eine angemeldete PWA-Session ist nicht übertragbar. Die OAuth-Autorisierung muss Haushalt und Nutzer unveränderbar an den Token binden und bei jedem Zugriff serverseitig prüfen.
- Jeder Endpunkt autorisiert Haushaltsmitgliedschaft, Ressourcenzugriff und Rolle serverseitig. IDs sind nicht geheim und erlauben keinen Zugriff.
- Validierung von Payload-Größen, Zeichensatz, Datumsbereichen, Mengen, URI-Längen und Paginierungsgrenzen; Schutz vor Mass Assignment durch DTO-Allowlist.
- Begrenzte Rate Limits für Anmeldung, KI und externe Integrationen. Keine vertraulichen Inhalte in Logs.
- URL-Rezeptimport ist **nicht** Teil des MVP. Vor Aufnahme: nur HTTP(S), DNS/IP-Prüfung gegen private/loopback/link-local/reservierte Netze, Redirects begrenzen und nach jedem Redirect neu validieren, Egress-Beschränkung, Antwortgrößen-/Zeitlimits, Content-Type-Prüfung, Schutz vor DNS-Rebinding. URL nicht einfach an einen unbeschränkten Server-HTTP-Client übergeben.
- CORS nicht wildcard; bei Same-Origin kein breites Cross-Origin-Fenster. TLS-only, sichere Standardheader.
- APIs für Quellen und OpenAI dürfen keine Zugangsdaten an den Browser ausgeben. Externe Anbieterfehler als expliziter Zustand darstellen, nicht als leeres erfolgreiches Ergebnis tarnen.

## Online-PWA und spätere Offline-Erweiterung

Die erste PWA ist installierbar, benötigt für Datenzugriff aber eine Verbindung zur API. Offline-Datenzugriff, lokale Schreibwarteschlange und Konfliktauflösung sind vertagt. Die serverseitige API behält ETags/Concurrency-Schutz gegen gleichzeitig ausgeführte Online-Änderungen, ohne daraus einen Offline-Synchronisationsumfang abzuleiten.
