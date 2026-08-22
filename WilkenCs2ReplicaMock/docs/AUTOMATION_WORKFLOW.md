# Exact Wilken CS/2 export workflow for Cursor / FlaUI

## Purpose

This document is the behavioral source of truth for the .NET WPF Wilken CS/2 replica and for the later FlaUI automation worker. It is reconstructed from the three unique supplied recordings:

- `ALDI 1.webm` / `ALDI 1(1).webm` (the two ALDI files are byte-for-byte identical)
- `Zugangsliste 1.webm`
- `Anlagenspiegel Detailliert nach Anlagen 1.webm`

The automation must be state-driven. Do not replace modal/state detection with fixed sleeps.

## Common state machine

`HOME → NAVIGATING → REPORT/PROCESS_MANAGER → CONFIGURED → CONFIRMATION → GENERATING → REPORT_READY → LISTE_ANZEIGEN → DRUCKAUSWAHL → SPOOL_LOADED → TARGET_PRT_SELECTED → CONTEXT_EXPORT → GITTERBOX_EXPORT → XLSX_SELECTED → EXPORT_TRIGGERED → FILE_DETECTED → FILE_STABLE → FILE_VALIDATED → RENAMED → SUCCESS`

## Common shell behavior

1. Application starts on the Wilken home workspace.
2. Left navigation remains visible.
3. Report/process screens use the blue title strip, menu row, toolbar row, main workspace, right context pane, and bottom status bar.
4. `System - Gitterbox-Export` hides the right context pane and uses the wider work area.
5. `Liste anzeigen` is a real intermediate screen; do not jump directly to the spool grid.

---

# Workflow A — Zugangsliste

## A1. Open the report

Navigation path used by the replica:

`Anlagenbuchhaltung → Prozesse → Einzeldefinitionen → Zugangsliste erstellen`

Expected title:

`Anlagenbuchhaltung - Zugangsliste erstellen`

Expected header:

- label `Prozess`
- top process input = blank, yellow/editable
- process below = `001`
- label `Bezeichnung`
- language = `D`
- description = `Zugangsliste`
- tabs = `Steuerung`, `Laufprotokoll`

## A2. Verify Steuerung

### Modus

- `Aktiv` = checked
- `Automatisch deaktivieren` = unchecked
- `Laufprotokoll` = unchecked

### Laufsteuerung

- `Rhythmus` = blank
- `Nächstes Laufdatum` = blank
- `Letztes Laufdatum` = `19.08.2026` before the recorded run

### Status

- `Bearbeitungszustand` = `OK`
- `Letzter Returncode` = blank
- `Letzte Fehlernummer` = blank

### Auswahl

- `Art` = `Bericht`
- `Bericht` = `NACH ANLAGEN`
- `Fachbereich` = `Handelsrecht`
- `Wertart/Plan` = `Ist`
- secondary plan/code field = `0`
- column labels = `Von` / `Bis`
- `Zugangsdatum` = `01.01.2020` to `31.12.2020`
- `Zeitraum` = `01 / 2020` to `12 / 2020`

### Erstellung

- `Art` = `Druckversion`
- `Summe für Anlagenhauptnummer` = unchecked
- `Mit Umbuchungs-Gegenkonto bei Kontenselektion` = unchecked

### Buchungsart

- `Umbuchung Bilanzposition` = checked
- `Umbuchung Anlage` = checked

### Berechtigung / Fremdwährung

Visible fields must remain present:

- `Verwalten`
- `Ausführen`
- `Währungsschlüssel`
- `Kennzeichen`

Before Execute, capture correlation information:

`report=Zugangsliste, process=001, fachbereich=Handelsrecht, period=01/2020-12/2020, user=BHL, runStartedAt=<current time>`

## A3. Execute and confirm

Click the toolbar Execute/check action.

Expected modal:

- window title: `Zugangsliste`
- text: `Die angeforderte Liste erstellen?`
- buttons: `Ja`, `Abbrechen`

Click `Ja`.

