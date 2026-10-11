# Küchenfuchs – API-Design

**Stand:** 11. Oktober 2026 · **Status:** P3-Rezeptvertrag und P4-PWA-KI-Entwurf umgesetzt; externe ChatGPT-Actions bleiben Entwurf

## Konventionen

- Basis `/api/v1`; JSON in UTF-8, UUID-IDs, Datumswerte ISO 8601 mit Offset/UTC.
- REST-Ressourcen nach Haushalt getrennt: `/households/{householdId}/...`. Die API prüft Mitgliedschaft bei jedem Aufruf.
- Dezimalwerte als JSON-Zahlen mit definierter Präzision; `amount: null` bedeutet unbekannt/„nach Geschmack“, nicht null Menge.
- `ETag`/`If-Match` bei veränderlichen Rezepten; fehlender `If-Match` ergibt `428 Precondition Required`, eine veraltete Version `412 Precondition Failed`.
- Fehler als RFC 9457 Problem Details, ohne Stacktrace oder Geheimnisse. Validierungsfehler nennen sichere Feldpfade.
- Alle zustandsändernden Cookie-authentifizierten Anfragen (POST, PUT, PATCH, DELETE) verlangen das serverseitig validierte Antiforgery-Token im Header `X-KF-CSRF`; SameSite-Cookies sind nur eine zusätzliche Schutzschicht.
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
| `GET /api/v1/households/{hid}/foods/{foodId}` | Katalog-Lebensmittel abrufen | Mitgliedschaft prüfen |
| `GET /api/v1/households/{hid}/units` | stabile Einheiten und Dimensionen abrufen | Mitgliedschaft prüfen; keine unsichere Dimensionsumrechnung |
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

### P3-Rezeptvertrag

Implementierte Endpunkte:

- `GET /api/v1/households/{hid}/recipes?q=&category=&includeArchived=` listet bis zu 100 nicht archivierte Rezepte; Suche umfasst Name, Beschreibung, Zutatenbezeichnung und Tags.
- `POST /api/v1/households/{hid}/recipes` erstellt das Rezept samt Zutaten und Schritten und antwortet mit `201 Created` und `ETag`.
- `GET /api/v1/households/{hid}/recipes/{rid}` liefert Rezeptdetails und `ETag`.
- `PUT /api/v1/households/{hid}/recipes/{rid}` ersetzt die bearbeitbaren Rezeptfelder, Zutaten und Schritte atomar; `If-Match` ist erforderlich.
- `DELETE /api/v1/households/{hid}/recipes/{rid}` archiviert das Rezept und verlangt ebenfalls `If-Match`.
- `GET /api/v1/households/{hid}/foods?q=` und `/foods/{foodId}` lesen den normalisierten Lebensmittelkatalog; `/units` liefert Einheiten mit Dimension.
- `GET /api/v1/households/{hid}/recipe-favorites` liefert die Favoriten-IDs des Haushalts.
- `PUT /api/v1/households/{hid}/recipes/{rid}/favorite` und `DELETE .../favorite` setzen bzw. entfernen einen haushaltsweit geteilten Favoriten idempotent; beide Schreibzugriffe verlangen CSRF-Schutz.

Alle Routen liegen in der autorisierten API-Gruppe und prüfen die Haushaltsmitgliedschaft serverseitig. Unbekannte Haushalte und haushaltsfremde Ressourcen werden nicht offengelegt (404). `amount: null` bleibt unbekannt; Mengen müssen positiv sein und höchstens drei Nachkommastellen besitzen. Mengen werden nicht zwischen Dimensionen umgerechnet.

## KI- und externe ChatGPT-Endpunkte

### P4: PWA-KI-Assistent

`POST /api/v1/households/{hid}/ai/recipe-drafts` erwartet eine cookie-authentifizierte Anfrage mit gültigem `X-KF-CSRF`-Token und serverseitig bestätigter Haushaltsmitgliedschaft:

