# Küchenfuchs – Frontend-Konzept

**Stand:** 10. Oktober 2026 · **Ziel:** früh nutzbare, optisch ausgearbeitete iPhone-first Online-PWA

## Navigation

Mobile Navigation mit vier stabilen Hauptzielen:

1. **Heute/Plan** – Wochenplan und nächste Mahlzeiten
2. **Rezepte** – suchen, filtern, Rezept ansehen/bearbeiten
3. **Einkauf** – aktive Einkaufsliste und Abhaken
4. **Angebote** – Angebote/Empfehlungen nach Region oder Filiale

Ein klar sichtbarer „Rezept hinzufügen“- und „Mit KI erstellen“-Einstieg sowie Haushalts-/Einstellungszugriff ergänzen die Hauptnavigation. Desktop kann dieselben Bereiche als Seitenleiste zeigen. Rezept- und Angebotsansicht bieten jeweils einen direkten Weg zur anderen Perspektive. Der erste benutzbare Release priorisiert Rezepte und KI; Plan, Einkauf und Angebote werden schrittweise ergänzt.

## Kernseiten und Zustände

### Wochenplan

- Woche wechseln, Tag/ Mahlzeit auswählen, Rezept hinzufügen und Portionen festlegen.
- Leere Woche mit primärer Aktion „Rezepte planen“; keine leeren Preisversprechen.
- Einkaufliste aus ausgewählten Mahlzeiten erzeugen; bereits vorhandene Liste nicht unbemerkt überschreiben.

### Rezepte

- Suche, Tag-/Favoritenfilter, sortierte Liste und „Neues Rezept“.
- Formularabschnitte: Basisdaten, Portionen, Zutaten, Schritte; Mengen und Einheit als getrennte Eingaben.
- Zutatenzeile erlaubt Menge unbekannt/„nach Geschmack“, optionaler Text und kontrollierte Lebensmittelzuordnung.
- Portions-Skalierung wird vor dem Speichern sichtbar dargestellt.

### KI-Rezeptassistent

- Direkt von Rezeptliste und Rezepteditor erreichbar, nicht in einer versteckten Einstellungsseite.
- Eingabe für Rezeptziel, vorhandene Zutaten und optionale Einschränkungen; vor Absenden klare Anzeige, dass ein externer KI-Dienst verwendet wird.
- Antwort als bearbeitbarer Entwurf mit Warnungen; sichtbare Aktionen „Übernehmen und speichern“ und „Verwerfen“.
- Fehler, Budgetlimit und fehlende Verbindung lassen die manuelle Rezeptverwaltung verfügbar.

### Externe ChatGPT-Integration

- Separate Anleitung zum Verbinden/Autorisieren; angeforderte Rechte vor dem OAuth-Redirect verständlich benennen.
- Nutzer kann die Berechtigung widerrufen. Erfolgreiches Speichern wird mit Rezeptlink/Bestätigung quittiert.
- Die Integration setzt einen erreichbaren HTTPS-Endpunkt und unterstützten OAuth-Flow voraus; lokale Compose-Entwicklung allein stellt diese Erreichbarkeit nicht bereit.
- **Umgesetzt (P5a.2):** **Einstellungen → ChatGPT-Verbindung** (`/settings/chatgpt`). Zwei getrennte Statusstufen: (1) „Keycloak-Konto verknüpft“ mit Haushalt und Zeitpunkt, (2) „ChatGPT autorisiert“ nur, wenn der Server eine ChatGPT-Sitzung in Keycloak bestätigt – sonst „noch nicht autorisiert“ bzw. „nicht prüfbar“. Verbinden: Haushalt wählen (bei einem Haushalt vorausgewählt), ausdrückliche Bestätigung, dann Top-Level-Weiterleitung zu Keycloak; Rückkehr mit verständlicher Erfolg-/Fehlermeldung, die anschließend aus der URL entfernt wird. Trennen nur nach zweiter Bestätigung; konnte Keycloak die Sitzungen nicht beenden, Hinweis mit „Erneut versuchen“. Frühere Verbindungen werden aufgelistet. Ist Keycloak auf dem Server nicht aktiviert, erklärt die Seite das, ohne andere Funktionen zu beeinträchtigen. Die Seite speichert und zeigt keine Tokens.

