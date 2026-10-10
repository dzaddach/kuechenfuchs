# Küchenfuchs – Frontend-Konzept

**Stand:** 10. Oktober 2026 · **Ziel:** mobile-first PWA mit gleichwertigem Rezept- und Angebots-Einstieg

## Navigation

Mobile Navigation mit vier stabilen Hauptzielen:

1. **Heute/Plan** – Wochenplan und nächste Mahlzeiten
2. **Rezepte** – suchen, filtern, Rezept ansehen/bearbeiten
3. **Einkauf** – aktive Einkaufsliste und Abhaken
4. **Angebote** – Angebote/Empfehlungen nach Region oder Filiale

Ein klar sichtbarer „Rezept hinzufügen“-Einstieg sowie Haushalts-/Einstellungszugriff ergänzen die Hauptnavigation. Desktop kann dieselben Bereiche als Seitenleiste zeigen. Rezept- und Angebotsansicht bieten jeweils einen direkten Weg zur anderen Perspektive.

## Kernseiten und Zustände

### Wochenplan

- Woche wechseln, Tag/ Mahlzeit auswählen, Rezept hinzufügen und Portionen festlegen.
- Leere Woche mit primärer Aktion „Rezepte planen“; keine leeren Preisversprechen.
- Einkaufliste aus ausgewählten Mahlzeiten erzeugen; bereits vorhandene Liste nicht unbemerkt überschreiben.

### Rezepte

- Suche, Tag-/Favoritenfilter, sortierte Liste und „Neues Rezept“.
- Formularabschnitte: Basisdaten, Portionen, Zutaten, Schritte; Mengen und Einheit als getrennte Eingaben.
- Zutatenzeile erlaubt Menge unbekannt/„nach Geschmack“, optionaler Text und kontrollierte Lebensmittelzuordnung.
- Portions-Skalierung wird vor dem Speichern sichtbar dargestellt; Import und KI nur spätere optionale Einstiege.

### Rezeptdetails

- gut lesbare Zutatenliste und Schritte auf kleinem Bildschirm;
- Portionen-Steuerung passt Mengen live an;
- Aktion „Zum Plan“ oder „Einkaufsliste erstellen“; Änderungen markieren, wenn noch nicht gespeichert.

### Einkaufsliste

- gruppiert nach Lebensmittelkategorie, aber einzelne Posten mit Menge/Einheit und Rezeptursprung nachvollziehbar;
- große Touch-Ziele für Erledigt-Markierung, manuelles Hinzufügen und Rückgängigmachen;
- Offline verfügbarer Stand mit Zeitpunkt; ausstehende Änderungen deutlich markieren;
- bei Konflikt aktuelle und lokale Änderung anzeigen statt still überschreiben.

### Angebote und Empfehlung

- Filiale/Region bzw. Auswahlstatus deutlich anzeigen.
- Angebot zeigt Produkt, Packungsgröße, Preis und Grundpreis nur, wenn bekannt, Gültigkeit, Bedingungen, Quelle und Erfassungszeit.
- Mock-Daten prominent als synthetisch kennzeichnen.
- Empfehlung zerlegt Kosten pro Packung/Markt und listet ungepreiste oder nicht gematchte Positionen separat.
- Keine Nutzerführung, die fehlende Preise als 0 oder eine unvollständige Lösung als „günstigste“ tarnt.

## Mobile-first-Interaktionen

- Formulare schrittweise, aber ohne Datenverlust beim Zurückgehen; automatische Entwürfe nur lokal mit klarer Statusanzeige.
- Eingabefelder und Buttons iPhone-tauglich, Tastaturtypen für Menge/Text passend, sichere Abstände zu Notch/Bottom-Bar.
- Keine Hover-only-Bedienung. Screenreader-Labels, Fokuszustände, ausreichender Kontrast, skalierbare Schrift und sichtbare Fehler je Feld.
- Toasts allein reichen nicht für kritische Ergebnisse; Speichern, Offlinezustand und Synchronisationskonflikte bleiben sichtbar.
- Loading-, Leer-, Fehler-, veraltete Daten- und „nicht verfügbar“-Zustand für jede externe Quelle.

## PWA und Offline

- Service Worker cacht App-Shell und statische Assets.
- Nur bewusst ausgewählte, nutzerbezogene Rezepte/Listen zur Offline-Lektüre in IndexedDB cachen; keine Auth-Tokens im Browser-Storage.
- Einkaufliste kann lokal abgehakt werden; Änderungen werden als wartend markiert und erst bei Verbindung mit Versionsprüfung synchronisiert.
- Keine Offline-Angebotsaktualität behaupten: Zeitpunkt des letzten Abrufs anzeigen und abgelaufene Angebote aus Empfehlungen ausschließen.
- Mehrgerät-Synchronisierung erfolgt über API; bei Kollision Nutzerentscheidung, nicht blindes Last-Write-Wins.

## Akzeptanzkriterien für den ersten Durchstich

- iPhone-Breite: Rezept anlegen, speichern und wieder öffnen ohne horizontales Scrollen.
- Rezept mit Portionszahl skalieren; Zutatenmengen bleiben nachvollziehbar.
- Einkaufsliste erstellen, Einträge ergänzen und erledigt markieren; Mock-Angebote für zugeordnete Produkte sehen.
- Fehlende Match-/Preis-/Filialdaten und synthetische Mock-Daten sind eindeutig gekennzeichnet.
- PWA bleibt bei Anbieterfehler bedienbar und gibt keinen Erfolg vor, wenn die API-Synchronisierung fehlgeschlagen ist.
