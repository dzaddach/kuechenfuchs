# Küchenfuchs – Machbarkeitsnachweis Angebotsquelle

**Status:** Prüfplan; noch kein Anbieter technisch oder rechtlich freigegeben
**Zielzeitpunkt:** Start parallel zu den ersten Produktpaketen; Abschluss vor Implementierung einer realen Quelle
**Verantwortung:** Auftraggeber gibt Datenzugriff und Rechtsprüfung frei; Entwickler sammelt reproduzierbare technische Belege.

## Zweck und Umfang

Separat von der Rezept-/KI-Entwicklung wird für mindestens eine konkrete Supermarktquelle geprüft, ob eine Angebotsanbindung in einer festgelegten Pilotregion technisch ausreichend stabil und zur beabsichtigten Nutzung zulässig ist. Die Prüfung darf verfügbare offizielle Dokumentation oder ausdrücklich genehmigten Zugang verwenden. Öffentliche Erreichbarkeit einer Seite allein ist keine Nutzungserlaubnis.

Dieser Nachweis ist eine Voraussetzung für eine spätere produktive Provider-Implementierung, nicht für PWA, Rezepte, KI, Einkaufslisten oder den Mock-Provider. Die erste Recherche in [offer-providers.md](./offer-providers.md) ist lediglich eine Shortlist; sie bestätigt derzeit keine Quelle.

## Vorgesehene Arbeitspakete

### MF1 – Quelle und Nutzungspfad

Beginnt parallel zu Paket P0/P1 in [implementation-plan.md](./implementation-plan.md).

1. Mindestens eine Quelle aus Lidl, REWE, ALDI Nord/Süd, EDEKA, Kaufland oder eine begründet bessere Alternative auswählen.
2. Offizielle API-/Partnerdokumentation, Vertrags-/Lizenzbedingungen, erlaubte Felder, Speicher-/Cache-Dauer, Anzeige-/Attributionspflichten, Abruflimits und regionale Abdeckung belegen.
3. Nur wenn die Bedingungen unklar sind: konkrete schriftliche Anfrage an den Anbieter bzw. qualifizierte rechtliche Prüfung veranlassen. Ungeklärte Punkte zählen nicht als Erlaubnis.
4. Erforderliche Authentifizierung, Nutzerkonto, App, Coupon-/Loyalty-Voraussetzung und manuelle Filialauswahl dokumentieren.
5. Entscheidung **Go für einen begrenzten technischen Probeabruf**, **No-Go** oder **offen/blockiert** treffen.

**MF1-Abnahme:** Der Bericht benennt Quelle, offizielle Zugangsmethode und Quellenbelege mit Abrufdatum; klärt, ob der geplante private Abruf, die Speicherung der benötigten Angebotsdaten und deren Anzeige abgedeckt sind. Er listet Regions-, Authentifizierungs- und Attributionsauflagen. Bei rechtlicher Unklarheit steht die konkrete offene Rechtsfrage und der zur Klärung notwendige nächste Schritt fest. Ohne schriftlich geklärten Erlaubnispfad kein automatisierter Probeabruf.

### MF2 – Technischer Zuverlässigkeitsnachweis

Beginnt nach MF1-Go und läuft parallel zu den Produktpaketen, spätestens bevor P7/P8 eine echte Quelle voraussetzen könnten. Keine dauerhafte Produktionsintegration.

1. Eine Pilotregion bzw. konkrete Filiale und zulässige Abrufmethode fixieren.
2. Innerhalb der erlaubten Abruflimits an mindestens drei verschiedenen Tagen über mindestens sieben Kalendertage Probeabrufe durchführen.
3. Einen Referenzsatz von mindestens 30 tatsächlich gelieferten Angebotsdatensätzen gegen die zugehörige offizielle Darstellung/Antwort prüfen. Der Referenzsatz darf nicht künstlich auf fehlende oder unzugängliche Datensätze ergänzt werden.
4. Fehler, Laufzeit, Aktualität, Gültigkeit, Preis-/Packungsdaten, Filial-/Regionsbezug, Quellenlink und Bedingungen protokollieren. Sensible Zugangsdaten nicht in den Bericht aufnehmen.
5. Einen klaren Go/No-Go-Bericht mit Aufwandsschätzung, Einschränkungen und Mock-Fallback erstellen.

## Erfolgskriterien für „Go“

Alle Kriterien sind kumulativ. Zielwerte sind Küchenfuchs-Abnahmeschwellen, keine Behauptungen über vorhandene Anbieterfähigkeiten:

