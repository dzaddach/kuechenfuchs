# Küchenfuchs – KI-Integration

**Stand:** 10. Oktober 2026 · **Status:** Vorschlag; KI-Rezeptverwaltung ist frühe Kernfunktion

## Zweck und Grenze

KI unterstützt bei Rezeptideen und optional beim Zuordnen unklarer Lebensmittel. Sie ist weder Preisquelle noch Verfügbarkeits-, Ernährungs- oder Sicherheitssystem. Angebotsabgleich und Mengenberechnung sind regelbasiert und testbar; KI darf diese Ergebnisse nicht erfinden oder überschreiben.

## Geplante Abläufe

1. Nutzer wählt den KI-Assistenten bewusst und gibt Rezeptwunsch sowie optionale Zutaten/Portionen an.
2. PWA sendet den Auftrag an die Küchenfuchs-API; OpenAI-Schlüssel und Anbieterlogik verbleiben serverseitig.
3. Backend sendet nur notwendige Daten und verlangt schema-konformes strukturiertes Ergebnis.
4. Backend validiert Struktur, Größenlimits, Portionen, Mengen/Einheiten, Allergene/Constraints und Textfelder; unplausible Werte werden zurückgewiesen oder als unsicher gekennzeichnet.
5. UI zeigt Entwurf mit Änderungsmöglichkeit. Es gibt keinen automatischen Schreibvorgang.
6. Erst Nutzerbestätigung speichert über den normalen Rezept-Endpunkt.

Die KI-Rezeptverwaltung wird nach dem manuellen Rezept-Grundablauf in einem frühen Arbeitspaket geliefert. Optionale Lebensmittelzuordnung durch KI darf nur Kandidaten samt Konfidenz/Begründung liefern. Niedrige Sicherheit führt zur manuellen Auswahl. Sie ist nicht Voraussetzung für Einkaufsmengen oder Angebotsberechnung.

## Strukturierter Vertragsentwurf

```json
{
  "title": "string",
  "description": "string",
  "servings": 4,
  "ingredients": [
    {
      "displayName": "string",
      "foodSuggestion": "string|null",
      "amount": 250,
      "unit": "g",
      "preparation": "string|null",
      "optional": false
    }
  ],
  "steps": ["string"],
  "warnings": ["string"]
}
```

Das ist ein Anwendungs-Datentransferformat, kein unveränderlicher Modellvertrag. Backend legt Limits fest: maximale Zutaten-/Schrittzahl, Textlängen, positive Portionszahl, bekannte Einheitencodes bzw. explizit unbekannte Einheit, Dezimalgrenzen und Schema-Version. Modellantworten bleiben nicht vertrauenswürdig: JSON-Schema allein prüft keine fachliche Korrektheit, Allergensicherheit oder Essbarkeit. Freitext wird escaped und als Text, nicht als HTML, gerendert.

## Datenschutz und Sicherheit

- Vor externem Senden genau anzeigen, welche Eingabe verarbeitet wird; keine Haushaltsidentität, E-Mail, Einkaufshistorie oder nicht relevante Rezeptdaten mitschicken.
- Keine API-Schlüssel im Angular-Bundle, Browser-Speicher, Prompt oder Repository. Schlüssel im Backend via Secrets.
- Keine vollständigen Prompts/Antworten standardmäßig loggen; Metriken möglichst ohne Inhaltsdaten.
- Nutzer muss KI-Funktion und Anbieterverarbeitung bewusst auslösen; klare Information zu Drittland-/Anbieterbedingungen, Aufbewahrung und geltenden Datenschutzpflichten vor produktiver Nutzung.
- OpenAI dokumentiert, dass API-Inhalte standardmäßig nicht für Training genutzt werden, aber Abuse-Monitoring-Daten standardmäßig bis zu 30 Tage aufbewahrt werden können; Speicherung von Anwendungszustand hängt von Endpunkt/Funktion ab. Für konkrete Funktionen und Policies vor Aktivierung erneut prüfen. Keine Zusicherung von Zero Data Retention ohne bestätigte Berechtigung.
- Prompts dürfen keine privilegierte Aktion auslösen. Nur validierte DTOs erreichen Speicherrouten. Modelltext wird nicht als System-/Tool-Instruktion ausgeführt.
- Rate-Limits, Ausgaben-/Zeitlimits, monatliches konfigurierbares Budget sowie Circuit Breaker. Bei Budget-/Anbieterausfall Fehler explizit zeigen, nicht leere erfolgreiche Vorschläge zurückgeben.

## Kostenkontrolle

Anbieterpreise und Modellnamen ändern sich; in dieser Planung werden keine Beträge oder fixen Tokenkosten behauptet. Vor Freigabe Modell und aktuelle offizielle Preisübersicht festhalten. Pro Aufruf wird Kostenverbrauch anhand tatsächlich gemeldeter Eingabe-/Ausgabetokens und des am Nutzungstag konfigurierten Tarifs berechnet. Im Betrieb:

- Modell/Preis konfigurierbar statt im Code fest verdrahtet;
- maximale Prompt- und Ausgabelänge, Zeitlimit und Rate Limit;
- Monatsbudget und Alarm/Deaktivierung bei Überschreitung;
- Nutzungsstatistik aggregiert, ohne Inhaltsprotokollierung;
- KI ist für den manuellen Rezept-Grundablauf und den späteren Einkauf nicht erforderlich; der vereinbarte frühe Produktumfang enthält jedoch die KI-Rezeptentwurfsfunktion.

## Teststrategie

- Schema- und Domainvalidierung mit gültigen/ungültigen Beispielen; unbekannte Einheiten, leere Ausgabe, überlange Texte und ungültige Mengen.
- API-Tests mit gefälschtem KI-Client (kein Netzwerk und keine Kosten), Timeout, Providerfehler, Refusal und Budgetüberschreitung.
- UI-Test: Entwurf bleibt ungespeichert, bis Speichern explizit bestätigt wird; KI-Ausfall verhindert die manuelle Rezeptverwaltung nicht.
- Regression: von der KI erzeugte Daten durchlaufen dieselbe Rezeptvalidierung wie Nutzereingaben.

## Quellen (abgerufen 10.10.2026)

- OpenAI, Structured Outputs: <https://developers.openai.com/api/docs/guides/structured-outputs> — JSON-Schema-konforme Ausgabe; ersetzt keine fachliche Validierung.
- OpenAI, Data Controls: <https://developers.openai.com/api/docs/guides/your-data> — Angaben zu API-Datennutzung, Abuse-Monitoring und Aufbewahrung; bei Implementierung erneut prüfen.
- OpenAI, API Pricing: <https://developers.openai.com/api/docs/pricing> — dynamische Preisübersicht; vor Kostenfreigabe konkretes Modell/Preis erfassen.