```json
{
  "prompt": "Ein schnelles Abendessen mit Kartoffeln",
  "desiredServings": 3,
  "availableIngredients": ["Kartoffeln", "Paprika"],
  "preferences": ["schnell"],
  "avoidIngredients": [],
  "externalProcessingConfirmed": true
}
```

`externalProcessingConfirmed` muss `true` sein; die Oberfläche zeigt unmittelbar vor dem Aufruf die Verarbeitung durch OpenAI an. Anfragegrenzen: Prompt gemäß Konfiguration (lokaler Beispielwert 2.000 Zeichen), bis zu 30 vorhandene Zutaten, 12 Wünsche und 20 zu vermeidende Zutaten; einzelne Werte sind ebenfalls längenbegrenzt.

Erfolg liefert `200 OK` mit `recipe` in der Form des bestehenden Rezept-Schreibvertrags (`name`, `description`, `baseServings`, `preparationMinutes`, `category`, `tags`, `ingredients`, `steps`) sowie `warnings`. Der Entwurf wird nicht gespeichert. Alle Antwortfelder werden serverseitig gegen Längen-/Anzahl-/Portions-/Zeit-/Mengenlimits und den globalen Einheitenkatalog geprüft. Unbekannte oder nicht unterstützte Einheiten, unplausible Mengen, fehlende Pflichtfelder, ungültiges JSON oder Abweichung von angeforderten Portionen ergeben `502 Bad Gateway`; unbekannte Lebensmittel werden nicht automatisch verknüpft (`foodId: null`) und als Hinweis ausgegeben. `amount: null` bleibt eine unbekannte Menge und erzeugt einen sichtbaren Prüfhinweis; sie ist niemals eine Nullmenge. Texte werden in Angular ausschließlich als Text dargestellt.

Fehlerantworten: `400` bei ungültigen Eingaben oder fehlender externer Einwilligung; `401` ohne Anmeldung; `404` bei fehlender Haushaltsmitgliedschaft; `422` bei einer ausdrücklichen Provider-Ablehnung; `429` bei KI-Ratenlimit oder Monatsbudget; `502` bei Provider-/Schemafehlern; `503`, wenn die KI-Konfiguration unvollständig ist. Providerdetails und Eingabeinhalte werden nicht in Problem Details oder normalen Logs ausgegeben.

Die Speicherung erfolgt nach Prüfung/Bearbeitung im vorhandenen Rezepteditor und ausschließlich über das bestehende `POST /api/v1/households/{hid}/recipes` mit CSRF-Schutz. Der Entwurfsendpunkt schreibt weder Rezepte noch temporäre Entitäten in die Datenbank; die monatliche Budgetentität enthält nur aggregierte Request-, Kosten- und Reservierungswerte.

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

### Keycloak-Verknüpfung und Bearer-Prüfung (P5a.1/P5a.2, umgesetzt)

Nur aktiv mit `Keycloak__Enabled=true` (lokal über `compose.keycloak.yaml`).