## A4. Wait for report processing

Expected progress window:

- title: `Fortschritt`
- recorded visible message: `Ermitteln der Werte gestartet.`

Do not navigate while the progress window exists. Continue only after it closes.

After the run, the mock adds **two logical spool outputs** and increases `Protokolle/Listen` by 2:

1. protocol output: `B024 / PRT` followed by `STOP  Protokoll: Zugangsliste`
2. data output: `5J0102 / 001` followed by the Zugangsliste report description

`Letztes Laufdatum` becomes `21.08.2026` in the replica.

## A5. Open Liste anzeigen

Navigate to:

`Anlagenbuchhaltung → Liste anzeigen`

Expected main title first:

`Anlagenbuchhaltung - Liste anzeigen`

The underlying screen becomes visible before the dialog:

- white/blank list area
- bottom button `Alle auswählen`
- bottom button `Auswahl aufheben`

Then the `Druckauswahl` dialog opens.

## A6. Druckauswahl

Expected dialog title:

`Druckauswahl`

Expected columns:

- `Sortierung`
- `Von/nur Wert`
- `Bis Wert`
- `Auswahl`
- `E/A`

Expected sort/filter rows:

1. Konzern
2. Mandant
3. Werk
4. Version/Release
5. Gebiet
6. Listenname
7. Programmname
8. Terminal
9. Drucker
10. Erstelldatum
11. Status
12. Benutzer
13. Listenbezeichnung

Recorded/default values represented by the mock:

- Konzern = `1`
- Mandant = `02`
- Version/Release = `3 / 0`
- Gebiet = `CSA`
- Benutzer = `BHL`
- sorting radio = `Listenname`
- `Alle anzeigen` = unchecked

Click `Start`.

## A7. Spool list — exact recorded selection behavior

The spool contains many historical rows. Each logical output is represented by a blue `CSA` metadata row and a following `STOP` description/path row.

**Important correction from the video re-review:** in the supplied recording, the user right-clicks the newest matching **protocol metadata row**, not the green data-description row.

For Zugangsliste, the recorded target pattern is:

- blue metadata row: `CSA ... B024 ... PRT ... BHL ...`
- following description row: `STOP  Protokoll: Zugangsliste`

The mock therefore requires the automation workflow to find/select the newest `B024 / PRT` row for the current run.

Correlation rules:

- Gebiet = `CSA`
- Mandant = `02`
- Listenname = `B024`
- Erweiterung = `PRT`
- Benutzer = `BHL`
- creation date/time >= `runStartedAt`
- following STOP line = `Protokoll: Zugangsliste`

Do **not** select an arbitrary row simply because it is highlighted.

## A8. Context menu

Right-click the selected blue `CSA / B024 / PRT` row.

Follow exactly:

`Export → Erweitert`

Do not choose:

`Exportiere als HTML-Format in Excel`

## Critical: how the export is actually started

There is **no separate text button named `Export` inside the Gitterbox form** in the recordings.
After `right-click → Export → Erweitert` opens `System - Gitterbox-Export`, configure the fields and then click the **green check / execute icon in the top Wilken toolbar**.

Exact action:

`XLS → XLSX` → keep `Ziel = Excel` → keep `Datensätze = Alle` → click **top toolbar green ✓ (`Export ausführen`)** → wait for the `.xlsx` file.

For FlaUI, keep the stable automation id `Toolbar_Execute`; when the current screen is `System - Gitterbox-Export`, that control means **Export ausführen**, not report generation.

## A9. System - Gitterbox-Export

Expected title:

`System - Gitterbox-Export`

Expected fields/state:

- `Beschreibung` = blank
- `Tabellenname` = `CTLP1`
- `Menge gesamt` = `37`
- initial Format = **XLS**
- Ziel = `Excel`
- Datensätze = `Alle`
- Von/bis = `1` to `37`

Format choices visible:

- CSV
- XML
- HTML
- XLS
- XLSX

Explicitly change:

`XLS → XLSX`

