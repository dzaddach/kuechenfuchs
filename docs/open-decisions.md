# Küchenfuchs – Offene Entscheidungen

**Stand:** 10. Oktober 2026
**Hinweis:** Empfehlungen sind Vorschläge, keine stillschweigend getroffenen Auftraggeberentscheidungen. Architektur- und Implementierungsfreigabe bleibt erforderlich.

| ID | Entscheidung / Frage | Empfehlung | Auswirkungen / Risiko | Status |
|---|---|---|---|---|
| D1 | Haushaltsmodell und Nutzerumfang zum MVP? | Haushalt/Mitgliedschaft von Anfang an modellieren; MVP zunächst ein privater Haushalt und wenige Konten, keine komplexen Rollen. | Auth-/Autorisierungsaufwand früh, verhindert spätere Trennung und Synchronisationsprobleme. | Auftraggeber bestätigen |
| D2 | Authentifizierung für PWA? | ASP.NET Core Identity oder gleichwertige etablierte Lösung; Same-Origin sichere HttpOnly-Cookies mit CSRF-Schutz. | Keine Tokens im Browser-LocalStorage; genauer Login/Passwortreset-/MFA-Betrieb muss festgelegt werden. | Technische Auswahl vor Paket 2 |
| D3 | Externe ChatGPT-Integration sofort oder später? | Später nach MVP; dann OAuth pro Benutzer mit eng begrenzten Scopes und expliziter Speicheraktion. | Action-OAuth ist zusätzlicher Auth-/Sicherheitsaufwand; Fähigkeiten und aktuelle Anforderungen vor Implementierung verifizieren. | Empfehlung: vertagen |
| D4 | KI-Provider und Limits? | OpenAI API nur optional und backendseitig; Monatsbudget, Modell/Preis konfigurierbar, keine KI-Abhängigkeit im MVP. | Laufende variable Kosten, Aufbewahrung und Drittanbieter-Datenschutz; konkretes Modell/Limit vor Aktivierung freigeben. | Offen |
| D5 | Erste reale Angebotsquelle? | Keine im MVP. Nach Mock-Durchstich konkrete Pilotregion und ein Anbieter wählen, nur bei belegter erlaubter Schnittstelle. | Regionale Daten, Aktualität und Nutzungsrechte nicht bestätigt; unberechtigtes Scraping oder veraltete Preise können Vertrauen/Recht verletzen. | Blockiert durch Recherche/Freigabe |
| D6 | Caddy oder Traefik? | Caddy für einzelnen Linux-Host; Traefik nur bei bereits bestehendem Betriebsmuster/mehreren dynamischen Diensten. | Caddy reduziert zunächst Konfigurationsaufwand; Zertifikate erfordern Domain und erreichbare Ports. | Vorschlag |
| D7 | Offline-Synchronisationstiefe? | Zunächst Offline-Lesen plus idempotentes Listen-Abhaken mit Versionskonflikten; keine umfassende bidirektionale Synchronisation. | Offline-Konflikte bleiben möglich und müssen sichtbar gelöst werden. | MVP-Kriterium bestätigen |
| D8 | URL-Rezeptimport? | Nicht Teil des MVP; zunächst manuelle Rezepteingabe. Später nur erlaubte Quellen/Benutzerbestätigung, SSRF-Schutz und Copyright-/Nutzungsprüfung. | Server-URL-Abruf ist SSRF-Angriffsfläche; Rezepttexte/Bilder haben Nutzungsrechte. | Vertagt |
| D9 | Fotos/Uploads? | Im MVP weglassen; später Speicherort, Größen-/Formatlimits, Virenprüfung, Rechte und Backup festlegen. | Uploads erhöhen Angriffs-, Speicher- und Backupumfang. | Vertagt |
| D10 | Bewertungsziel für Optimierer? | MVP minimiert nur bekannte Packungskosten bei nachvollziehbarem Stopplimit; unbekannte Preise separat zeigen, Fahrtkosten/Vorräte später. | Eine vollständige globale Kostenminimierung ist mit unvollständigen Quellen unmöglich. Keine „günstigste“ Behauptung ohne Abdeckung. | Empfehlung |
| D11 | Betriebsziele? | Startvorschlag: tägliches Backup, RPO bis 24 h, RTO innerhalb eines Tages; vor produktiver Nutzung bestätigen. | Ohne Restore-Test und bestätigte RPO/RTO ist Selbsthosting nicht belastbar. | Auftraggeber bestätigen |
| D12 | Hosting/Datenschutzstandort? | Server in einer vom Auftraggeber freigegebenen Region; Datenverarbeitung externer KI-Dienste separat entscheiden. | Personen-/Haushaltsdaten und potenzielle Drittlandverarbeitung; Datenschutzpflichten vor Produktionsbetrieb prüfen. | Auftraggeber klären |
| D13 | Technologieversionen und Updatepolitik? | Zum Start unterstützte Angular/.NET/PostgreSQL-Versionen fixieren, lockfiles und Container-Digests verwenden, regelmäßige Sicherheitsupdates. | Releasezyklen und Kompatibilität; Versionen nicht vor Projektstart unnötig festschreiben. | Vor Paket 0 prüfen |

## Hauptrisiken und Gegenmaßnahmen

1. **Angebotsquellen fehlen/ändern sich:** Start mit Mock, Adaptervertrag, Quellenstatus und explizit unbekannten Preisen; echte Quellen einzeln freigeben.
2. **Falsche Einheiten-/Produktzuordnung:** vier Domänen strikt trennen, dimensionsgerechte Aggregation, unsichere Treffer sichtbar bestätigen lassen.
3. **Offline-Kollisionen:** Versionsprüfung und Konfliktanzeige statt Last-Write-Wins; Cache für sensible Daten bewusst begrenzen.
4. **KI-Kosten und unzutreffende Inhalte:** KI nachrangig, strukturiert und domain-validiert, Budgetgrenze, keine automatische Speicherung.
5. **URL-Import/ChatGPT-Actions erhöhen Angriffsfläche:** nicht Teil des ersten MVP; vor Aktivierung separate Threat-/Rechtsprüfung, OAuth und SSRF-Abwehr.
6. **Einzelentwickler-Betrieb:** Modularer Monolith, kleine Pakete, automatisierte Tests, dokumentierte Wiederherstellung; keine unnötige Infrastruktur.

## Freigabepunkt

Nach Durchsicht dieses Plans entscheidet der Auftraggeber ausdrücklich, welche offenen Punkte übernommen, angepasst oder vertagt werden und ob Paket 0 beginnen darf. Das Vorliegen dieser Dokumente ist keine Freigabe zur Implementierung, zum Starten von Diensten oder zum Zugriff auf echte Angebotsquellen.