| Methode / Pfad | Authentifizierung | Verhalten |
|---|---|---|
| `POST /api/v1/integrations/keycloak/link-requests` `{ "householdId": "…" }` | PWA-Cookie + `X-KF-CSRF` | Nur aktive Mitglieder; sonst `404`. Legt eine einmalige Anfrage an (10 min), setzt das HttpOnly-Bindungscookie `KF.KeycloakLink` (SameSite=Lax, Pfad = Callback) und liefert `{ authorizationUrl, expiresAt }` (`no-store`). Die PWA navigiert per Top-Level-Navigation dorthin. Ältere offene Anfragen des Benutzers werden verworfen. |
| `GET /api/v1/integrations/keycloak/callback?code&state` | anonym; Bindungscookie, optional passende PWA-Sitzung | Verbraucht die Anfrage atomar, prüft Ablauf, Browserbindung, tauscht den Code serverseitig (PKCE, Client-Secret) und prüft das ID-Token (Signatur, Issuer, Audience, `nonce`). Beendet vor dem Anlegen alle Keycloak-Sitzungen der Identität (sonst `unavailable`). Antwort immer `302` auf `/settings/chatgpt?keycloakLink=linked|conflict|expired|cancelled|unavailable|failed`, ohne Tokens oder Gründe in der URL. |
| `GET /api/v1/integrations/keycloak/links` | PWA-Cookie | Eigene Verknüpfungen (`id`, `issuer`, `householdId`, `householdName`, `createdAt`, `revokedAt`, `providerSessionsRevokedAt`, `lastBearerAccessAt`, `chatGpt`); kein externes Subject. `chatGpt` nur für die aktive Verknüpfung: `{ state: authorized|not-authorized|unknown, sessionLastAccessAt }` – `authorized` nur, wenn Keycloak eine Sitzung des Clients `chatgpt-actions` meldet. |
| `DELETE /api/v1/integrations/keycloak/links/{linkId}` | PWA-Cookie + `X-KF-CSRF` | Widerruf: `200 { revoked: true, providerSessionsEnded }`. Sperrt Bearer-Zugriffe sofort und beendet alle Keycloak-Sitzungen der Identität (Refresh-Tokens ungültig); bei `providerSessionsEnded: false` erneut aufrufen. Fremde/unbekannte IDs `404`. |
| `GET /api/v1/integrations/chatgpt/households/{householdId}/authorization-check` | ausschließlich `Authorization: Bearer` (Schema `KeycloakBearer`) | Diagnose ohne personenbezogene Daten: `{ status, householdId, scopes }`. `401` bei ungültigem Token, fehlendem `iat`, vor `TokensNotBefore` der aktiven Verknüpfung ausgestelltem Token, unbekannter/widerrufener Verknüpfung oder gesperrtem Benutzer; `403` ohne `recipes:read`; `404` für einen anderen als den gebundenen Haushalt oder bei entfernter Mitgliedschaft; `429` mit `Retry-After: 60` über `Keycloak:BearerRequestsPerMinute` (Standard 60) je Verknüpfung. Kein CSRF-Header erforderlich, Cookies werden ignoriert. |

Die Bearer-Endpunkte liegen bewusst außerhalb der Cookie-geschützten `/api/v1`-Gruppe; Bearer-Tokens öffnen keine Cookie-Endpunkte und umgekehrt.

### ChatGPT-Actions und OAuth/OIDC

Das konkrete P5a-Konzept, die am 11.10.2026 recherchierten Actions-Vorgaben, der Providervergleich, Token-/Haushaltsbindung, HTTPS-Anforderungen und die Implementierungs-/Testliste stehen in [ChatGPT Actions OAuth](./chatgpt-actions-oauth.md). P5a ist noch nicht freigegeben.

**Entscheidung (11.10.2026):** Keycloak ist der OAuth-/OIDC-Provider. Da er das ASP.NET-Core-Identity-Verzeichnis nicht automatisch übernimmt, verbindet Küchenfuchs Keycloak-Identitäten ausschließlich über die explizite Verknüpfung oben. Küchenfuchs implementiert kein OAuth-Serverprotokoll und keine eigene Token-Kryptografie.

Die ChatGPT-Action nutzt Authorization Code und erhält ein Bearer-Access-Token. Die API bindet ausschließlich das validierte `(iss, sub)` an einen aktiven Küchenfuchs-Benutzer. Sie prüft die aktuelle Mitgliedschaft in dem im Grant/Token festgelegten Haushalt bei jedem Zugriff. E-Mail-Claims und Client-gelieferte IDs sind keine Identitäts- oder Berechtigungsbeweise.

