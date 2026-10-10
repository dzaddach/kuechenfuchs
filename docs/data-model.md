# Küchenfuchs – Datenmodell

**Stand:** 10. Oktober 2026 · **Status:** Architekturvorschlag

## Grundregeln

- **Rezeptzutat ≠ Lebensmittel ≠ Händlerprodukt ≠ Angebot.** Diese vier Konzepte haben eigene Identitäten und Lebenszyklen.
- Mengen und Preise werden dezimal, nicht als binäre Gleitkommazahlen gespeichert.
- Ein Angebot ist zeit- und regionsgebunden; aus einem Produktpreis wird kein dauerhafter Lebensmittelpreis.
- Herkunft (Rezept, Angebot, Nutzer, Import) und Unsicherheit bleiben nachvollziehbar.
- Mengen, die nicht sicher umgerechnet oder aggregiert werden können, werden nicht stillschweigend zusammengeführt.

## Entitäten und Beziehungen

| Entität | Wesentliche Felder / Zweck | Beziehungen |
|---|---|---|
| `User` | interne ID, Anmeldeidentität, Status, Zeitstempel | Mitglied in einem oder mehreren Haushalten |
| `Household` | ID, Name, Standardregion/Filialauswahl optional, Zeitstempel | besitzt Rezepte, Wochenpläne, Listen und Einstellungen |
| `HouseholdMember` | Haushalt, Benutzer, Rolle, Status | eindeutige Mitgliedschaft; Autorisierung jeder Haushaltsressource |
| `Recipe` | ID, Haushalt, Name, Beschreibung, Basisportionen, Status, Zeitstempel/Concurrency-Version | enthält Zutatenzeilen, Schritte und Tags |
| `RecipeIngredient` | Rezept, Reihenfolge, optional `FoodId`, Rohbezeichnung, Dezimalmenge, Einheit, Zubereitungshinweis, optionale Mindest-/Höchstmenge | viele zu einem Rezept; optional zu normalisiertem Lebensmittel |
| `RecipeStep` | Rezept, Reihenfolge, Text, optionale Dauer/Temperatur | mehrere Schritte je Rezept |
| `Food` | normalisierte Lebensmittel-ID, kanonischer Name, Kategorie, optionale Synonyme/Markierungen | verbindet Rezeptzutaten mit Produkten; keine Preise |
| `Unit` | Code, Anzeige, Dimension (Masse, Volumen, Stück, Länge o.ä.), Basisfaktor | definierte Einheitenkonversionen |
| `FoodConversion` | Lebensmittel, Quell-/Zieleinheit, Faktor, Provenienz/Vertrauen | nur für lebensmittelspezifische Konversionen (z. B. Volumen↔Masse) |
| `Retailer` | Händler-ID, Anzeigename, Status | besitzt Filialen und Produkte |
| `Store` | Händler, optionale externe Filial-ID, Name, Adresse/Region, Zeitzone | Standortkontext eines Angebots; nur speichern, was nötig und verfügbar ist |
| `RetailerProduct` | Händler, externe Produkt-ID falls vorhanden, Titel/Marke, Packungsinhalt und -einheit, optionale EAN, Erfassungsprovenienz | kann über eine Zuordnung mehrere normalisierte Lebensmittel repräsentieren, aber keine Rezeptzutaten ersetzen |
| `FoodProductMatch` | Lebensmittel, Produkt, Zuordnungsart, Konfidenz/Quelle, geprüft-am, Prüfer | viele-zu-viele und überprüfbar; automatische Treffer bleiben Vorschläge |
| `Offer` | Produkt, Händler/Filial-/Regionsbezug, Angebotswert, Währung, Gültig-ab/bis, erfasst-am, Quelle, Status, Bedingung (z. B. Coupon) | zeitlich gültig; Preis/Grundpreis nur wenn Quelle belastbar vorliegt |
| `MealPlan` | Haushalt, Zeitraum/Woche, Status | enthält Planpositionen |
| `MealPlanEntry` | Plan, Datum/Mahlzeit, Rezept, geplante Portionen | erzeugt Einkaufsbedarf |
| `ShoppingList` | Haushalt, optionale Planreferenz, Name, Status, Änderungs-/Concurrency-Version | enthält Einträge und Synchronisationszustand |
| `ShoppingListItem` | Liste, optional `FoodId`, Anzeigename, Menge/Einheit oder freier Posten, erledigt, Herkunftsreferenzen, Notiz | aggregierter Bedarf; Herkunftszeilen dürfen für Erklärbarkeit erhalten bleiben |
| `RecommendationRun` | Haushalt, Erstellungszeit, Region, verwendeter Angebotsstand, Kriterien, Zusammenfassung | erzeugt Einkaufsoptionen und begründet Positionen |
| `RecommendationItem` | Durchlauf, Listenposition, Produkt/Angebot optional, Packungsanzahl, geschätzte bekannte Kosten, Status/Grund | erlaubt „nicht bepreist“ und „kein sicherer Treffer“ statt erfundener Werte |

