# Küchenfuchs – Offene Entscheidungen

**Stand:** 10. Oktober 2026
**Hinweis:** Empfehlungen sind Vorschläge, keine stillschweigend getroffenen Auftraggeberentscheidungen. Architektur- und Implementierungsfreigabe bleibt erforderlich.

| ID | Entscheidung / Frage | Empfehlung | Auswirkungen / Risiko | Status |
|---|---|---|---|---|
| D1 | Haushaltsmodell und Nutzerumfang zum MVP? | Haushalt/Mitgliedschaft von Anfang an modellieren; MVP zunächst ein privater Haushalt und wenige Konten, keine komplexen Rollen. | Auth-/Autorisierungsaufwand früh, verhindert spätere Trennung und Synchronisationsprobleme. | Auftraggeber bestätigen |
| D2 | Authentifizierung für PWA und ChatGPT? | P2: ASP.NET Core Identity mit sicherer Same-Origin-Cookie-Session für die PWA. P5a: bestehenden OIDC-Provider, bevorzugt Keycloak, als Actions-Token-Aussteller prüfen; keine eigene OAuth-/OIDC-Serverentwicklung. Externe `issuer`+`subject`-Identität muss serverseitig mit einem internen Nutzer verknüpft sein; Haushaltsrechte und Scopes bleiben API-seitig maßgeblich. | ChatGPT Actions benötigt derzeit Authorization Code, Client-ID/-Secret, registrierte Redirect-URI und `state`. Eine externe Lösung führt zu Kontoverknüpfungs-, Einwilligungs-, Betriebs- und Backupaufwand; Keycloak-Kompatibilität ist durch End-to-End-Test zu belegen. P2 allein bietet keine Actions-OAuth-Schnittstelle. | P2-Session umgesetzt; Providerwahl und P5a-Freigabe offen |
| D3 | Zeitpunkt und Umfang der ChatGPT-Integration? | Früh nach Rezept-Grundablauf; Entwurf und explizit bestätigtes Speichern, benutzerbezogen autorisiert. | Erfordert öffentlich erreichbares HTTPS-Testsystem und abgestimmte OAuth-Registrierung. | Frühe Ausbaustufe |
| D4 | KI-Provider, Datenschutz und Limits? | OpenAI API backendseitig für den frühen PWA-KI-Assistenten; Modell/Preis konfigurierbar, Monatsbudget und klare Nutzereinwilligung. | Laufende variable Kosten, Aufbewahrung und Drittanbieter-Datenschutz; konkretes Modell/Limit vor Aktivierung freigeben. | Vor KI-Paket bestätigen |
| D5 | Reale Angebotsquelle machbar? | Bereits in den ersten Entwicklungsphasen separater Machbarkeitsnachweis mit rechtlicher und technischer Go/No-Go-Entscheidung; Mock bleibt Fallback. | Bisher keine belastbare, frei nutzbare API bestätigt. Keine automatisierte Erfassung vor geklärter Erlaubnis. | Früh zu prüfen |
| D6 | Caddy oder Traefik? | Caddy für einzelnen Linux-Host; Traefik nur bei bereits bestehendem Betriebsmuster/mehreren dynamischen Diensten. | Caddy reduziert zunächst Konfigurationsaufwand; Zertifikate erfordern Domain und erreichbare Ports. | Vorschlag |
| D7 | Offline-Synchronisationstiefe? | Zunächst installierbare, online arbeitende PWA; keine lokalen Datenschreibvorgänge, Offline-Datencaches oder Sync-/Konfliktlogik. | App-Daten stehen ohne API-Verbindung zunächst nicht zur Verfügung. | Zurückgestellt |
| D8 | URL-Rezeptimport? | Nicht Teil des MVP; zunächst manuelle Rezepteingabe. Später nur erlaubte Quellen/Benutzerbestätigung, SSRF-Schutz und Copyright-/Nutzungsprüfung. | Server-URL-Abruf ist SSRF-Angriffsfläche; Rezepttexte/Bilder haben Nutzungsrechte. | Vertagt |
| D9 | Fotos/Uploads? | Im MVP weglassen; später Speicherort, Größen-/Formatlimits, Virenprüfung, Rechte und Backup festlegen. | Uploads erhöhen Angriffs-, Speicher- und Backupumfang. | Vertagt |
| D10 | Bewertungsziel für Optimierer? | MVP minimiert nur bekannte Packungskosten bei nachvollziehbarem Stopplimit; unbekannte Preise separat zeigen, Fahrtkosten/Vorräte später. | Eine vollständige globale Kostenminimierung ist mit unvollständigen Quellen unmöglich. Keine „günstigste“ Behauptung ohne Abdeckung. | Empfehlung |
| D11 | Betriebsziele? | Startvorschlag: tägliches Backup, RPO bis 24 h, RTO innerhalb eines Tages; vor produktiver Nutzung bestätigen. | Ohne Restore-Test und bestätigte RPO/RTO ist Selbsthosting nicht belastbar. | Auftraggeber bestätigen |
| D12 | Hosting/Datenschutzstandort? | Server in einer vom Auftraggeber freigegebenen Region; Datenverarbeitung externer KI-Dienste separat entscheiden. | Personen-/Haushaltsdaten und potenzielle Drittlandverarbeitung; Datenschutzpflichten vor Produktionsbetrieb prüfen. | Auftraggeber klären |
| D13 | Technologieversionen und Updatepolitik? | Zum Start unterstützte Angular/.NET/PostgreSQL-Versionen fixieren, lockfiles und Container-Digests verwenden, regelmäßige Sicherheitsupdates. | Releasezyklen und Kompatibilität; Versionen nicht vor Projektstart unnötig festschreiben. | Vor Paket 0 prüfen |
| D14 | Öffentlicher ChatGPT-Action-Testendpunkt? | Für die frühe Ende-zu-Ende-Integration einen begrenzten HTTPS-Testendpunkt mit genehmigter Domain und OAuth-Redirects bereitstellen; bis dahin Actions lokal/fake testen. | Erfordert externe Erreichbarkeit und OAuth-Client-Konfiguration; kein ungeschützter Tunnel oder produktives Geheimnis im Repository. | Vor P5b freigeben |
| D15 | OAuth/OIDC-Server für Actions | Keinen eigenen Autorisierungsserver entwickeln. Als bestehenden Provider Keycloak evaluieren; PWA-Identity in P2 bleibt zunächst ASP.NET Core Identity. In P5a Identitätsverknüpfung und delegierte Haushaltsfreigabe praktisch nachweisen. OpenIddict ist Framework, kein fertiger Provider; nur nach expliziter neuer Freigabe für eine eigene Serverinstanz erwägen. | Zwei Identitätsebenen können Verknüpfungs- und Supportaufwand erzeugen. Keycloak fügt einen zu betreibenden Dienst hinzu. Vor Implementierung festlegen, wie Benutzer erstellt/verknüpft, Einwilligungen verwaltet, Scopes begrenzt und Widerrufe wirksam werden. | Empfehlung; Auftraggeberentscheidung vor P5a |

