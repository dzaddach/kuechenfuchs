# Küchenfuchs – KI-Integration

**Stand:** 11. Oktober 2026 · **Status:** P4-PWA-Rezeptentwurf umgesetzt; P5a-OAuth-Konzept separat dokumentiert, nicht zur Implementierung freigegeben

## Zweck und Grenze

KI unterstützt bei Rezeptideen und optional beim Zuordnen unklarer Lebensmittel. Sie ist weder Preisquelle noch Verfügbarkeits-, Ernährungs- oder Sicherheitssystem. Angebotsabgleich und Mengenberechnung sind regelbasiert und testbar; KI darf diese Ergebnisse nicht erfinden oder überschreiben.

## Ablauf

1. Nutzer wählt den KI-Assistenten bewusst und gibt Rezeptwunsch sowie optionale Zutaten/Portionen an.
2. PWA sendet den Auftrag an die Küchenfuchs-API; OpenAI-Schlüssel und Anbieterlogik verbleiben serverseitig.
3. Backend sendet nur notwendige Daten und verlangt schema-konformes strukturiertes Ergebnis.
4. Backend validiert Struktur, Größenlimits, Portionen, Mengen/Einheiten und Textfelder; unplausible Werte werden zurückgewiesen oder als unsicher gekennzeichnet. Vermeidungswünsche werden an den Anbieter übermittelt, sind aber keine Garantie für Allergenfreiheit.
5. UI zeigt den Entwurf im vollständigen vorhandenen Rezepteditor; Mengen, Einheiten, Zutaten, Schritte, Tags und Kategorie sind editierbar. Es gibt keinen automatischen Schreibvorgang.
6. Erst die Aktion **„Rezept übernehmen“** speichert über den normalen, CSRF-geschützten Rezept-Endpunkt.

Optionale Lebensmittelzuordnung durch KI wird nicht automatisch übernommen. Der P4-Vertrag liefert keine `FoodId`; textuelle Food-Vorschläge bleiben Hinweise für den Nutzer und erfordern eine spätere manuelle Zuordnung. Niedrige Sicherheit ist sichtbar. KI ist weder Preis-/Verfügbarkeitsquelle noch Voraussetzung für Einkaufsmengen oder Angebotsberechnung.

## Externe ChatGPT-Actions: Abgrenzung zu P4

Die P4-OpenAI-API-Anbindung ist ein Backend-zu-Backend-Anruf, der von der PWA ausgelöst wird. ChatGPT Actions ist ein separater OAuth-Client und darf weder den OpenAI-API-Key noch die PWA-Cookie-Session erhalten. P5a-Planung für Authorization Code, Providerwahl, Identitäts-/Haushaltszuordnung, Tokenprüfung, Widerruf, öffentliches HTTPS und Datei-/Testumfang: [ChatGPT Actions OAuth](./chatgpt-actions-oauth.md).

Empfohlen wird zunächst die Evaluation von Duende IdentityServer im ASP.NET-Core-Prozess, da dieser Weg denselben ASP.NET-Core-Identity-Benutzerbestand verwenden kann. Die Produktionslizenz für die konkrete private Nutzung und die Kompatibilität mit dem aktuellen Actions-Client müssen vor Implementierungsfreigabe geklärt werden. Keycloak bleibt eine etablierte, aber nicht direkt integrierte Alternative; dann ist sicheres Account-Linking erforderlich. Bis zur Entscheidung bleiben ASP.NET-Identity-Cookie-Login, CSRF-Schutz und P4 unverändert.

## P4-Implementierung

