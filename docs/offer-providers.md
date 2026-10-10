# Küchenfuchs – Angebotsquellen und Provider

**Recherche-Stichtag:** 10. Oktober 2026
**Wichtig:** Die folgenden Beobachtungen belegen nur die öffentlich sichtbaren Seiten/Informationen am Stichtag. Sie belegen weder ein Nutzungsrecht für automatisiertes Auslesen noch eine stabile API, vollständige Preisabdeckung oder technische Eignung. Keine konkreten Preise wurden übernommen oder angenommen.

## Prüfstatus nach Anbieter

| Anbieter | Beobachtung am Stichtag | Region/Zeit/Produktdaten | Nutzbarkeit und offene Punkte | MVP-Eignung |
|---|---|---|---|---|
| Lidl | Offizielle Prospektseite sagt, Prospekte seien online/in der App abrufbar und Angebote regional in der Filiale; Aktionen wechseln. Die allgemeine Angebotsseite verweist auf Prospekte und Filialen. | Filial-/Regionsbezug wird ausdrücklich erwähnt. Aus der sichtbaren Seite ergibt sich kein stabiler maschinenlesbarer Datensatz oder garantierter Preis-/Packungsumfang. | Keine öffentliche, dokumentierte Daten-API verifiziert. Prospektdarstellung und mögliche dynamische Inhalte machen Parser fragil. Nutzungsbedingungen, Robots-Regeln, Rechte an Bild/Text und zulässige Abrufrate vor jeglicher Automatisierung prüfen. | Mock; danach manuell/klickbarer Quellverweis evaluieren, kein Scraping im MVP. |
| REWE | Offizielle nationale Angebotsseite mit Titel „Angebote in deinem REWE Markt“ und Beschreibung zu Wochenangeboten im nahen Markt. HTML lädt mehrere Angebotskomponenten dynamisch; beobachteter initialer Kontext hatte noch keine ausgewählte Markt-ID. | Lokaler Markt ist fachlich relevant; Initialseite allein beweist keine Marktverfügbarkeit. Die sichtbare Angebotsauswahl kann dynamische Daten benötigen. | Keine öffentliche, für diese Anwendung dokumentierte API verifiziert. Eventuelle Shop-/Web-Endpunkte nicht als öffentliche Schnittstelle behandeln. Bedingungen und regionale Unterschiede prüfen. | Mock; reale Anbindung nur mit dokumentiertem/erlaubtem Weg. |
| ALDI Nord | Offizielle Angebotsseite abrufbar; Titel „Aktuelle Angebote von ALDI Nord zum ALDI Preis“. | Die abgerufene sichtbare Ausgabe enthält keine ausreichend belegte, strukturierte Produkt-/Filial-/Preis-Schnittstelle. | Keine dokumentierte öffentliche API oder automatisierte Weiterverwendungsfreigabe verifiziert; getrennt von ALDI Süd untersuchen. | Mock. |
| ALDI Süd | Offizielle Angebotsseite erreichbar. Die Seite weist darauf hin, dass Sortiments- und Aktionsartikel ausschließlich in Filialen erhältlich sind; Lieferdienst wird nur in ausgewählten Gebieten getestet. | Filialverfügbarkeit/Region relevant; Online-Shop für ALDI-Angebote nicht als Quelle voraussetzen. | Keine dokumentierte öffentliche Angebots-API verifiziert. Vor automatisierter Nutzung Nutzungsbedingungen, technische Zugänglichkeit und Rechte prüfen. | Mock. |
| EDEKA | EDEKA-Startseite zeigt Bedingungen für App-Angebote: App an der Kasse, Registrierung, Lieblingsmarkt, teilnehmende Märkte; es gelten regionale Ausnahmen. | App-Angebote sind marktspezifisch und nur für teilnehmende Märkte gültig. Eine zentrale vollständige Markt-Abdeckung ist nicht belegt. | Die getesteten allgemeinen Angebots-URLs lieferten 404; kein verlässlicher allgemeiner Quellpfad bzw. keine API verifiziert. Händlerstruktur/Regionenvielfalt ist ein erhöhtes Pflege-/Abdeckungsrisiko. | Mock; erst mit konkreter Pilotregion und autorisierter Datenquelle neu bewerten. |
| Kaufland | Abruf der offiziellen Angebotsseite sowie Startseite aus dieser Rechercheumgebung mit HTTP 403 abgelehnt. | Nicht beurteilbar; daraus folgt nicht, dass die Seite für Nutzer nicht verfügbar ist. | Keine Aussage zu API, Preisabdeckung, Nutzungsrechten oder technischem Verfahren möglich. Kein Umgehen der Zugriffsbeschränkung. | Mock. Recherche später manuell über zulässige offizielle Dokumentation fortsetzen. |

**Fazit:** Für keinen der sechs untersuchten Namen konnte bei dieser Prüfung eine belastbare, frei nutzbare und dokumentierte Angebots-API bestätigt werden. Das ist ein Recherchebefund, keine Behauptung, dass keine vertragliche oder zukünftige Partner-Schnittstelle existiert. Produktive automatische Provider sind deshalb ein eigener Freigabe- und Recherchepunkt, kein versteckter Bestandteil des MVP.