## Abweichungen zum ursprünglichen Plan

- KI-Rezeptentwürfe und externe ChatGPT-Actions wurden von späteren Ausbaustufen in frühe Pakete nach dem Rezept-Grundablauf vorgezogen.
- Ein visuell ausgearbeitetes, iPhone-taugliches UI und eine installierbare Online-PWA entstehen früh; Offline-Datenzugriff und Synchronisation wurden dagegen zurückgestellt.
- Docker Compose ist nicht mehr nur Produktions-/spätere Betriebsziel, sondern die reproduzierbare lokale Entwicklungsumgebung ab Paket 0.
- Die Prüfung realer Angebotsquellen startet parallel zu den ersten Entwicklungspaketen und erhält einen eigenen Machbarkeitsnachweis mit messbaren Go/No-Go-Kriterien. Mock-Angebote bleiben unabhängig davon der Test- und Fallbackpfad.
- Der Implementierungsplan ist in kleinere, einzeln test- und reviewbare Lieferungen aufgeteilt; ein erster praktisch nutzbarer Rezeptbereich entsteht vor Einkaufsoptimierung.

## Hauptrisiken und Gegenmaßnahmen

1. **Angebotsquellen fehlen/ändern sich:** frühe Machbarkeitsprüfung mit dokumentiertem Go/No-Go, Mock als vollständiger Fallback, Quellenstatus und explizit unbekannte Preise.
2. **Falsche Einheiten-/Produktzuordnung:** vier Domänen strikt trennen, dimensionsgerechte Aggregation, unsichere Treffer sichtbar bestätigen lassen.
3. **Offline-Nutzung:** zunächst bewusst online-only; Offline-Datensync erst nach separater fachlicher Entscheidung.
4. **KI-Kosten und unzutreffende Inhalte:** früh, aber begrenzt; strukturiert und domain-validiert, Budgetgrenze, keine automatische Speicherung.
5. **ChatGPT-Actions erhöhen Angriffsfläche:** keine eigene OAuth-Serverlogik; bestehenden Provider (Keycloak-Kandidat) prüfen, minimale Scopes, verifizierte Kontoverknüpfung, erreichbares HTTPS-Testsystem, Audit und separate Sicherheitsprüfung.
6. **URL-Import erhöht Angriffsfläche:** nicht Teil des frühen Umfangs; vor Aktivierung separate Threat-/Rechtsprüfung und SSRF-Abwehr.
7. **Einzelentwickler-Betrieb:** Modularer Monolith, kleine Pakete, Docker Compose, automatisierte Tests und dokumentierte Wiederherstellung; keine unnötige Infrastruktur.

## Freigabepunkt

Nach Durchsicht dieses Plans entscheidet der Auftraggeber ausdrücklich, welche offenen Punkte übernommen, angepasst oder vertagt werden und ob Paket 0 beginnen darf. Das Vorliegen dieser Dokumente ist keine Freigabe zur Implementierung, zum Starten von Diensten oder zum Zugriff auf echte Angebotsquellen.