- Endpoint: `POST /api/v1/households/{hid}/ai/recipe-drafts`, nur angemeldet und nach Mitgliedschaftsprüfung. Jede schreibende Cookie-Anfrage verlangt das zentrale Antiforgery-Token.
- Adaptergrenze `IAiRecipeProvider`; implementierter Adapter sendet an OpenAI Chat Completions mit `response_format: json_schema`, `strict: true`, ohne Tools/Funktionsaufrufe.
- Der serverseitige Validator lehnt fehlerhaftes JSON, zusätzliche Properties, fehlende Felder, Einheiten außerhalb des globalen Anwendungskatalogs, ungültige Portionen, Zeiten, Mengen und Text-/Arraygrößen explizit ab. Mengen mit `null` bleiben unbekannt und erzeugen einen sichtbaren Prüfhinweis. Unbekannte Lebensmittel werden nicht gemappt.
- Antworttexte sind normale Strings; Angular-Interpolation escaped sie. Es gibt keinen HTML-/Markdown-Renderer, keine Speicherung und keinen automatischen API-Schreibaufruf nach Generierung.
- Bei `Refusal`, Providerfehler, Timeout, Schemafehler, fehlendem Schlüssel/Tarif/Budget oder erschöpftem Limit gibt die API einen expliziten Fehler zurück; kein leerer oder erfundener Erfolgsentwurf.
- Eingaben werden nicht in Anwendungslogs geschrieben. Providerfehler enthalten keine Prompt-/Antwortinhalte. Die aggregierte DB-Nutzung enthält ausschließlich Haushalt, Monat, Requestzahl und USD-Summen.

### Modell und Konfiguration

Ausgewähltes Anfangsmodell: **`gpt-4.1-mini`**. Begründung: die offizielle Modellseite beschreibt es als kleinere, schnellere GPT-4.1-Variante; Chat Completions und Structured Outputs sind laut aktueller Modellübersicht unterstützt. Modell-ID bleibt über `OPENAI_MODEL` austauschbar. Modell und Preise am **11.10.2026** gegen die offizielle Quelle geprüft; vor produktiver Aktivierung erneut prüfen.

OpenAI-Schlüssel bleiben ausschließlich als Backend-Konfiguration (`OPENAI_API_KEY` → `Ai__OpenAI__ApiKey`). Die Featureflag `AI_ENABLED` ist standardmäßig aus. Selbst bei eingeschalteter Featureflag bleibt der Endpoint nicht betriebsbereit, solange API-Key, positives Monatsbudget und beide Tokenpreise nicht gesetzt sind.

| `.env`-Variable | Wirkung | Beispiel-/Standardwert |
|---|---|---|
| `AI_ENABLED` | KI-Funktion explizit aktivieren | `false` |
| `OPENAI_API_KEY` | Backend-Schlüssel; nicht im Browser/Repository | leer |
| `OPENAI_MODEL` | Provider-Modell | `gpt-4.1-mini` |
| `AI_MONTHLY_BUDGET_USD` | maximale modellierte Ausgaben je Haushalt und Kalendermonat | `0` (deaktiviert) |
| `AI_INPUT_USD_PER_MILLION_TOKENS` / `AI_OUTPUT_USD_PER_MILLION_TOKENS` | Tarifwerte zur Kostenrechnung; für das gewählte Modell aktuelle offizielle Standardpreise nachschlagen | `0` (deaktiviert) |
| `AI_REQUESTS_PER_MINUTE_PER_USER` | Prozesslokales API-Ratenlimit pro angemeldetem Benutzer | `8` |
| `AI_MAX_MONTHLY_REQUESTS_PER_HOUSEHOLD` | persistente monatliche Aufrufgrenze pro Haushalt | `100` |
| `AI_MAX_INPUT_TOKENS` / `AI_MAX_OUTPUT_TOKENS` | konservative Vorab-Kostengrenze und Provider-Ausgabelimit | `16000` / `1500` |
| `AI_TIMEOUT_SECONDS` | Provider-Timeout | `30` (zulässig 1–120) |
| `AI_RESERVATION_EXPIRATION_SECONDS` | Nach dieser Zeit werden verwaiste Reservierungen bei der nächsten Reservierung konservativ als Verbrauch abgeschlossen | `180`; produktiv größer als Provider-Timeout plus Sicherheitsabstand setzen |
| `AI_MAX_PROMPT_CHARACTERS` | Promptlimit | `2000` |

Vor dem Provideraufruf wird die konfigurierte maximale Input-/Outputtoken-Nutzung zu den gesetzten Tarifen reserviert. Jede Anfrage hat eine eigene persistente Reservierungs-ID, die nur aggregierte Kosten-/Statuswerte, niemals Prompts oder Antworten enthält. Eine Haushalt-/Monatszeile wird beim Reservieren und Abrechnen gesperrt, sodass parallele Requests die gemeinsame Grenze nicht überbuchen. Bei erfolgreicher Antwort werden plausible gemeldete Tokenwerte exakt und auf acht USD-Nachkommastellen aufgerundet abgerechnet. Eine explizite Provider-Ablehnung ohne Verarbeitung (z. B. HTTP 429) gibt die Reservierung frei; Netzwerkfehler, Timeout, Abbruch oder Antworten ohne belastbare Usage werden vorsichtshalber mit der vollen Reservierung abgeschlossen. So verbleibt kein dauerhaft blockierender offener Betrag.