1. **Recht:** Ein dokumentierter und vom Auftraggeber freigegebener Nutzungspfad erlaubt die geplante Abfrage sowie Speicherung und Anzeige der für das Feature benötigten Daten. Anbieterbedingungen und erforderliche Rechtsprüfung sind abgeschlossen; keine Schutzmaßnahme wird umgangen.
2. **Wiederholbarkeit:** Mindestens 3 von 3 zulässigen Probeabrufen an verschiedenen Tagen über mindestens 7 Tage liefern verwertbare Antworten innerhalb der dokumentierten Anbieterlimits. Fehlerrate/Timeouts und eventuelle Einschränkungen stehen im Bericht.
3. **Mindestabdeckung:** Mindestens 30 echte, voneinander unterscheidbare Angebote aus der Pilotregion liegen für den Referenzabgleich vor. Wenn die Quelle saisonal oder regional weniger liefert, wird kein künstlicher Erfolg konstruiert; der Auftraggeber entscheidet, ob das Zielgebiet geändert wird.
4. **Pflichtfelder:** 100 % der in die Anwendung übernehmbaren Datensätze haben eine belastbare Quelle/Erfassungszeit, Produktidentifikation, Preis und Währung, Packungsgröße/-einheit, Region/Filiale und Angebotsgültigkeit oder werden ausdrücklich als nicht verwendbar ausgeschlossen. Angebotsbedingungen, die den Preis ändern (z. B. App-Coupon), müssen erhalten bleiben.
5. **Referenzgenauigkeit:** Mindestens 95 % des Referenzsatzes stimmen bei Preis, Produkt/Packungsgröße, Gültigkeitszeitraum und Region/Filiale mit der offiziellen Quelle überein. 100 % der übernommenen Datensätze behalten eine nachvollziehbare Herkunft.
6. **Betriebsfähigkeit:** Erlaubte Abrufrate, Authentifizierungsablauf, Daten-Aufbewahrung und notwendige manuelle Eingriffe sind dokumentiert; Timeout/Fehler kann erkannt und die Quelle ohne Datenverlust deaktiviert werden.
7. **Review:** ChatGPT-Review des begrenzten Probeabrufs findet keinen ungelösten kritischen oder hohen Sicherheits-, Lizenz- oder Datenqualitätsbefund; Auftraggeber nimmt den Bericht ausdrücklich ab.

Ein nicht erfülltes Pflichtkriterium bedeutet **No-Go** für die Integration in den MVP. „Offen“ ist nicht „Go“.

## Ergebnisformat

Der Nachweis wird als versionierter Bericht mit folgenden Abschnitten abgelegt:

- Anbieter, Pilotregion und Prüfzeitraum;
- Zugangsweg und konkrete offizielle Vertrags-/API-/Dokumentationsbelege;
- Rechtsstatus, Freigabeperson und verbleibende Fragen;
- Anzahl der Abfragen/Datensätze, Zeitpunkt, Laufzeit und Fehler;
- Referenzstichprobe und Genauigkeitsauswertung ohne Veröffentlichung geschützter Rohdaten;
- Feldabdeckung, Attributions- und Aufbewahrungsauflagen;
- notwendige Betriebs-/Wartungskosten und menschlicher Aufwand;
- Kriterienmatrix (erfüllt/nicht erfüllt/nicht geprüft), Go/No-Go, Reviewer und Auftraggeberentscheidung.

Vor der Implementierungsfreigabe wird der Planungsabschnitt dieses Dokuments durch tatsächliche Ergebnisse ergänzt. Keine Zugangsdaten, personenbezogenen Kontodaten oder urheberrechtlich geschützten Prospektkopien in das Repository aufnehmen.

## Fallback und Folgeentscheidung

- **Go:** Erst nach eigener Freigabe wird ein kleiner, abschaltbarer Provider als separates Arbeitspaket implementiert; Mock bleibt für Tests und Provider-Ausfälle verfügbar.
- **No-Go/offen:** keine automatische Abfrage und keine Preisbehauptung. Mock-Daten bleiben eindeutig synthetisch markiert; Nutzer kann ohne Angebotsempfehlung Rezepte und Einkaufslisten verwenden.
- **Teilweise Abdeckung:** Anwendungsumfang und Marktregion müssen so begrenzt werden, dass die UI keine vollständige oder repräsentative Abdeckung suggeriert. Das erfordert eine erneute Auftraggeberentscheidung.