OAuth-/Actions-Bearer-Endpunkte müssen als eigene Bearer-only-Routen bzw. Policies von den bestehenden Cookie-Endpunkten getrennt bleiben. Der zentrale CSRF-Schutz für Cookie-authentifizierte Schreibzugriffe bleibt wirksam und unverändert; er darf weder entfernt noch als Voraussetzung auf reine Bearer-Action-Aufrufe angewandt werden. Actions unterstützen laut OpenAI keine beliebigen Custom Headers, daher ist `X-KF-CSRF` dort keine verfügbare CSRF-Abwehr. Bearer-only bedeutet: nur explizit akzeptiertes `Authorization: Bearer`-Token, kein Rückfall auf Cookie-Authentifizierung. P5b muss jede Mutation zusätzlich durch Scope, Haushaltsprüfung, Rate Limit und explizite Aktionssemantik schützen.

## Authentifizierung und Sicherheit

- PWA: Empfehlung Same-Origin, sichere `HttpOnly`, `Secure`, angemessen `SameSite`-Cookies; **jeder Cookie-authentifizierte schreibende API-Endpunkt** (POST, PUT, PATCH, DELETE und sonstige zustandsändernde Methoden) muss wirksam CSRF-geschützt sein. Mutationen verlangen einen gültigen serverseitig validierten Anti-Forgery-Token oder eine gleichwertige, dokumentierte CSRF-Abwehr; SameSite allein genügt nicht. Neue P3-Endpunkte müssen diesen Schutz und Ablehnung bei fehlendem/ungültigem Token in Integrationstests nachweisen. Keine langlebigen Bearer-Tokens in `localStorage`.
- ChatGPT-Client: separate OAuth-Integration und Scopes; eine angemeldete PWA-Session ist nicht übertragbar. Die OAuth-Autorisierung bindet genau einen Nutzer und nach Möglichkeit einen ausgewählten Haushalt an den Grant; aktuelle Haushaltsmitgliedschaft und Aktivität werden bei jedem Bearer-Aufruf serverseitig geprüft. Details siehe [P5a OAuth-Konzept](./chatgpt-actions-oauth.md).
- Cookie-Endpunkte behalten den zentralen CSRF-Schutz. Spätere Action-Mutationen sind in einer Bearer-only-Gruppe mit ausdrücklich konfigurierten Authentifizierungsschemes und Scopes abzubilden. Cookie- und Bearer-Authentifizierung dürfen nicht über eine unklare Default-Policy gemischt werden.
- Jeder Endpunkt autorisiert Haushaltsmitgliedschaft, Ressourcenzugriff und Rolle serverseitig. IDs sind nicht geheim und erlauben keinen Zugriff.
- Validierung von Payload-Größen, Zeichensatz, Datumsbereichen, Mengen, URI-Längen und Paginierungsgrenzen; Schutz vor Mass Assignment durch DTO-Allowlist.
- Begrenzte Rate Limits für Anmeldung, KI und externe Integrationen. Keine vertraulichen Inhalte in Logs.
- URL-Rezeptimport ist **nicht** Teil des MVP. Vor Aufnahme: nur HTTP(S), DNS/IP-Prüfung gegen private/loopback/link-local/reservierte Netze, Redirects begrenzen und nach jedem Redirect neu validieren, Egress-Beschränkung, Antwortgrößen-/Zeitlimits, Content-Type-Prüfung, Schutz vor DNS-Rebinding. URL nicht einfach an einen unbeschränkten Server-HTTP-Client übergeben.
- CORS nicht wildcard; bei Same-Origin kein breites Cross-Origin-Fenster. TLS-only, sichere Standardheader.
- APIs für Quellen und OpenAI dürfen keine Zugangsdaten an den Browser ausgeben. Externe Anbieterfehler als expliziter Zustand darstellen, nicht als leeres erfolgreiches Ergebnis tarnen.

## Online-PWA und spätere Offline-Erweiterung

Die erste PWA ist installierbar, benötigt für Datenzugriff aber eine Verbindung zur API. Offline-Datenzugriff, lokale Schreibwarteschlange und Konfliktauflösung sind vertagt. Die serverseitige API behält ETags/Concurrency-Schutz gegen gleichzeitig ausgeführte Online-Änderungen, ohne daraus einen Offline-Synchronisationsumfang abzuleiten.