Verwaiste aktive Reservierungen (z. B. nach API-Prozessabbruch) werden beim nächsten Reservierungsversuch nach `AI_RESERVATION_EXPIRATION_SECONDS` als geschätzter voller Verbrauch abgeschlossen, nicht freigegeben. Wenn eine verspätete erfolgreiche Antwort später doch Usage liefert, ersetzt eine idempotente Abrechnung den geschätzten Betrag durch den gemeldeten tatsächlichen Betrag. Die Produktivkonfiguration muss die Ablaufzeit länger als Provider-Timeout plus Sicherheitsabstand setzen; der Test-Compose-Stack verkürzt sie gezielt, um parallele/verspätete Abrechnung schnell zu prüfen. Usage außerhalb der konfigurierten Tokenobergrenzen erzeugt keinen Entwurf und wird mindestens mit der maximal reservierten Summe angesetzt.

Für `gpt-4.1-mini` zeigte die offizielle Preisseite am 11.10.2026 Standardpreise von **0,40 USD/Mio. Inputtokens** und **1,60 USD/Mio. Outputtokens**. Der Beispiel-`.env` enthält absichtlich keine aktiven Preise oder ein frei erfundenes Budget. Preise ohne Cache-Rabatt eintragen, den Monatsbetrag bewusst festlegen und vor Aktivierung die aktuellen Werte bei OpenAI kontrollieren. Unterschiedliche, später gewählte Modelle erfordern passende Preiswerte.

### Lokaler Start und Fake-Provider

Für reale lokale Nutzung `.env` aus `.env.example` erstellen und nach Prüfung der aktuellen Preise `AI_ENABLED=true`, `OPENAI_API_KEY`, `OPENAI_MODEL`, `AI_MONTHLY_BUDGET_USD` und beide Tarifvariablen setzen. Compose reicht den Key nur an die API weiter. Die Entwicklungs-UI zeigt pro Aufruf die Drittanbieter-Verarbeitung an; der Benutzer muss sie bewusst bestätigen.

Automatisierte Tests benutzen ausschließlich den lokalen, profilgebundenen Fake-Dienst und keine OpenAI-Kosten:

```sh
bash scripts/test-p4-ai-recipes.sh
```

Das Testprofil setzt einen Test-Key und eine interne Fake-URL nur für den isolierten Development-Compose-Stack; es exponiert den Fake nicht am Host. In Nicht-Development-Umgebungen akzeptiert der Adapter als Provider-URL ausschließlich HTTPS zu `api.openai.com`.

## Strukturierter Vertragsentwurf

```json
{
  "title": "string",
  "description": "string",
  "servings": 4,
  "preparationMinutes": 25,
  "category": "Abendessen",
  "tags": ["schnell"],
  "ingredients": [
    {
      "displayName": "string",
      "amount": 250,
      "unitCode": "g",
      "foodSuggestion": "string|null"
    }
  ],
  "steps": ["string"],
  "warnings": ["string"]
}
```

Das ist die strukturierte Modellantwort; die API überführt sie in das bestehende Rezept-DTO (`name`, `baseServings`, Einheiten-Code, Zutaten und beschriebene Schritte). Das Modellformat verlangt alle Felder, Mengen dürfen bei Unsicherheit `null` sein. Das Backend setzt Grenzen für Zutaten/Schritte, Textlängen, positive Portionszahl, globale Einheitencodes und Dezimalstellen. Modellantworten bleiben nicht vertrauenswürdig: JSON-Schema allein prüft keine fachliche Korrektheit, Allergensicherheit oder Essbarkeit. Freitext wird escaped und als Text, nicht als HTML, gerendert.

## Datenschutz und Sicherheit

