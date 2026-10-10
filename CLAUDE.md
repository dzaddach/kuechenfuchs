# Arbeitsanweisung für Claude Code – Küchenfuchs

Lies zuerst README.md und docs/PROJECT_BRIEF.md.

## Verbindlicher Status: PLANUNG
**Keine Anwendung implementieren und keine technischen Ressourcen provisionieren.**
Bis zur ausdrücklichen Freigabe durch den Auftraggeber:
- keinen Anwendungscode schreiben, keine Dependencies installieren
- keine Docker-Container, Datenbanken oder externen Dienste starten
- keine API-Schlüssel anlegen, keine Deployments durchführen
- bestehende Dateien nicht ohne nachvollziehbaren Grund überschreiben
- Änderungen auf Planungsdokumente in docs/ beschränken

## Aktueller Auftrag
Erstelle eine pragmatische, überprüfbare Architektur und einen **konkreten Implementierungsplan** für eine zunächst von einem Entwickler gebaute PWA.

Lege folgende Dokumente an:
- docs/architecture.md – Komponenten, Datenflüsse, Mermaid-Diagramm
- docs/data-model.md – Entitäten, Beziehungen, Mengen-/Einheitenkonzept
- docs/api-design.md – REST-Endpunkte, externe ChatGPT-Anbindung und Sicherheit
- docs/offer-providers.md – konkret recherchierte Angebotsquellen, Belege, Nutzbarkeit, Risiken, Fallback mit Mock-Provider
- docs/ai-integration.md – KI-Grenzen, strukturierte Ausgabe, Kosten, Datenschutz
- docs/frontend-concept.md – Navigation, Kernseiten, mobile-first-Workflows
- docs/deployment.md – Docker-Compose-Topologie, Secrets, Backups, Updates
- docs/implementation-plan.md – priorisierte kleine Arbeitspakete, Abhängigkeiten, Tests, Akzeptanzkriterien
- docs/open-decisions.md – Entscheidungsliste mit klaren Empfehlungen, offenen Fragen und Risiken

## Planungsleitlinien
- Technologiewunsch: Angular, ASP.NET Core, PostgreSQL, EF Core, Docker Compose; Alternativen nur begründet
- Kein Overengineering; modularer Monolith ist der bevorzugte Ausgangspunkt
- Strikte Trennung zwischen Rezeptzutat, normalisiertem Lebensmittel, Händlerprodukt und zeitlich gültigem Angebot
- Niemals Angebotspreise, APIs oder Fähigkeiten von Datenquellen erfinden
- Rechercheergebnisse von Annahmen trennen, Quellen mit Datum vermerken
- Ersten Durchstich mit Mock-Angeboten priorisieren
- Datenmodell und Endpunkte so konkret wie nötig, aber ohne voreilige Implementierung
- Nach jedem späteren Arbeitspaket lauffähiger, testbarer Stand
- Keine automatische Freigabe durch Dokumenterstellung; ausdrücklich auf Entscheidung warten

## Zusammenarbeit
Claude: planen, nach Freigabe schrittweise entwickeln und testen.
ChatGPT: unabhängige Architektur- und Codeprüfung anhand des GitHub-Repositories.
Auftraggeber: Architekturentscheidungen und Implementierung freigeben.

Wenn die Dokumente erstellt sind, liefere nur eine kompakte Zusammenfassung mit wesentlichen Entscheidungen, Risiken und der empfohlenen MVP-Reihenfolge – **dann STOPP**.