Then click the toolbar Execute/check action.

## A10. File completion

The recording does not show an additional Wilken success modal. Detect the exported file outside the report page.

Expected generated name in the replica:

`CTLP12.xlsx`

If it already exists:

`CTLP12 (1).xlsx`, `CTLP12 (2).xlsx`, etc.

Automation completion checks:

1. new `.xlsx` created after export trigger
2. size stops changing
3. file can be opened without write lock
4. XLSX ZIP/package structure is valid
5. rename/move to deterministic job path
6. only then mark Success

---

# Workflow B — Anlagenspiegel Detailliert nach Anlagen

## B1. Open

Navigation:

`Anlagenbuchhaltung → Prozesse → Einzeldefinitionen → Anlagenspiegel erstellen`

Expected title:

`Anlagenbuchhaltung - Anlagenspiegel erstellen`

Expected header:

- top process input = blank/yellow
- process below = `001`
- language = `D`
- description = `Anlagenspiegel nach Anlagen`
- tabs = `Steuerung`, `Sortierung für Liste`, `Laufprotokoll`

## B2. Sortierung für Liste

The recording exposes this tab; it must not be empty.

Replica values:

- sort row 1 = `Hauptkonto`
- row 1 `Summen` = checked
- row 1 `Seitenwechsel` = unchecked
- sort row 2 = `Anlagennummer`
- remaining sort rows = blank
- `Betragseinschränkung`
- `AHK` = `0.00`
- `Restbuchwert` = `0.00`

Return to `Steuerung` before execution.

## B3. Steuerung

### Modus

- `Aktiv` = checked
- `Automatisch deaktivieren` = unchecked
- `Laufprotokoll` = checked

### Auswahl

- `Art` = `Kompletter Datenbestand`
- `Bericht` = blank/disabled
- `Fachbereich` = `Steuerrecht`
- `Wertart/Plan` = `Ist`
- secondary value = `0`
- `Zeitraum` = `01 / 2021` to `12 / 2021`

### Erstellung

- `Art` = `Druckversion`
- `Summe für Anlagenhauptnummer` = unchecked

### Einzelne Buchungen für Anlagen

- `Zugänge` = checked
- `Abgänge` = checked
- `Umbuchung Anlage` = checked

Capture `runStartedAt` before execution.

## B4. Execute / confirmation

Click Execute.

Expected modal:

- title: `Anlagenspiegel`
- text: `Anlagenspiegel erstellen?`
- buttons: `Ja`, `Abbrechen`

Click `Ja`.

## B5. Progress states

Wait through the same `Fortschritt` window as its text changes:

1. `Anlagenselektion gestartet.`
2. `Ermitteln der Werte gestartet.`
3. `Der Anlagenspiegel wird erstellt.`

The value-calculation stage is the longest visible processing period.

A successful run creates two spool outputs:

1. `B015 / PRT` + `STOP  Protokoll: Anlagenspiegel`
2. `4J0402 / 001` + `STOP  Anlagenspiegel nach Anlagen ...`

## B6. Liste anzeigen / Druckauswahl

Repeat A5 and A6 exactly.

## B7. Select the recorded PRT row

For the recorded detailed Anlagenspiegel export, select the newest current-run:

- Gebiet = `CSA`
- Listenname = `B015`
- Erweiterung = `PRT`
- Benutzer = `BHL`
- creation time >= `runStartedAt`
- next STOP row = `Protokoll: Anlagenspiegel`

Right-click that metadata row.

## B8. Export

`Export → Erweitert`

Expected Gitterbox values:

- Tabellenname = `CTLP1`
- Menge gesamt = `31`
- initial Format = `XLS`
- Ziel = `Excel`
- Datensätze = `Alle`
- range = `1` to `31`

Change `XLS → XLSX`, then Execute and validate the generated file.

---

# Workflow C — Alle Anlagen nach Konten verdichtet (ALDI recording)

## C1. Start from Home

The ALDI recording begins at the Wilken home workspace.