Für den MVP können `RecipeStep`, `FoodConversion`, Favoriten/Tags und die Recommendation-Entitäten reduziert oder erst bei Bedarf eingeführt werden. Haushaltsbezug und die Trennung der vier Kerndomänen sollen dagegen von Beginn an feststehen. Keine Entität erzwingt bereits einen Händler-Connector.

## Mengen- und Einheitenkonzept

### Speicherung und Skalierung

- `RecipeIngredient.Amount` ist `decimal`/`numeric` mit angemessener Präzision und optional; Bruchteile wie 0,5 sind gültig. Null/fehlende Menge ist nicht dasselbe wie 0.
- `Recipe.BaseServings` muss positiv sein. Skalierung: `Menge × Zielportionen / Basisportionen`; Rundung erfolgt erst zur Anzeige bzw. bei expliziten Packungsentscheidungen.
- `Unit` nutzt stabile maschinenlesbare Codes (beispielsweise `g`, `kg`, `ml`, `l`, `piece`) und eine Dimension. Pro Dimension wird eine kanonische Basiseinheit definiert.
- Masse↔Masse und Volumen↔Volumen können über feste Faktoren normalisiert werden; Stück ist eine eigene Dimension. Masse↔Volumen ist ohne explizite lebensmittelspezifische Konversion unzulässig.
- Verpackungsangaben eines Händlerprodukts werden getrennt von der Rezept-Einheit gespeichert. Einheitenumrechnung allein beweist keine Produktgleichheit.

### Aggregation

Automatisch aggregieren darf die API nur, wenn das normalisierte Lebensmittel identisch ist und beide Mengen sicher in dieselbe Dimension umrechenbar sind. `2 EL` plus `100 g` wird ohne passende Lebensmittel-Konversion nicht addiert. „Salz nach Geschmack“, freie Textzeilen und optionale Zutaten bleiben einzelne Einträge. Eine Zuordnung oder Konversion mit geringer Sicherheit wird zur Nutzerbestätigung angezeigt.

### Geld und Angebot

- Geldbetrag als `numeric` plus ISO-Währung; es wird keine Fremdwährung umgerechnet.
- Grundpreis nur aus Angebotswert und nachweisbarem Packungsinhalt/-einheit berechnen; Basisgröße und Rundung fest definieren.
- Angebotsgültigkeit als Zeitintervall mit Zeitzone; unbekannte Endzeit nicht durch eine vermutete Laufzeit ersetzen.
- Händlerbedingungen wie App-Coupon, Kundenkarte, Mehrfachkauf, Filialbeschränkung und „solange Vorrat reicht“ als sichtbare Angebotsbedingungen führen.
- Veraltete, nicht bestätigte oder regional nicht passende Angebote aus der Empfehlung ausschließen oder klar als nicht verifiziert ausweisen.

## Schlüssel, Integrität und Datenschutz

- UUIDs oder vergleichbare nicht erratbare IDs; Fremdschlüssel und eindeutige Constraints für Haushaltsmitgliedschaften, stabile Produkt-IDs und Referenzen.
- Haushaltszugriff ist eine API-Autorisierungsregel und zusätzlich durch gezielte Abfragen zu begrenzen; IDs allein sind kein Berechtigungsnachweis.
- Rezept-/Listenänderungen erhalten eine Concurrency-Version (`rowversion`-ähnliche Anwendungs- oder ETag-Version), damit Geräteänderungen nicht still überschreiben.
- Angebotsrohtexte und Quellenbelege nur so lange und so umfangreich speichern, wie es für Nachvollziehbarkeit, Fehleranalyse und rechtmäßige Nutzung notwendig ist.
- Keine Speicherung kompletter Chatverläufe oder sensibler KI-Prompts standardmäßig; KI-Nutzung und Speichervorgang sind getrennt.