- Vor externem Senden genau anzeigen, welche Eingabe verarbeitet wird; keine Haushaltsidentität, E-Mail, Einkaufshistorie oder nicht relevante Rezeptdaten mitschicken.
- Keine API-Schlüssel im Angular-Bundle, Browser-Speicher, Prompt oder Repository. Schlüssel im Backend via Secrets.
- Keine vollständigen Prompts/Antworten standardmäßig loggen; Metriken möglichst ohne Inhaltsdaten.
- Nutzer muss KI-Funktion und Anbieterverarbeitung bewusst auslösen; klare Information zu Drittland-/Anbieterbedingungen, Aufbewahrung und geltenden Datenschutzpflichten vor produktiver Nutzung.
- OpenAI dokumentiert, dass API-Inhalte standardmäßig nicht für Training genutzt werden, aber Abuse-Monitoring-Daten standardmäßig bis zu 30 Tage aufbewahrt werden können; Speicherung von Anwendungszustand hängt von Endpunkt/Funktion ab. Für konkrete Funktionen und Policies vor Aktivierung erneut prüfen. Keine Zusicherung von Zero Data Retention ohne bestätigte Berechtigung.
- Prompts dürfen keine privilegierte Aktion auslösen. Nur validierte DTOs erreichen Speicherrouten. Modelltext wird nicht als System-/Tool-Instruktion ausgeführt.
- Ein pro angemeldetem Benutzer partitioniertes, prozesslokales Minutenlimit verhindert, dass beliebige Haushalts-IDs unbegrenzt Limiter-Partitionen anlegen. Das persistente monatliche Requestlimit und Kostenbudget gelten weiterhin je Haushalt. Bei Budget-/Anbieterausfall Fehler explizit zeigen, nicht leere erfolgreiche Vorschläge zurückgeben.

## Kostenkontrolle

Anbieterpreise und Modellnamen ändern sich. Pro Aufruf wird der tatsächliche Kostenverbrauch anhand gemeldeter Eingabe-/Ausgabetokens und des konfigurierten Nutzungstarifs berechnet; vor dem Call schützt eine Obergrenzenreservierung das Budget. Im Betrieb:

- Modell/Preis konfigurierbar statt im Code fest verdrahtet;
- maximale Prompt- und Ausgabelänge, Zeitlimit und Rate Limit;
- Monatsbudget und explizite Ablehnung bei Überschreitung;
- Nutzungsstatistik aggregiert, ohne Inhaltsprotokollierung;
- KI ist für den manuellen Rezept-Grundablauf und den späteren Einkauf nicht erforderlich; der vereinbarte frühe Produktumfang enthält jedoch die KI-Rezeptentwurfsfunktion.

## Teststrategie und Nachweise

- Schema- und Domainvalidierung mit gültigen/ungültigen Beispielen; unbekannte Einheiten, leere Ausgabe, überlange Texte und ungültige Mengen.
- API-Tests mit gefälschtem KI-Client (kein Netzwerk und keine Kosten), Timeout, Providerfehler, Refusal und Budgetüberschreitung.
- UI-Test: Entwurf bleibt ungespeichert, bis Speichern explizit bestätigt wird; KI-Ausfall verhindert die manuelle Rezeptverwaltung nicht.
- Regression: von der KI erzeugte Daten durchlaufen dieselbe Rezeptvalidierung wie Nutzereingaben.
- Compose-Integration gegen den lokalen Fake deckt Anmeldung, CSRF-Negativfälle, Fremdhaushalt, veraltete/ungültige strukturierte Ausgabe, unbekannte Einheit, ungültige Mengen/Portionen, Timeout, Provider-Ratelimit, Budgetlimit, monatliche Haushalts- und minutenbasierte Benutzerlimits, fehlende automatische Speicherung und anschließendes Speichern eines bearbeiteten Entwurfs ab.

## Quellen (abgerufen 11.10.2026)

- OpenAI, Structured Outputs: <https://developers.openai.com/api/docs/guides/structured-outputs> — JSON-Schema-konforme Ausgabe; ersetzt keine fachliche Validierung.
- OpenAI, GPT-4.1 Mini: <https://developers.openai.com/api/docs/models/gpt-4.1-mini> — Modell-ID, Modellmerkmale, Chat Completions/Structured Outputs, Standardpreise.
- OpenAI, Data Controls: <https://developers.openai.com/api/docs/guides/your-data> — Angaben zu API-Datennutzung, Abuse-Monitoring und Aufbewahrung; bei Implementierung erneut prüfen.
- OpenAI, API Pricing: <https://developers.openai.com/api/docs/pricing> — dynamische Preisübersicht; vor Kostenfreigabe konkretes Modell/Preis erfassen.
