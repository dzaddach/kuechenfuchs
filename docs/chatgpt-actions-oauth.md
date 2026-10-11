# Küchenfuchs – P5a OAuth/OIDC-Konzept für ChatGPT Actions

**Stand:** 11. Oktober 2026  
**Status:** Provider entschieden: **Keycloak**. P5a.1 und P5a.2 sind gemergt. P5b (Rezept-Actions, siehe [chatgpt-gpt-setup.md](./chatgpt-gpt-setup.md) und [API-Design](./api-design.md#chatgpt-rezept-actions-p5b-umgesetzt)) ist auf `feature/p5b-chatgpt-recipes` umgesetzt. Nicht live: DNS/HTTPS auf dem Heimserver und der öffentliche OAuth-Ende-zu-Ende-Test mit einem echten GPT sind der ausstehende Abnahmeschritt.

## Ziel und Abgrenzung

P5a soll eine bestehende OAuth-2.0-/OIDC-Lösung sicher mit den vorhandenen ASP.NET-Core-Identity-Benutzern verbinden. Es umfasst die Autorisierungscode-Anmeldung, Tokenausgabe und -prüfung, Benutzer-/Haushaltsbindung, Widerruf sowie einen isolierten HTTPS-Test. P5a erstellt noch kein GPT, keine Action-OpenAPI-Beschreibung und keine Rezeptaktionen. Diese gehören zu P5b.

Die PWA behält ihren bestehenden Same-Origin-Login mit ASP.NET Core Identity, HttpOnly-Cookie und CSRF-Schutz. P5a ersetzt, lockert oder teilt dieses Cookie nicht mit ChatGPT. OAuth-Bearer-Tokens sind ausschließlich für API-Endpunkte bestimmt, die ausdrücklich eine Bearer-Policy verlangen.

## P5a.1 – Umsetzungsstand Keycloak (lokal)

Keycloak wurde als lizenzkostenfreier Open-Source-Provider gewählt, damit Küchenfuchs später auch von weiteren Haushalten genutzt werden kann. Die frühere Duende-Empfehlung unten ist damit überholt und nur noch als Entscheidungshistorie enthalten.

| Bereich | Umsetzung in P5a.1 |
|---|---|
| Laufzeit | `compose.keycloak.yaml` ergänzt `compose.yaml` um `keycloak` und `keycloak-db`. Image aus `deploy/keycloak/Dockerfile`: `quay.io/keycloak/keycloak:26.7.5` mit festem Digest, vorab per `kc.sh build` optimiert (`start --optimized`). |
| Datenbank | Eigene PostgreSQL-17-Instanz `keycloak-db` mit Datenbank/Benutzer `keycloak`, Volume `keycloak_postgres_data`, nur im internen Netz `keycloak-backend`. Die API ist nicht in diesem Netz; die Küchenfuchs-Datenbank bleibt getrennt. |
| Exposition | Keycloak nur an `127.0.0.1:${KEYCLOAK_PORT}`; beide PostgreSQL-Instanzen ohne Host-Port. Keine öffentliche Domain. |
| Realm | `deploy/keycloak/import/kuechenfuchs-realm.json` (Import nur, wenn der Realm noch nicht existiert): keine Benutzer, keine Selbstregistrierung, kein Login per E-Mail, Brute-Force-Schutz, Access-Token-Laufzeit 300 s, nur `basic`-Claims (kein E-Mail-/Profilclaim). Client-Secrets und Link-Redirect kommen als Platzhalter aus Pflicht-Umgebungsvariablen. |
| Clients | `kuechenfuchs-link`: vertraulicher Authorization-Code-Client mit erzwungenem PKCE S256 und exakt einer Redirect-URI; seine Tokens werden von der API **nicht** als Bearer akzeptiert. `chatgpt-actions`: vertraulicher Authorization-Code-Client, Audience `kuechenfuchs-api`, optionale Scopes `recipes:read`/`recipes:write`, **keine Redirect-URIs** bis zur Freigabe der GPT-ID (P5a.2). |
| Verknüpfung | `ExternalIdentityLink` bindet `(Issuer, Subject)` an genau einen `ApplicationUser` und genau einen bewusst gewählten Haushalt. Start nur aus angemeldeter PWA-Sitzung mit CSRF-Token; `state`, `nonce`, PKCE-Verifier und Browser-Bindungs-Cookie pro Versuch; Keycloak-Anmeldung mit `prompt=login`; Code-Tausch ausschließlich serverseitig; Anfrage wird vor allen weiteren Prüfungen atomar verbraucht. Keine E-Mail-basierte Zuordnung. |
| Bearer | Eigenes Schema `KeycloakBearer` (nicht Default). Prüft RS256-Signatur über Keycloak-JWKS, exakten Issuer, Audience `kuechenfuchs-api`, `exp`/`nbf`, `typ=Bearer`, erlaubten Client (`azp`), aktive Verknüpfung, `IsEnabled`/Sperre und pro Endpunkt Scope sowie gebundenen Haushalt inklusive aktueller Mitgliedschaft. |
| Widerruf | `DELETE /api/v1/integrations/keycloak/links/{linkId}` setzt `RevokedAt`; da jede Bearer-Anfrage die aktive Verknüpfung in der Küchenfuchs-Datenbank prüft, wirkt der Widerruf sofort, auch für noch gültige Tokens. Über `TokensNotBefore` werden solche Tokens auch nach einer späteren Neuverknüpfung nie wieder gültig. |

### Bewusste Abweichungen vom Konzept

- **Haushaltsbindung pro Verknüpfung statt pro Grant:** Keycloak kennt Küchenfuchs-Haushalte nicht. Ohne eigene Keycloak-Erweiterung ist die Verknüpfung der Ort, an dem der Haushalt ausdrücklich gewählt wird. Ein Wechsel erfordert Widerruf und neue Verknüpfung. Damit gilt: eine aktive Keycloak-Identität ↔ ein Benutzer ↔ ein Haushalt.
- **Selbsttragende JWT statt Introspection:** Sofortiger Widerruf wird über die Datenbankprüfung der Verknüpfung erreicht (fail closed bei Datenbankfehler). Seit P5a.2 beendet die API zusätzlich beim Trennen und vor jeder neuen Verknüpfung alle Keycloak-Sitzungen der Identität, sodass auch Refresh-Tokens unbrauchbar werden.
- **Token-Generation je Verknüpfung:** Jede Verknüpfung speichert `TokensNotBefore` = Beginn der nächsten vollen Sekunde nach dem Anlegen + `Keycloak:ClockSkewSeconds` (Standard 30 s). Akzeptiert werden nur Access-Tokens mit `iat >= TokensNotBefore`; Tokens ohne `iat` werden abgelehnt. Damit bleiben Tokens, die vor einem Widerruf, zwischen Widerruf und Neuverknüpfung oder vor der ersten Verknüpfung ausgestellt wurden, auch nach einer erneuten Verknüpfung derselben Keycloak-Identität dauerhaft ungültig. Die Rundung berücksichtigt die Sekundenauflösung von `iat`, der Zuschlag eine vorgehende Keycloak-Uhr. Folge: Erst Tokens, die nach diesem kurzen Zeitfenster (Standard rund 31 s) ausgestellt werden, funktionieren; früher geholte Tokens werden abgelehnt (fail closed), der Client holt einfach ein neues.
- **HTTP nur intern:** Issuer und Link-Redirect müssen außerhalb von `Development` HTTPS sein. Discovery und Admin-API dürfen mit `Keycloak:RequireHttpsMetadata=false` per HTTP nur interne Compose-Dienstnamen ohne Punkt ansprechen (z. B. `http://keycloak:8080/auth`).

## P5a.2 – ChatGPT-OAuth-Vorbereitung und Heimserver (umgesetzt, nicht live)

| Bereich | Umsetzung |
|---|---|
| Same Origin | Keycloak mit `http-relative-path=/auth` hinter dem Küchenfuchs-Caddy; öffentlich nur `/auth/realms/kuechenfuchs/*` und `/auth/resources/*`. Issuer `${KUECHENFUCHS_PUBLIC_URL}/auth/realms/kuechenfuchs` (Heimserver: `https://kuechenfuchs.zaddach.org/auth/realms/kuechenfuchs`). Admin-Konsole über `KC_HOSTNAME_ADMIN` nur auf einem Loopback-Port. |
| Heimserver | `compose.homeserver.yaml` (Projekt `kuechenfuchs`, Production, `migrate`-Dienst, Restart-Policies, Forwarded-Header nur aus privaten Netzen); Caddy-Snippet `deploy/caddy/edge/kuechenfuchs.caddy` für den bestehenden Heimserver-Caddy. Anleitung: [homeserver-setup.md](./homeserver-setup.md). |
| ChatGPT-Client | `chatgpt-actions`: vertraulich, nur Authorization Code, `client_secret_basic` und `client_secret_post` möglich, keine Redirect-URIs bis zur bestätigten GPT-ID, Refresh-Rotation, kein `offline_access`, keine E-Mail-/Profil-Claims. Pflege über `scripts/keycloak-chatgpt-client.sh` (nur exakte `https://chat.openai.com|chatgpt.com/aip/g-…/oauth/callback`, PKCE-Schalter, Secret-Rotation). |
| Widerruf | Trennen: Verknüpfung sofort widerrufen, danach Keycloak-Sitzungen über den Service-Account `kuechenfuchs-api-admin` beenden (Refresh-Tokens ungültig, Ergebnis `providerSessionsEnded`). Vor jeder neuen Verknüpfung ebenfalls alle Sitzungen beenden; scheitert das, wird nicht verknüpft. |
| Nachweis der Autorisierung | Die PWA zeigt „ChatGPT autorisiert“ nur, wenn Keycloak eine Sitzung des Clients `chatgpt-actions` meldet; zusätzlich der letzte erfolgreiche Bearer-Zugriff. |
| Rate Limit | 60 Bearer-Anfragen pro Minute je Verknüpfung (`Keycloak:BearerRequestsPerMinute`), `429` mit `Retry-After`. |

### Interoperabilität: geprüft vs. noch live zu prüfen

| Punkt | Automatisiert geprüft (gegen Keycloak 26.7.5) | Live mit ChatGPT zu prüfen |
|---|---|---|
| Client-Authentifizierung am Token-Endpunkt | `client_secret_post` und `client_secret_basic` funktionieren, falsches Secret `401` | Welche Methode ChatGPT tatsächlich sendet (OpenAI-Doku nennt sie nicht; Keycloak-Log prüfen) |
| PKCE | ohne PKCE funktioniert; mit S256 wird falscher/fehlender Verifier abgelehnt; Pflicht-PKCE per Skript lehnt Anfragen ohne Challenge ab | Ob ChatGPT `code_challenge` sendet → danach `pkce required` (D21) |
| `state` | wird unverändert (auch mit Sonderzeichen) zurückgegeben | – |
| Redirect-URIs | nicht registrierte/abgewandelte URIs `400` ohne Weiterleitung; Skript lehnt Wildcards, HTTP und fremde Hosts ab | Exakte von ChatGPT angezeigte Callback-URIs mit GPT-ID |
| Tokenantwort | `access_token`, `token_type: Bearer`, `expires_in: 300`, `refresh_token` | Erneuerung durch ChatGPT nach Ablauf |
| Refresh | Rotation; Wiederverwendung des alten Refresh-Tokens `invalid_grant`; nach Trennen/Neuverknüpfung `invalid_grant` | Verhalten nach Keycloak-Sitzungs-Idle (30 min, D20) |
| Scopes | unbekannte Scopes und `offline_access` werden nicht gewährt; ohne `recipes:read` `403` | Scope-Feld im GPT-Editor (`openid recipes:read`) |
| Domain/TLS | Discovery und alle Endpunkte unter derselben HTTPS-Origin (Test-CA) | Öffentliches Zertifikat, TLS 1.2+, Port 443, 45-s-Timeout |

### Weiterhin offen

DNS/HTTPS auf dem Heimserver, Einbau des Caddy-Snippets, GPT-ID/Callback-URIs, Live-Interoperabilitätstest, Entscheidungen D20–D23 (Verbindungsdauer, PKCE-Pflicht, Caddy-Anbindung, Keycloak-Benutzerverwaltung) sowie die Rezept-Actions (P5b).

## Verifizierte Anforderungen von ChatGPT Actions

Offizielle OpenAI-Dokumentation wurde am **11.10.2026** abgerufen. Sie kann sich ändern und muss unmittelbar vor einem Live-Test erneut geprüft werden.

| Bereich | Dokumentierter Stand | Konsequenz für Küchenfuchs |
|---|---|---|
| Authentifizierung | OAuth-Einrichtung verlangt Client-ID, Client-Secret, Authorization-URL, Token-URL und Scope. Der gezeigte Austausch verwendet `grant_type=authorization_code`, `code`, `redirect_uri`, Client-ID und -Secret. | Confidential Client mit Authorization Code; den von der Actions-Oberfläche erwarteten Client-Authentifizierungsmodus (insbesondere `client_secret_post`) praktisch testen. Kein Implicit- oder Password-Grant. |
| Callback und `state` | Die Action-Anleitung verlangt `state`; sie nennt `https://chat.openai.com/aip/{GPT-ID}/oauth/callback` und `https://chatgpt.com/aip/{GPT-ID}/oauth/callback` als gültige Callback-Formen. | `state` vom Client unverändert in der Autorisierungsantwort zurückgeben; pro GPT nur exakte, registrierte Redirect-URIs verwenden. Keine Callback-Wildcards. |
| Token | Actions erwartet mindestens `access_token`, `token_type: Bearer` und `expires_in`; ein `refresh_token` ist optional und kann von ChatGPT zur Erneuerung verwendet werden. Das Nutzer-Token kommt bei API-Aufrufen im `Authorization`-Header. | Kurze Access-Token-Laufzeit, Refresh-Rotation und Revokation vorsehen; API prüft ausschließlich Access-Tokens, niemals ID-Tokens. Refresh-Verhalten gegen die Plattform testen. |
| Netzwerk/TLS | Der Production Guide verlangt öffentlich gültiges TLS 1.2 oder neuer auf Port 443. Requests haben ein 45-Sekunden-Roundtrip-Limit. | Lokales `http://localhost:8080` ist kein Actions-Testendpunkt. Für den Ende-zu-Ende-Test ist eine freigegebene öffentliche Testdomain erforderlich. |
| Domains/Transport | OAuth-URLs müssen grundsätzlich dieselbe Domain wie die Action-API verwenden; dokumentierte Ausnahmen sind Google-, Microsoft- und Adobe-OAuth-Domains. Action-Aufrufe unterstützen keine benutzerdefinierten Header. Payloads müssen unter 100.000 Zeichen bleiben. | OAuth-Aussteller, Authorization-/Token-Endpunkte und API hinter derselben öffentlichen Küchenfuchs-Origin betreiben. Kein CSRF-Custom-Header für Bearer-Actions voraussetzen. |
| Betrieb | OpenAI empfiehlt Rate Limits; `429` und `500` werden berücksichtigt. | Niedrige, benutzerbezogene Limits und nachvollziehbare `429`-Antworten; kein prompt- oder tokenhaltiges Logging. |

Die Plattform dokumentiert den Authorization-Code-Austausch und `state`, aber nicht abschließend alle Details zu PKCE, Client-Authentifizierung, Refresh-Rotation oder Anbieter-spezifischen Randfällen. Diese Punkte gelten als **P5a-Kompatibilitätstests**, nicht als ungeprüfte Annahmen.

## Anbieterentscheidung

*Entscheidungshistorie (vor dem 11.10.2026): Die folgende Abwägung führte zunächst zu Duende; der Auftraggeber hat sich für Keycloak entschieden (siehe P5a.1).*

### Empfehlung: zuerst Duende IdentityServer evaluieren

Duende IdentityServer ist ein etabliertes OAuth-/OIDC-Produkt für ASP.NET Core. Im Gegensatz zu einem separaten Provider kann es im vorhandenen API-Prozess dieselbe Identity-Benutzerdatenbank und denselben stabilen Benutzerbezeichner verwenden. Dadurch entfallen ein zweites Benutzerverzeichnis, Passwortsynchronisierung und ein separater öffentlich sichtbarer Providerhost. Die PWA-Cookie-Anmeldung bleibt dabei unverändert; Autorisierungsserver, Endpunkte, Client und Cookie-/Login-Interaktion müssen jedoch zusätzlich eingerichtet und getestet werden.

Das ist eine bedingte Empfehlung, keine Providerfreigabe: Duende verlangt für Produktionsnutzung grundsätzlich eine Lizenz; offizielle Angaben erwähnen eine kostenlose Community Edition für qualifizierende Organisationen und persönliche Projekte. Ob die konkrete private Küchenfuchs-Bereitstellung darunter fällt, muss vor Paketstart schriftlich geklärt werden. Zusätzlich muss die Actions-Client-Kompatibilität praktisch belegt werden. Duende ist ein fertiges Protokollprodukt, nicht selbst geschriebene OAuth-Kryptografie oder ein eigener Protokollserver.

### Keycloak: technisch geeignet, aber nicht die einfachste Passung

Keycloak ist ein etablierter, selbst betreibbarer OIDC-Provider mit Authorization-Code-Flows, Clients, Sessions, Scopes/Rollen und Token-Endpunkten. Für den bestehenden Küchenfuchs-Bestand ist er jedoch **kein Plug-in für ASP.NET Core Identity**: Die vorhandene Identity-Datenbank wird nicht automatisch zu einem Keycloak-Benutzerverzeichnis. Eine sichere Zuordnung benötigt entweder ein explizites Linking-Verfahren oder eine zusätzliche Föderationsintegration. Gleiches gilt für Kontosperrung und Haushaltsmitgliedschaften.

Ein eigener Keycloak-Hostname für Issuer und OAuth-Endpunkte kollidiert außerdem mit der dokumentierten Same-Domain-Vorgabe von Actions. Er müsste über dieselbe öffentliche Origin (z. B. Caddy-Pfad-Routing) veröffentlicht werden, mit korrektem externem Issuer, Forwarded-Headern, Hostname-Konfiguration und eigenem Backup-/Updatebetrieb. Kein PWA-Login-Cookie darf an Keycloak weitergegeben werden.

**Fazit:** Keycloak ist ein belastbarer Fallback, wenn Duende-Lizenzierung oder Kompatibilität scheitert und der Auftraggeber die getrennte Identitätsverknüpfung samt Zusatzbetrieb akzeptiert. Es ist nicht die bevorzugte Erstwahl für den aktuellen Identity-Bestand.

### Nicht empfohlene Abkürzungen

- Die ASP.NET-Core-Identity-API für SPAs ist für Cookie-geschützte APIs gedacht; ihre optionalen Bearer-Tokens sind kein Ersatz für einen OAuth-Authorization-Server mit Actions-kompatiblen Authorization-/Token-Endpunkten.
- OpenIddict ist eine etablierte .NET-Bibliothek, aber kein fertig betriebener Identity Provider. Sein Einsatz würde weiterhin die Implementierung und den Betrieb von Autorisierungsserver-Verhalten im Küchenfuchs-Code bedeuten und ist ohne geänderte Freigabe ausgeschlossen.
- Ein gehosteter Provider wie Auth0 könnte den Betrieb abgeben, führt aber ebenfalls ein zweites Benutzerverzeichnis, externe Verarbeitung, Anbieter-/Kostenbindung und Account-Linking ein. Er ist nicht ohne Entscheidung über Datenschutz, Kosten und Konten einfacher.

## Benutzeridentität und Zuordnung

### Bevorzugter Weg mit gemeinsamem ASP.NET-Identity-Benutzerbestand

1. Der Actions-Authorization-Code-Client wird nach erfolgreicher Anmeldung einem bestehenden `ApplicationUser` zugeordnet. Es werden keine neuen Benutzer per E-Mail-Match angelegt.
2. Der Access-Token enthält als `sub` ausschließlich die unveränderliche ASP.NET-Identity-User-ID (GUID). E-Mail, Anzeigename und vom Client gelieferte `userId` sind keine Autorisierungsgrundlage.
3. Die API akzeptiert nur den exakt konfigurierten `iss`, die richtige API-`aud`, gültige Zeitbedingungen und den erwarteten Tokenstatus. `sub` muss in `ApplicationUser.Id` auflösbar sein; `IsEnabled == false`, gelöschte oder unbekannte Benutzer werden abgewiesen.
4. Niemals Benutzer automatisch per E-Mail-Claim verknüpfen. E-Mail-Adressen können geändert, wiedervergeben oder ungeprüft sein.

Damit ist die vertrauenswürdige Zuordnung `(iss, sub) -> ApplicationUser.Id` eindeutig und in der vorhandenen Identity-Datenbank verankert. Ein separates Mappingmodell ist für diesen bevorzugten, gemeinsamen Benutzerbestand zunächst nicht notwendig.

### Fallback bei einem getrennten Provider (z. B. Keycloak)

Dann ist eine Tabelle `ExternalIdentityLink` vorzusehen:

- eindeutiger Schlüssel `(Issuer, Subject)`, Fremdschlüssel auf `ApplicationUser.Id`, Erstellungs-/Widerrufszeit;
- keine Zuordnung anhand der E-Mail-Adresse und keine vom Browser übermittelte interne User-ID als Beweis;
- Linking nur aus einer bereits authentifizierten PWA-Sitzung: einmaliger zufälliger Link-Code, serverseitig nur gehasht gespeichert, an internen Benutzer gebunden, kurze Gültigkeit und atomar genau einmal verwendbar;
- separater externer Authorization-Code-Flow mit festem `issuer`, Client-ID/Audience, exakter Redirect-URI, `state`, `nonce` und PKCE S256, sofern der gewählte Client-Flow es unterstützt;
- nach erfolgreicher OIDC-Prüfung wird `(iss, sub)` atomar an den gebundenen User geknüpft; Konflikte/Mehrfachzuordnungen werden abgelehnt und auditierbar gemacht;
- E-Mail darf nur als angezeigter Hinweis dienen. Re-Linking erfordert einen neuen expliziten, authentifizierten Vorgang.

Dieser Fallback darf nicht in P5a „nebenbei“ entstehen: er ist eine eigene Implementierungs- und Reviewgrenze.

## Haushaltsbindung und Berechtigungen

- Vor dem Autorisieren zeigt Küchenfuchs die Haushalte aus `HouseholdMembers`, in denen der angemeldete, aktive Benutzer Mitglied ist. Bei mehreren Haushalten muss genau ein Zielhaushalt bewusst ausgewählt werden; ein fremder, frei übermittelter `householdId`-Wert wird nicht übernommen.
- Der Provider bindet die ausgewählte `household_id` als serverseitig erzeugten Token-Claim an Authorization Grant und Access-Token. Die Haushalts-ID ist kein geheimer Schlüssel und berechtigt allein zu nichts.
- Jeder Bearer-Endpunkt prüft pro Anfrage: gültige Token-Identität, aktiven Benutzer, `household_id`-Claim gleich der Route, aktuelle Mitgliedschaft in `HouseholdMembers` und benötigten Scope. Fehlende/fremde Ressource wird wie im Cookie-Pfad mit `404` verborgen.
- Mitgliedschaft wird nicht als dauerhaft gültiger Rollen-Claim in ein langes Token kopiert. Entfernte Mitgliedschaft sperrt Zugriffe unmittelbar durch den Datenbankcheck, auch wenn ein Token noch nicht abgelaufen ist.
- Minimaler Startumfang: `recipes:read` und nur nach separater Zustimmung `recipes:write`. `household:manage`, Mitgliederverwaltung, Administratorrechte und globale Haushaltsauswahl werden nicht als Actions-Scope ausgegeben. Ein späterer KI-Aufruf benötigt zusätzlich `recipes:draft`, Kostenlimits und die bestehende externe Verarbeitungsbestätigung.
- Die API schreibt Audit-Metadaten (interne User-ID, ausgewählter Haushalt, Scope, Endpunkt/Ergebnis, Zeitpunkt, Request-ID), aber keine Tokens, Secrets, Prompts oder Rezeptvolltexte.

## Tokenprüfung, Ablauf und Widerruf

### Verbindliche Prüfungen am Resource Server

- Eigene, fest konfigurierte Discovery-Metadaten/Issuer-URL; keine dynamische `iss`- oder JWKS-URL aus dem Token übernehmen.
- Signatur/zulässiger Algorithmus beziehungsweise Provider-Introspection, exakter `iss`, API-`aud`, Ablauf `exp`, `nbf`, erforderlicher Bearer-Token-Typ, Scope und Clientkontext prüfen.
- Keine Annahme von ID-Tokens, Tokens mit falschem Audience-Wert, unaufgelösten `sub`-Werten, unbekannten Scopes oder Haushalts-IDs aus Anfragekörpern.
- Secrets und Schlüsselmaterial nur backendseitig als Runtime-Secrets; keine Token-/Authorization-Header im Log. HTTPS auch zwischen Proxy und API nur entsprechend dem festgelegten internen Netz-/Threat-Modell.

### Bevorzugte Widerrufsstrategie

Für den ChatGPT-Client wird **referenzbasierter/undurchsichtiger Access Token mit serverseitiger Introspection pro API-Anfrage** bevorzugt. Bei der geringen erwarteten Last ist der zusätzliche Aufruf/Lookup vertretbar und vermeidet, dass ein widerrufener selbsttragender JWT bis zu seinem Ablauf weiter akzeptiert wird. Der Introspection-Aufruf bleibt intern und authentifiziert; ist der Provider nicht erreichbar, wird der Request geschlossen abgewiesen (fail closed).

Refresh-Tokens sind optional, werden aber – falls ChatGPT sie im aktuellen Flow tatsächlich nutzt – pro Rotation ersetzt; Wiederverwendung eines alten Tokens widerruft die zugehörige Grant-/Refresh-Token-Familie. Eine explizite „ChatGPT-Verbindung trennen“-Aktion widerruft den Grant serverseitig. Ein `POST`-Revocation-Endpunkt nach RFC 7009 wird vom Actions-Client nicht vorausgesetzt; UI-/Admin-Widerruf muss unabhängig davon funktionieren.

Zusätzlich prüft die API pro Request den aktiven Benutzer und die aktuelle Haushaltmitgliedschaft. Damit wirken Benutzersperre und Mitgliedschaftsentzug auch dann, wenn Provider-Caches vorhanden sind. Provider-Widerruf und API-Berechtigungsprüfung sind beide erforderlich. Falls der ausgewählte Provider keine kompatible Introspection/reference-token-Konfiguration bietet, ist als alternative SLA maximal fünf Minuten Restgültigkeit für Access-Tokens festzulegen und vom Auftraggeber zu akzeptieren; Widerruf ist dann nicht augenblicklich.

## HTTPS- und Testumgebung

1. Vor P5a muss der Auftraggeber eine kontrollierte öffentliche Testdomain, DNS-Verantwortung und Freigabe für eingehende Ports bereitstellen. Kein öffentliches Testsystem wird durch diese Planung erstellt.
2. Ein isolierter Staging-Stack bekommt getrennte Datenbank, Testnutzer, Provider-/Signing-Secrets und ausschließlich synthetische Rezepte. Keine Produktionsdaten oder produktiven Schlüssel.
3. Caddy nimmt öffentlich HTTPS auf Port 443 mit öffentlich vertrauenswürdigem Zertifikat und TLS 1.2+ an. HTTP kann ausschließlich auf HTTPS umleiten. Nur Caddy ist öffentlich; PostgreSQL, API und gegebenenfalls Keycloak sind intern und ohne Host-Port.
4. Action-API, Authorization-, Token-, Discovery- und – bei Wahl Keycloak – externe Provider-URL erscheinen unter derselben Origin. Provider-Pfad-Routing nur verwenden, wenn Discovery-`issuer`, Redirects, Hostname und Forwarded Headers Ende-zu-Ende stimmen.
5. ASP.NET vertraut Forwarded Headers ausschließlich vom Compose-/Proxy-Adressbereich. Der externe Issuer wird aus einer festen HTTPS-Konfiguration gebildet, nie aus beliebigen `Host`-/`X-Forwarded-*`-Werten.
6. Test-GPT registriert nur seine exakten von OpenAI angezeigten Callback-URIs für `chat.openai.com`/`chatgpt.com`; Änderungen werden mit GPT-ID nachgetragen. Keine Wildcards und kein Rückfall auf HTTP.
7. Rate Limits, restriktive Payloadlimits, Security-Header, minimale Antwortdaten, Secret-Rotation und Alarmierung für wiederholte fehlgeschlagene Logins gehören zum Testaufbau. Kein ungeschützter Tunnel.

## P5a-Implementierungsplan nach Freigabe

### Betroffene Dateien und Verantwortlichkeiten

| Datei / Bereich | Geplante Änderung |
|---|---|
| `backend/Kuechenfuchs.Api/Program.cs` | Bestehende Identity-Cookie-/CSRF-Konfiguration unverändert erhalten; OAuth-Server, getrennte OAuth-Interaktionssitzung und explizite Bearer-Policies registrieren. CSRF muss Cookie-Endpunkten zugeordnet bleiben, nicht pauschal OAuth-Bearer-Schreibanfragen blockieren. |
| `backend/Kuechenfuchs.Api/Kuechenfuchs.Api.csproj` | Nur nach Provider-/Lizenzfreigabe ein etabliertes Duende-Paket ergänzen; bei Keycloak-Fallback stattdessen nur erforderlichen Standard-OIDC/JWT-Validierungsclient, keinen OAuth-Servercode. |
| `backend/Kuechenfuchs.Api/Identity/ChatGptAuthorizationEndpoints.cs` (neu) | Authorization/Login-/Haushaltsauswahl-Interaktion und Widerruf/Verbindungsverwaltung; Anti-CSRF/State des OAuth-Flow korrekt erhalten. Der bestehende `/api/auth/login`-PWA-Vertrag bleibt eigenständig. |
| `backend/Kuechenfuchs.Api/Identity/ChatGptClaimsService.cs` (neu) | Nur internen `sub`, gewählten Haushalt und freigegebene Scopes aus vertrauenswürdigen DB-Daten erzeugen; keine E-Mail-Verknüpfung. |
| `backend/Kuechenfuchs.Api/Identity/ExternalIdentityLink.cs` (nur Keycloak-/getrennter Provider-Fallback) | Eindeutige `(issuer, subject)`-Zuordnung mit one-time Linking; für integriertes Duende-Modell zunächst nicht anlegen. |
| `backend/Kuechenfuchs.Api/Data/KuechenfuchsDbContext.cs` und `Data/Migrations/*` | Nur benötigte Provider-Grant-/Persisted-Grant-Tabellen beziehungsweise optionales ExternalIdentityLink versionieren. Bestehende Benutzerdaten und PWA-Cookie bleiben unverändert. |
| `backend/Kuechenfuchs.Api/Identity/HouseholdMember.cs`, `Identity/ApplicationUser.cs` | Keine Änderung am Haushaltsmodell voraussetzen; pro Zugriff aktuelle `IsEnabled`- und Mitgliedschaftsprüfung wiederverwenden. |
| `backend/Kuechenfuchs.Api/Program.cs` und neue `Identity/ChatGptAuthorizationPolicies.cs` | Bearer-Only-Scheme/Policy mit festem Audience-/Issuer-Vertrauen und scopes. Bestehende Cookie-API-Gruppe bleibt Cookie-geschützt; kein stiller Wechsel der Default-Authentifizierung. |
| `backend/Kuechenfuchs.Api.Tests/*OAuth*` (neu) | Authorization-Code-/Token-/Introspection- und Policy-Integrationstests ohne externe ChatGPT- oder kostenpflichtige Provideraufrufe. |
| `compose.p5a-test.yaml`, `scripts/test-p5a-oauth.sh` (neu) | Isolierter Teststack/Smoke für OIDC-Metadaten, TLS-Konfiguration, PWA-Cookie-Regressions- und Bearer-Policies; keine Veröffentlichung im Standard-Compose. |
| `docs/deployment.md`, `docs/api-design.md`, `docs/ai-integration.md` | Tatsächliche Issuer-/Client-/Redirect-/Secret-Konfiguration und ausgelieferte Verträge nach Entscheidung dokumentieren; keine Secrets versionieren. |

### Endpunkte und Verträge

| Methode / Pfad | Verantwortung in P5a |
|---|---|
| `GET /.well-known/openid-configuration` | Provider-Discovery; festes HTTPS-Issuer, Authorization-/Token-/JWKS-Metadaten |
| `GET /connect/authorize` | Authorization Code, exakter registrierter `redirect_uri`, `state`, Scope, `code_challenge`; aktiver Küchenfuchs-Login und bewusste Haushaltsauswahl |
| `POST /connect/token` | Authorization-Code-Austausch; confidential Action-Client, exakte Redirect-Übereinstimmung, kurzlebiges Access-Token und optional rotierendes Refresh-Token |
| `POST /connect/revocation` | Provider-seitiger Token-/Grant-Widerruf nach RFC 7009; nie als Ersatz für API-Introspection behandeln |
| `POST /connect/introspect` | Nur authentifizierter Resource-Server-Aufruf; nicht als anonyme öffentliche Token-Validierungs-API verwenden |
| `GET /api/v1/integrations/chatgpt/households/{householdId}/authorization-check` (P5a.1 umgesetzt, nur Nachweis) | Bearer-only-Testressource ohne persönliche Daten; bestätigt aktive Identität, Scope und aktuellen Zugriff auf genau den Haushalt. P5b kann sie entfernen oder ausschließlich als Diagnose nutzen. |

P5a erzeugt keine eigentlichen Rezept-Actions. P5b fügt separate Bearer-only-Rezeptoperationen hinzu. Vor P5b sind `GET /api/v1/households/{hid}/recipes`, `POST .../ai/recipe-drafts` und ein expliziter bestätigter Speichervorgang weiterhin ausschließlich nach ihrem aktuell dokumentierten Cookie-/CSRF-Vertrag verfügbar.

### Tests und Akzeptanzkriterien

1. Authorization Code mit gültigem Client/Redirect/`state` und PKCE S256, sofern Actions diesen Ablauf sendet, erfolgreich; falscher Client-Secret-Modus oder nicht unterstützter Parameter wird vor öffentlichem Test als Blocker dokumentiert.
2. Nicht registrierte, abweichende oder HTTP-Redirect-URIs werden abgelehnt; keine Wildcard.
3. Fehlender/falscher `state`, wiederverwendeter Code, falscher `code_verifier`, ungültiger/abgelaufener/revokierter Token, falsches `iss`/`aud`, manipulierte Signatur und unbekannter Scope werden abgelehnt.
4. Token ist nur für einen aktiven internen Benutzer, einen ausgewählten Haushalt und gewählte minimale Scopes gültig. Freie Route-ID, Fremdhaushalt, entfernte Mitgliedschaft, deaktivierter Benutzer sowie gelöschter Link führen zu verweigertem Zugriff.
5. Keine Cookies oder CSRF-Custom-Header sind für Bearer-Probeaufrufe nötig. PWA-Login, PWA-Rezeptzugriff, Cookieflags, Logout und CSRF-Ablehnung für Cookie-Schreibzugriffe bestehen unverändert die P0–P4-Regressionstests.
6. Provider-Introspection-Ausfall verweigert Bearer-Zugriff; Verbindungswiderruf stoppt Folgeaufrufe; Refresh-Token-Rotation/Wiederverwendung wird getestet.
7. Staging verwendet öffentlich gültiges HTTPS/TLS 1.2+, Port 443 und dieselbe Origin für Action-/OAuth-URLs; PostgreSQL und API sind nicht direkt öffentlich erreichbar.
8. Logs enthalten weder Client-Secret, Code, Access-/Refresh-Token, Authorization-Header, Loginpasswort, vollständige PII noch Chat-Inhalte.
9. Keine echten ChatGPT-Aufrufe, Produktionsaccounts oder produktive Daten in automatisierten Tests. Die abschließende OAuth-Interoperabilität wird erst mit freigegebenem Testendpunkt und Test-GPT manuell verifiziert.

## Offene Entscheidungen und benötigte Angaben

Vor Implementierungsfreigabe werden benötigt:

1. **Provider und Lizenz:** Bestätigung, ob Duende für die konkrete private Produktionsnutzung lizenziert/zulässig ist und das Budget dafür freigegeben wird. Falls nein: ausdrückliche Auswahl Keycloak oder gehosteter Alternative sowie Zustimmung zum zusätzlichen Linking-/Betriebsmodell.
2. **ChatGPT Client:** Zugriff auf Test-GPT, seine GPT-ID und die von der aktuellen Actions-OAuth-Oberfläche exakt angezeigten Redirect-URIs, Client-Authentifizierung und Refresh-/PKCE-Verhalten. Client-Secret wird über einen sicheren Kanal gesetzt, nicht in Git/Chat dokumentiert.
3. **Testbetrieb:** freigegebene DNS-Domain, TLS-/Firewall-Verantwortlicher, öffentlich erreichbare Ports und zugelassene Testnutzer/-Haushalte. Entscheidung, ob Staging völlig synthetische Daten verwendet (empfohlen: ja).
4. **Delegation:** Soll ein Actions-Grant genau einen Haushalt umfassen (empfohlen) oder alle aktuellen Mitgliedschaften des Benutzers? Soll ein eigener `recipes:draft`-Scope KI-Kosten auslösen dürfen?
5. **Widerrufs-SLA:** Sofortiger Widerruf per Referenz-Token/Introspection (empfohlen) oder ein vom Auftraggeber akzeptierter kurzer JWT-Restlaufzeit-Zeitraum.
6. **Nutzerinformation:** Freigabe der Login-/Consent-Texte und Datenschutzinformation für die Übergabe von ChatGPT-Konversationsausschnitten an Küchenfuchs; Actions-Aufrufe können Teile einer Unterhaltung enthalten.

Bis diese Punkte entschieden und P5a ausdrücklich freigegeben ist, bleiben Provider, Test-URLs, Ports, Pakete und Secrets unverändert; es werden weder Provider installiert noch Dienste öffentlich gemacht.

## Quellen (abgerufen 11.10.2026)

- OpenAI, [GPT Action authentication](https://developers.openai.com/api/docs/actions/authentication) — OAuth-Parameter, Authorization-Code-Beispiel, Tokenantwort, `state`, Redirect-URI.
- OpenAI, [Production notes on GPT Actions](https://developers.openai.com/api/docs/actions/production) — TLS 1.2+ / Port 443, 45-Sekunden-Timeout, Domainregeln, fehlende Custom Headers, Limits und Rate-Limit-Empfehlungen.
- IETF, [RFC 9700: Best Current Practice for OAuth 2.0 Security](https://www.rfc-editor.org/rfc/rfc9700.html) — aktuelle Sicherheitspraktiken für Clients und Authorization Server, Redirects, Authorization Code und PKCE.
- IETF, [RFC 7009: OAuth 2.0 Token Revocation](https://datatracker.ietf.org/doc/html/rfc7009) — Widerrufs-Endpunkt und Widerruf von Tokens eines Grants.
- OpenID Foundation, [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html) — Identitätsclaims und Authentifizierungsfluss; `(iss, sub)` als externe Identität.
- Duende, [IdentityServer](https://docs.duendesoftware.com/identityserver/), [ASP.NET Identity integration](https://docs.duendesoftware.com/identityserver/identity/aspnet-identity/) und [Licensing](https://docs.duendesoftware.com/general/licensing/) — .NET OAuth/OIDC-Produkt, direkte ASP.NET-Core-Identity-Integration und Produktionslizenzierung.
- Keycloak, [Server Administration Guide](https://www.keycloak.org/docs/latest/server_admin/) und [OIDC layers](https://www.keycloak.org/securing-apps/oidc-layers) — Realm-/Client-/Benutzerverwaltung und OIDC-Funktionen.
- Microsoft, [ASP.NET Core Identity API authorization for SPAs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0) — Cookie-Empfehlung für Browser-SPAs und separate Token-Optionen; kein Ersatz für die hier benötigten OAuth-Autorisierungs-/Tokenendpunkte.
- Keycloak, [Configuring a reverse proxy](https://www.keycloak.org/server/reverseproxy) und [Configuring the hostname (v2)](https://www.keycloak.org/server/hostname) (abgerufen 11.10.2026) — freizugebende Pfade (`/realms/`, `/resources/`), nicht öffentliche Pfade (`/admin/`, `/realms/master/`, `/metrics`, `/health`), `proxy-headers`/`proxy-trusted-addresses`, `hostname` als vollständige URL mit Pfad, `hostname-admin`, `hostname-backchannel-dynamic`.
- OpenAI, GPT Action authentication / Production notes (erneut abgerufen 11.10.2026) — keine Angaben zu PKCE oder zur Client-Authentifizierungsmethode am Token-Endpunkt; daher als Live-Interoperabilitätstest geführt.