### Rezeptdetails

- gut lesbare Zutatenliste und Schritte auf kleinem Bildschirm;
- Portionen-Steuerung passt Mengen live an;
- Aktion „Zum Plan“ oder „Einkaufsliste erstellen“; Änderungen markieren, wenn noch nicht gespeichert.

### Einkaufsliste

- gruppiert nach Lebensmittelkategorie, aber einzelne Posten mit Menge/Einheit und Rezeptursprung nachvollziehbar;
- große Touch-Ziele für Erledigt-Markierung, manuelles Hinzufügen und Rückgängigmachen;
- Onlinezustand voraussetzen und bei fehlender API-Verbindung klaren Wiederholungszustand zeigen; keine Behauptung, dass Offline-Änderungen gespeichert wurden.

### Angebote und Empfehlung

- Filiale/Region bzw. Auswahlstatus deutlich anzeigen.
- Angebot zeigt Produkt, Packungsgröße, Preis und Grundpreis nur, wenn bekannt, Gültigkeit, Bedingungen, Quelle und Erfassungszeit.
- Mock-Daten prominent als synthetisch kennzeichnen.
- Empfehlung zerlegt Kosten pro Packung/Markt und listet ungepreiste oder nicht gematchte Positionen separat.
- Keine Nutzerführung, die fehlende Preise als 0 oder eine unvollständige Lösung als „günstigste“ tarnt.

## Mobile-first-Interaktionen

- Formulare schrittweise, aber ohne Datenverlust beim Zurückgehen; ungespeicherte Änderungen sichtbar markieren.
- Eingabefelder und Buttons iPhone-tauglich, Tastaturtypen für Menge/Text passend, sichere Abstände zu Notch/Bottom-Bar.
- Keine Hover-only-Bedienung. Screenreader-Labels, Fokuszustände, ausreichender Kontrast, skalierbare Schrift und sichtbare Fehler je Feld.
- Frühes UI-Paket setzt ein konsistentes visuelles Fundament: Typografie, Farbsystem, Abstände, Karten, Formulare, Bottom-Navigation, Lade-/Leer-/Fehlerzustände und Touch-Zielgrößen. Keine Platzhalteroberfläche bis zum Ende des Projekts.
- Als Review-Grundlage werden Rezeptübersicht und Rezepteditor in iPhone-Breite als echte UI-Zustände vorgelegt; der Auftraggeber kann die visuelle Richtung vor dem Ausbau weiterer Bereiche bestätigen.
- Toasts allein reichen nicht für kritische Ergebnisse; Speichern, Verbindungszustand und KI-Verarbeitung bleiben sichtbar.
- Loading-, Leer-, Fehler-, veraltete Daten- und „nicht verfügbar“-Zustand für jede externe Quelle.

## PWA im ersten Schritt und spätere Offline-Option

- Die PWA ist früh installierbar und online nutzbar; der Service Worker darf zunächst nur App-Shell/statische Assets bereitstellen.
- Rezept-, KI-, Listen- und Angebotsdaten werden im ersten Schritt nicht offline bearbeitet oder synchronisiert.
- Mehrgerätzugriff läuft online über die API. Serverseitiger Concurrency-Schutz verhindert unbemerkte gleichzeitige Überschreibung.
- Offline-Datencache, ausstehende lokale Änderungen und Konflikt-UX sind ein späteres, separat freizugebendes Paket.

## Akzeptanzkriterien für den ersten Durchstich

- iPhone-Breite: Rezept anlegen, speichern und wieder öffnen ohne horizontales Scrollen.
- Rezept mit Portionszahl skalieren; Zutatenmengen bleiben nachvollziehbar.
- Einkaufsliste erstellen, Einträge ergänzen und erledigt markieren; Mock-Angebote für zugeordnete Produkte sehen.
- Fehlende Match-/Preis-/Filialdaten und synthetische Mock-Daten sind eindeutig gekennzeichnet.
- PWA bleibt bei KI-/Anbieterfehler bedienbar und gibt keinen Erfolg vor, wenn API-Aufrufe fehlgeschlagen sind.
- Die App kann auf einem iPhone installiert werden und bleibt bei den zentralen Rezept- und KI-Workflows auch ohne Angebotsdaten nutzbar.
