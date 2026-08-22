# Screen-by-screen UI specification for Cursor

Use screenshots as the visual/pixel authority. Do not modernize the UI. Keep the dense classic Wilken layout.

## Screen 0 — Wilken Home

- Application title: `1/02 - Frank Krauss GmbH & Co. KG - Wilken_CS/2_Finanzmanagement`
- left navigation width about 288 px in the replica reference window
- Wilken logo at top of left pane
- search box directly below logo
- `Navigation` header bar
- white navigation tree
- lower accordion bars: Navigation / Favoriten / Aktive Fenster / Anmeldevarianten
- main workspace is blue Wilken branding area
- no report title/menu/toolbar on the Home workspace
- no right context pane
- bottom status bar remains

## Screen 1 — Prozesse verwalten

- left navigation unchanged
- top blue title: `Anlagenbuchhaltung - Prozesse verwalten`
- menu: `Allgemein`, `Hilfe`
- toolbar immediately below
- right pane: `Folgeaktionen`
- main grid starts near upper-left of workspace
- tab/header `Allgemein`
- grid columns exactly as listed in `AUTOMATION_WORKFLOW.md`
- bottom section `Auswahl`

## Screen 2 — Zugangsliste erstellen

- title: `Anlagenbuchhaltung - Zugangsliste erstellen`
- report header is above tabs
- `Prozess` top input blank/yellow, process `001` below it
- `Bezeichnung`: `D` + `Zugangsliste`
- tabs immediately below header
- Steuerung content is a 3-column composition:
  - left: Modus then Auswahl
  - center: Laufsteuerung then Erstellung/Buchungsart
  - right: Status then Berechtigung/Fremdwährung
- right context pane starts below toolbar, not at window top

## Screen 3 — Anlagenspiegel erstellen

Same shell as Screen 2, with:

- process `001` or `003`
- description matching process
- tabs: Steuerung / Sortierung für Liste / Laufprotokoll
- detailed process 001 has Zugänge/Abgänge/Umbuchung checked
- condensed process 003 has those unchecked

## Screen 4 — Sortierung für Liste

- first section: `Sortierung`
- columns: Sortierkriterium / Summen / Seitenwechsel
- five rows
- first values Hauptkonto, Anlagennummer
- lower section: `Betragseinschränkung`
- AHK and Restbuchwert rows

## Screen 5 — Confirmation modal

Zugangsliste:

- title `Zugangsliste`
- `Die angeforderte Liste erstellen?`

Anlagenspiegel:

- title `Anlagenspiegel`
- `Anlagenspiegel erstellen?`

Buttons centered: `Ja`, `Abbrechen`.

## Screen 6 — Fortschritt

Small modal centered over the report screen. Parent controls are disabled while visible.

Zugang visible state:

- `Ermitteln der Werte gestartet.`

Anlagenspiegel states:

- `Anlagenselektion gestartet.`
- `Ermitteln der Werte gestartet.`
- `Der Anlagenspiegel wird erstellt.`

## Screen 7 — Liste anzeigen base

Before Druckauswahl:

- title `Anlagenbuchhaltung - Liste anzeigen`
- large white list area
- bottom centered buttons `Alle auswählen` and `Auswahl aufheben`
- right pane still present

## Screen 8 — Druckauswahl

Large modal centered over Screen 7.

- five columns: Sortierung / Von-nur Wert / Bis Wert / Auswahl / E-A
- 13 filter rows
- `Listenname` radio selected
- values 1 / 02 / 3-0 / CSA / BHL as documented
- `Alle anzeigen` near lower-left
- Start / Abbrechen below

## Screen 9 — Spool list

- Screen 7 title remains
- dense DataGrid with many historical rows
- blue `CSA` metadata rows
- following `STOP` description/path rows in dark red/green
- two-line logical entries must remain adjacent after sorting
- current report run adds protocol pair + data pair
- bottom two buttons remain

## Screen 10 — Spool context menu

Right-click newest matching PRT metadata row.

Menu includes:

- Listenanzeige
- Listendruck
- Ändern
- Druckauftrag löschen
- Auswahl
- Übersicht
- Drucker starten
- Drucker anhalten
- Format
- Filter
- Trenner-Position
- Suchen
- Drucken
- In Zwischenablage kopieren
- Export
  - Exportiere als HTML-Format in Excel
  - Erweitert

## Screen 11 — System - Gitterbox-Export (initial)

- title `System - Gitterbox-Export`
- menu `Allgemein`, `Optionen`, `Hilfe`
- right context pane hidden; main area expands
- Beschreibung
- Tabellenname `CTLP1`
- Menge gesamt on right
- Format radio stack CSV / XML / HTML / XLS / XLSX
- **XLS initially selected**
- Ziel Browser / Excel / Staroffice Calc / Zwischenablage; Excel selected
- Datensätze Alle / Markierte / Nicht markierte / Von-bis; Alle selected
- range starts at 1 and ends at report count

## Screen 12 — Gitterbox after XLSX selection

Same screen, only format changes to XLSX. Execute/check action triggers export.

## Visual rules

- no Material/Fluent redesign
- no rounded cards
- no large whitespace
- classic compact controls
- pale blue/gray Wilken surfaces
- blue title strip
- narrow toolbar buttons
- flat section separators rather than modern boxed panels
- preserve labels even when values are blank
- preserve right pane/context counters where recorded
- use screenshots to adjust x/y placement and widths; do not change behavioral names/AutomationIds while visually tuning