## Provider-Vertrag (Plan)

Alle Provider geben dasselbe interne, normalisierte Format zurück:

- `Retailer`, optional `Store`/Region
- `RetailerProduct` mit Quelle, Erfassungszeit und möglichst nachweisbarer Packungsgröße
- `Offer` mit Betrag/Währung nur, wenn tatsächlich geliefert oder belastbar erfasst; Gültigkeitsintervall; Angebotsbedingungen; Erfassungszeit; Quellreferenz
- Datenherkunft (Providerkennung, Quell-URL/Dokument-ID, Erfassungsart), Status und optionale Fehler

Adapter sollen unverlässliche/quellenabhängige Felder abbilden, nicht fachliche Regeln duplizieren. Ein fehlendes Feld ist `unknown`, nicht ein erratener Standardwert. Providerfehler und veraltete Daten müssen sich von „keine Angebote verfügbar“ unterscheiden.

## Mock-Provider (MVP)

Der Mock-Provider ist die einzige Angebotsquelle des ersten Durchstichs:

- kleines versioniertes Fixture mit klar als synthetisch markierten Händlern, Produkten, Filialen, Preisen und gültigen Zeiträumen;
- keine realen aktuellen Preise oder Behauptung echter Händlerkonditionen;
- Fälle für fehlende Preise, abgelaufenes Angebot, Couponbedingung, Packungsgröße, Produkt ohne Match und regional abweichendes Angebot;
- deterministische Ergebnisse für Integrations- und Akzeptanztests;
- UI kennzeichnet „Beispieldaten (Mock)“ dauerhaft und zeigt Quelle/Erfassungszeit im gleichen Muster wie später echte Daten.

## Risiken und Freigabekriterien für reale Provider

1. **Recht/Nutzungsbedingungen:** schriftliche oder eindeutige rechtliche Prüfung der Weiterverwendung, Speicherung, Darstellung und Cache-Dauer; keine Ableitung einer Erlaubnis aus öffentlicher Abrufbarkeit.
2. **Technik:** dokumentierte Schnittstelle oder ausdrücklich erlaubter Datenzugang; keine Umgehung von Login, Bot-Schutz, 403, CAPTCHAs oder technischen Schutzmaßnahmen.
3. **Abdeckung:** ausgewählte Region/Filiale, Zeitraum, Artikel, Packungsinhalt, Grundpreis und Bedingungen konkret testen.
4. **Qualität:** Aktualitäts-SLA nicht unterstellen; zeitliche Gültigkeit und Erfassungszeit getrennt erfassen; fehlerhafte/abgelaufene Angebote sichtbar ausschließen.
5. **Betrieb:** Abruffrequenz, Timeout, Retry mit Backoff, Circuit Breaker, Monitoring und manuelles Deaktivieren je Provider planen.
6. **Nutzwert:** Fehlen belastbare Angebote, Produktzuordnungen oder Preisbedingungen, muss der Optimierer unvollständig/unbekannt melden und darf keine „günstigste“ Gesamtoption behaupten.

## Quellen und Prüfbelege

Alle folgenden Seiten wurden am **10.10.2026** abgerufen. „Abrufbar“ bezeichnet die Beobachtung dieser Prüfung; Inhalte können sich ändern.

- Lidl, Prospekte: <https://www.lidl.de/c/online-prospekte/s10005610> — Seite beschreibt regionale Prospekte in Filiale, online und App.
- Lidl, allgemeine Angebote: <https://www.lidl.de/angebote> — Weiterleitung zur Startseite; dort Verweis auf Prospekte und lokale Filiale.
- REWE, nationale Angebote: <https://www.rewe.de/angebote/nationale-angebote/> — Seitenbeschreibung bezieht Wochenangebote auf den Markt in der Nähe; HTML enthält dynamische Angebotskomponenten.
- ALDI Nord, Angebote: <https://www.aldi-nord.de/angebote.html> — offizielle Angebotsseite abrufbar, aber in der gelieferten Textansicht ohne verifizierbare Daten-API.
- ALDI Süd, Angebote: <https://www.aldi-sued.de/de/angebote.html> (Weiterleitung auf `/angebote`) — sichtbare Hinweise zu Filialverfügbarkeit und regionalem Lieferdienst-Test.
- EDEKA: <https://www.edeka.de/> — sichtbare Bedingungen schränken App-Angebote auf registrierte Nutzer, ausgewählten Lieblingsmarkt und teilnehmende Märkte ein; regionale Ausnahme genannt.
- Kaufland-Angebote: <https://www.kaufland.de/angebote.html> — Abrufversuch aus Rechercheumgebung mit HTTP 403; keine Inhaltsaussage abgeleitet.
- Kaufland-Startseite: <https://www.kaufland.de/> — Abrufversuch aus Rechercheumgebung mit HTTP 403.