Navigation:

`Anlagenbuchhaltung → Prozesse → Prozesse verwalten`

## C2. Prozesse verwalten

Expected title:

`Anlagenbuchhaltung - Prozesse verwalten`

Visible grid columns:

- Mandant
- Werk
- Programm
- Prozess
- Bezeichnung
- Status
- Zustand
- Prozess (code)
- Letztes Laufdatum
- Nächstes Laufdatum
- Rhythmus

Find/open:

- Mandant = `02`
- Programm = `CAB015`
- Prozess = `003`
- Bezeichnung = `Alle Anlagen nach Konten verdichtet`
- Status = `AKTIV`
- Zustand = `OK`
- process code = `CA45`

Bottom `Auswahl` area remains visible with:

- `Prozess`
- `Alle Mandanten anzeigen`
- `Anzeige = Alle Prozesse`
- `Status ändern = Keine`

Right pane is `Folgeaktionen` with:

- `Suchen`
- `Logging`
- `Bezeichnung`

Double-click/Enter the process 003 row.

## C3. Anlagenspiegel screen for process 003

Expected title:

`Anlagenbuchhaltung - Anlagenspiegel erstellen`

Header:

- process = `003`
- description = `Alle Anlagen nach Konten verdichtet`

Steuerung:

- Aktiv = checked
- Laufprotokoll = checked
- Art = `Kompletter Datenbestand`
- Fachbereich = `Steuerrecht`
- Wertart/Plan = `Ist`
- Zeitraum = `01/2021` to `12/2021`
- Erstellung = `Druckversion`
- Zugänge = unchecked
- Abgänge = unchecked
- Umbuchung Anlage = unchecked

Right context pane in this flow is `Folgeaktionen`; bottom right includes recorded `Protokolle/Listen` / `Folgeaktionen` counters.

## C4. Execute

Confirmation:

`Anlagenspiegel erstellen?` → `Ja`

Progress states:

1. `Anlagenselektion gestartet.`
2. `Ermitteln der Werte gestartet.`
3. `Der Anlagenspiegel wird erstellt.`

## C5. Spool/export

Navigate through:

`Liste anzeigen → Druckauswahl → Start → spool`

Select the newest current-run:

- `B015 / PRT`
- user `BHL`
- next STOP row = `Protokoll: Anlagenspiegel`

Then:

`right-click → Export → Erweitert`

Expected Gitterbox:

- `Tabellenname = CTLP1`
- `Menge gesamt = 23`
- initial Format = `XLS`
- Ziel = `Excel`
- Datensätze = `Alle`
- range = `1` to `23`

Change to `XLSX`, then Execute.

---

# Timing reference

Observed end-to-end recording durations:

- Zugangsliste: about `38 s` in the supplied recording; user observation is approximately `30 s`
- Anlagenspiegel Detailliert nach Anlagen: about `66.4 s`; user observation is approximately `65 s`
- ALDI process-003 recording: about `126.9 s` including navigation/process-manager/manual interaction

The replica uses configurable **application-owned wait phases** in `appsettings.json`. These are not intended to reproduce human hesitation between clicks. Automation must wait for controls/windows/state changes, with a timeout, rather than sleeping for the nominal duration.

# Locator policy

Use this order:

`AutomationId → ControlType + Name → Parent/child relation → keyboard → image match → coordinates`

Coordinates are last resort because the actual application is presented through a Windows/Citrix-style environment.

# Critical invariants for Cursor

- Never skip confirmation dialogs.
- Never skip `Liste anzeigen` before `Druckauswahl`.
- Never skip `Druckauswahl`.
- Never assume the spool has one row.
- Reproduce the recorded selection of the newest matching **PRT metadata row**.
- Never assume XLSX is preselected; recorded initial format is `XLS`.
- `Datensätze = Alle` applies to records of the selected export context; it does not mean “all spool rows”.
- No artificial “Export completed” modal.
- File existence alone is not Success; validate file stability and XLSX structure.
