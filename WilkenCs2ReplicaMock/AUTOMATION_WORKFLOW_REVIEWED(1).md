# Exact automation workflow for Cursor / FlaUI

## Goal

Automate the same Wilken CS/2 workflow shown in the supplied recordings:

`Open report → set/verify fields → execute → wait for report generation → open Liste anzeigen → Druckauswahl → find exact new spool report → Export → Erweitert → XLSX + Excel + Alle → export → validate downloaded XLSX`.

The worker must be state-driven. **Do not use a fixed `Thread.Sleep(30000)` or `Thread.Sleep(65000)` as the success condition.** The mock deliberately has realistic delays, but production automation must wait for UI states and use timeouts only as upper bounds.

---

# Common shell

Window title:

`1/02 - Frank Krauss GmbH & Co. KG - Wilken_CS/2_Finanzmanagement`

Persistent layout:

- Top blue application header.
- Menu: `Allgemein`, `Aktionen`, `Hilfe`.
- Toolbar below menu; execute/action icon exposed in mock as `AutomationId=Toolbar_Execute`.
- Left `Navigation` tree.
- Right side panels: `Dokumente`, `Protokolle/Listen`, `Notizen`, `Tooltips`, `Folgeaktionen`, `Informationen`.
- Bottom status bar contains user `BHL`.

Preferred locator order:

1. UI AutomationId.
2. Control type + Name/Text.
3. Parent/child relationship.
4. Keyboard navigation.
5. Image/template matching.
6. Coordinates only as the last fallback.

---

# Workflow A — Zugangsliste

## A1. Open the report screen

Navigation path:

`Anlagenbuchhaltung → Prozesse → Einzeldefinitionen → Zugangsliste erstellen`

Mock locator: `AutomationId=Nav_Zugangsliste`.

Expected screen title:

`Anlagenbuchhaltung - Zugangsliste erstellen`

## A2. Verify/set report fields

Set or verify these exact values:

| Field | Value | Mock AutomationId |
|---|---|---|
| Prozess | `001` | `Zugang_Prozess` |
| Bezeichnung | `Zugangsliste` | `Zugang_Bezeichnung` |
| Aktiv | checked | `Field_Aktiv` |
| Automatisch deaktivieren | unchecked | `Field_AutoDeactivate` |
| Laufprotokoll | unchecked | `Field_Laufprotokoll` |
| Art | `Bericht` | `Zugang_Art` |
| Bericht | `NACH ANLAGEN` | `Zugang_Bericht` |
| Fachbereich | `Handelsrecht` | `Zugang_Fachbereich` |
| Wertart/Plan | `Ist` | `Zugang_Wertart` |
| Zugangsdatum von | `01.01.2020` | `Zugang_DateFrom` |
| Zugangsdatum bis | `31.12.2020` | `Zugang_DateTo` |
| Zeitraum | `01/2020`–`12/2020` | `Zugang_Period` |
| Erstellung Art | `Druckversion` | `Field_ErstellungArt` |
| Summe für Anlagenhauptnummer | unchecked | `Field_SumMainAsset` |
| Mit Umbuchungs-Gegenkonto bei Kontenselektion | unchecked | `Zugang_Gegenkonto` |
| Umbuchung Bilanzposition | checked | `Zugang_UmbuchungBilanzposition` |
| Umbuchung Anlage | checked | `Zugang_UmbuchungAnlage` |

Before execution record a correlation object:

`{report=Zugangsliste, process=001, fachbereich=Handelsrecht, period=01/2020-12/2020, runStartedAt=<timestamp>, user=BHL}`.

## A3. Execute

Click toolbar execute/action control.

Mock locator: `AutomationId=Toolbar_Execute`.

Expected progress window:

- Window: `Fortschritt`
- Mock window AutomationId: `ProgressDialog`
- Message: `Erstellen der Zugangsliste gestartet.`
- Message locator: `Progress_Message`

Wait rule:

- Wait for the progress dialog to appear.
- Then wait for it to disappear.
- Suggested hard timeout for the real system: start with 2 minutes and make it configurable.
- Do not infer failure merely because it takes longer than the ~30 s observed benchmark.

## A4. Open spool list

After progress disappears, open:

`Anlagenbuchhaltung → Liste anzeigen`

Mock locator: `AutomationId=Nav_ListeAnzeigen`.

Expected dialog: `Druckauswahl` / `AutomationId=PrintSelectionDialog`.

## A5. Druckauswahl

The recording uses the Druckauswahl window before displaying spool rows.

Visible sort fields:

`Konzern, Mandant, Werk, Version/Release, Gebiet, Listenname, Programmname, Terminal, Drucker, Erstelldatum, Status, Benutzer, Listenbezeichnung`.

Recorded values include:

- Konzern `1`
- Mandant `02`
- Version/Release `3 / 0`
- Gebiet `CSA`
- Benutzer `BHL`
- Sortierung: `Listenname`

For robust automation, do not overfilter unless needed. At minimum verify user/mandant context and click:

`Start` / mock `AutomationId=PrintSelection_Start`.

## A6. Identify the exact new spool report

Spool grid mock locator: `AutomationId=Spool_Grid`.

Do **not** right-click the first row or any arbitrary row.

Target selection criteria, strongest to weakest:

1. Created after `runStartedAt`.
2. User = `BHL`.
3. Mandant = `02`.
4. Report description = `Zugangsliste`.
5. Fachbereich = `Handelsrecht`.
6. Period = `01.2020-12.2020`.
7. Choose newest matching creation time.

Spool presentation detail:

- The grid often shows a `CSA` metadata row followed by a `STOP` description/path row.
- Select the `CSA` metadata row logically associated with the matching `STOP Zugangsliste ...` description row.
- A `Protokoll: Zugangsliste` row is a log/protocol output and is not the same as the requested report data row.

## A7. Open advanced export

Right-click selected report row.

Context sequence:

`Export → Erweitert`

Mock AutomationIds:

- `Spool_Context_Export`
- `Spool_Context_ExportAdvanced`

Expected screen title:

`System - Gitterbox-Export`

## A8. Set export parameters

Expected values:

- Tabellenname: `CTLP1`
- Menge gesamt: `37`
- Format: `XLSX`
- Ziel: `Excel`
- Datensätze: `Alle`
- Range display: `1` to `37`

Mock locators:

- XLSX: `Export_Format_XLSX`
- Excel: `Export_Target_Excel`
- Alle: `Export_Records_All`
- Count: `Export_TotalRecords`

Always explicitly ensure `XLSX` is selected; do not trust remembered/default state.

## A9. Export and validate file

Trigger the toolbar export/action control: `Toolbar_Execute`.

Expected mock filename:

`%USERPROFILE%\Downloads\CTLP12.xlsx`

Production validation sequence:

1. Snapshot download directory before triggering export.
2. Trigger export.
3. Detect a newly created `.xlsx` file after the trigger time.
4. Wait until file size is stable across at least two checks.
5. Ensure file is not locked for exclusive write.
6. Verify ZIP/XLSX structure can be opened.
7. Verify row/header content is non-corrupt; empty-data reports should be treated according to business rules, not automatically as failure.
8. Immediately move/rename to deterministic job filename.

Recommended deterministic name:

`{Client}_{Year}_{Department}_Zugangsliste.xlsx`

---

# Workflow B — Anlagenspiegel Detailliert nach Anlagen

## B1. Open screen

Navigation path:

`Anlagenbuchhaltung → Prozesse → Einzeldefinitionen → Anlagenspiegel erstellen`

Mock locator: `Nav_Anlagenspiegel`.

Screen title:

`Anlagenbuchhaltung - Anlagenspiegel erstellen`

## B2. Verify/set fields

| Field | Value | Mock AutomationId |
|---|---|---|
| Prozess | `001` | `Anlage_Prozess` |
| Bezeichnung | `Anlagenspiegel nach Anlagen` | `Anlage_Bezeichnung` |
| Aktiv | checked | `Field_Aktiv` |
| Automatisch deaktivieren | unchecked | `Field_AutoDeactivate` |
| Laufprotokoll | checked | `Field_Laufprotokoll` |
| Art | `Kompletter Datenbestand` | `Anlage_Art` |
| Bericht | blank/disabled | `Anlage_Bericht` |
| Fachbereich | `Steuerrecht` | `Anlage_Fachbereich` |
| Wertart/Plan | `Ist` | `Anlage_Wertart` |
| Zeitraum | `01/2021`–`12/2021` | `Anlage_Period` |
| Erstellung Art | `Druckversion` | `Field_ErstellungArt` |
| Summe für Anlagenhauptnummer | unchecked | `Field_SumMainAsset` |
| Zugänge | checked | `Anlage_Zugaenge` |
| Abgänge | checked | `Anlage_Abgaenge` |
| Umbuchung Anlage | checked | `Anlage_Umbuchung` |

## B3. Execute and confirm

Click `Toolbar_Execute`.

Expected confirmation:

`Anlagenspiegel erstellen?`

Click `Ja` / `AutomationId=Confirm_Yes`.

## B4. Wait through both processing phases

Observed phase 1:

`Ermitteln der Werte gestartet.`

Observed phase 2:

`Der Anlagenspiegel wird erstellt.`

Automation rule:

- Wait for progress dialog.
- Treat both message changes as normal states.
- Continue waiting until the progress dialog disappears.
- Never click through or assume completion when text changes from phase 1 to phase 2.

Approximate measured total export benchmark: **~1 min 5 sec**.

Use a configurable hard timeout much larger than this benchmark (for example several minutes) in the real environment.

## B5. Liste anzeigen / Druckauswahl / spool

Use the same common sequence as A4–A6.

Target spool criteria:

1. Created after `runStartedAt`.
2. User `BHL`.
3. `Anlagenspiegel nach Anlagen`.
4. `Steuerrecht`.
5. `Ist`.
6. `01.2021-12.2021`.
7. Newest matching creation time.

Do not accidentally select:

- `Alle Anlagen nach Konten verdichtet`
- `Protokoll: Anlagenspiegel`
- an older `Anlagenspiegel nach Anlagen` entry from a previous run

## B6. Export advanced

Right-click exact selected metadata row:

`Export → Erweitert`

Expected `System - Gitterbox-Export`:

- Tabellenname = `CTLP1`
- Menge gesamt = `31`
- **XLS is initially selected in the recording**
- Ziel = `Excel`
- Datensätze = `Alle`

Therefore the automation **must change XLS → XLSX** explicitly.

Then export and validate exactly as A9.

Recommended filename:

`{Client}_{Year}_{Department}_Anlagenspiegel_Detailliert_nach_Anlagen.xlsx`

---

# Workflow C — Alle Anlagen nach Konten verdichtet (ALDI recording, re-verified)

This workflow was re-checked against the full `ALDI 1(2).webm` recording. The critical `00:18–00:33` segment is documented explicitly below because it contains an important transition that must not be skipped: **the operator first opens `Prozesse verwalten`, selects saved process `003`, and opens that saved process definition before executing it.**

## C0. Timestamp-verified sequence: 00:18–00:33

Follow this exact order:

1. **~00:18 — Navigation / idle shell**
   - Stay under `Anlagenbuchhaltung → Prozesse` in the left navigation tree.
   - Click `Prozesse verwalten`.

2. **~00:20 — `Anlagenbuchhaltung - Prozesse verwalten` opens**
   - A process grid is displayed.
   - Do **not** navigate directly to `Anlagenspiegel erstellen` for this recorded flow.
   - The process must be opened from the process-management grid.

3. **~00:26 — Select the saved process row**
   Select the row with all of these identifying values:

   | Column | Recorded value |
   |---|---|
   | Mandant | `02` |
   | Programm | `CAB015` |
   | Prozess | `003` |
   | Bezeichnung | `Alle Anlagen nach Konten verdichtet` |
   | Status | `AKTIV` |
   | Zustand | `OK` |
   | Process/type column | `CA45` |
   | Letztes Laufdatum | `21.08.2026` in this recording |

   - First ensure this exact row is selected/highlighted.
   - Then **double-click the selected row** to open its saved definition.
   - Never choose a row only by its visible position because the grid can contain many processes and the order can change.

4. **~00:27 — Saved process opens in `Anlagenbuchhaltung - Anlagenspiegel erstellen`**
   - The screen is populated from saved process `003`.
   - Verify that `Prozess = 003` and `Bezeichnung = Alle Anlagen nach Konten verdichtet` are loaded before continuing.
   - This is not a blank/new report definition.

5. **~00:27–00:32 — Verify the loaded saved-process configuration**
   The `Steuerung` tab is active. Verify the recorded values:

   - `Aktiv` = checked
   - `Automatisch deaktivieren` = unchecked
   - `Laufprotokoll` = checked
   - `Art` = `Kompletter Datenbestand`
   - `Bericht` = blank/disabled
   - `Fachbereich` = `Steuerrecht`
   - `Wertart/Plan` = `Ist`
   - `Zeitraum von` = `01 / 2021`
   - `Zeitraum bis` = `12 / 2021`
   - `Erstellung → Art` = `Druckversion`
   - `Summe für Anlagenhauptnummer` = unchecked
   - `Zugänge` = unchecked
   - `Abgänge` = unchecked
   - `Umbuchung Anlage` = unchecked
   - `Bearbeitungszustand` = `OK`

   Also preserve the visible tabs:
   - `Steuerung`
   - `Sortierung für Liste`
   - `Laufprotokoll`

   The right `Folgeaktionen` area visibly contains actions such as `Suchen`, `Logging`, and `Bezeichnung`.

6. **~00:32 — Execute the loaded process**
   - Click the **green check / execute icon in the top toolbar**.
   - In the mock this action is exposed as `AutomationId=Toolbar_Execute`.
   - Do not add a separate large Execute button to the form.

7. **~00:33 — Confirmation dialog**
   Expected dialog text:

   `Anlagenspiegel erstellen?`

   Click:

   `Ja`

   Do not choose `Abbrechen`.

This `00:18–00:33` sequence is mandatory for reproducing the ALDI recording accurately.

## C1. Open process manager

Navigation path:

`Anlagenbuchhaltung → Prozesse → Prozesse verwalten`

Mock locator: `Nav_ProzesseVerwalten`.

Expected screen:

`Anlagenbuchhaltung - Prozesse verwalten`

Find the saved process by **data values**, not grid coordinates:

- Mandant = `02`
- Programm = `CAB015`
- Prozess = `003`
- Bezeichnung = `Alle Anlagen nach Konten verdichtet`
- Status = `AKTIV`
- Zustand = `OK`
- Process/type = `CA45`

Select the row, then double-click it to open the saved process definition.

### Important

For this video workflow, **do not skip `Prozesse verwalten` and directly open `Anlagenspiegel erstellen` from the left tree**. The recorded behavior is:

`Prozesse verwalten → locate CAB015/003 → select row → double-click → Anlagenspiegel erstellen populated with process 003`.

## C2. Verify loaded process values

Expected screen title:

`Anlagenbuchhaltung - Anlagenspiegel erstellen`

Verify:

- Prozess = `003`
- Bezeichnung = `Alle Anlagen nach Konten verdichtet`
- `Aktiv` checked
- `Automatisch deaktivieren` unchecked
- `Laufprotokoll` checked
- Art = `Kompletter Datenbestand`
- Bericht = blank/disabled
- Fachbereich = `Steuerrecht`
- Wertart/Plan = `Ist`
- Zeitraum = `01/2021`–`12/2021`
- Erstellung Art = `Druckversion`
- Summe für Anlagenhauptnummer = unchecked
- Zugänge = unchecked
- Abgänge = unchecked
- Umbuchung Anlage = unchecked
- Bearbeitungszustand = `OK`

Record the job/spool correlation object **before execution**, for example:

`{report=Alle Anlagen nach Konten verdichtet, process=003, fachbereich=Steuerrecht, period=01/2021-12/2021, runStartedAt=<timestamp>, user=BHL}`.

Do not change the saved values unless the automation job explicitly requires a different client/year/department.

## C3. Execute and confirm

Click the top-toolbar green check / execute action:

`AutomationId=Toolbar_Execute`

Expected confirmation:

`Anlagenspiegel erstellen?`

Click:

`Ja`

## C4. Wait for the complete processing sequence

The ALDI recording shows multiple progress states. Treat them as **one continuing run**, not separate jobs.

Observed progress states include, in order:

1. `Anlagenselektion gestartet.`
2. `Ermitteln der Werte gestartet.`

The progress dialog remains modal while processing. It also contains an `Abbrechen` button; automation must **not click it** during a normal run.

Wait rules:

- Wait for the `Fortschritt` dialog to appear after confirmation.
- Observe progress-message changes without trying to close the dialog.
- Keep waiting until the `Fortschritt` dialog disappears by itself and the report form becomes responsive again.
- Use condition-based waits with a configurable hard timeout; do not use the recording duration as a fixed sleep.

## C5. Open `Liste anzeigen`

After the progress dialog disappears:

1. Use the left navigation tree.
2. Click `Liste anzeigen`.
3. A `Druckauswahl` dialog opens before the spool grid is shown.

Do not expect the spool grid to appear immediately after report generation.

## C6. `Druckauswahl`

The ALDI recording shows the print-selection/filter dialog before the report spool.

Use/verify the current Wilken context and click `Start` to load the spool list. The same common `Druckauswahl` rules from Workflow A apply.

Important automation behavior:

- Do not treat `Druckauswahl` as the export screen.
- `Start` here means **load/show matching spool entries**, not export the report.

## C7. Locate the exact generated spool entry

After `Druckauswahl → Start`, the spool list contains many rows.

Find the newly generated entry corresponding to the current run using:

1. creation time after `runStartedAt`
2. user/context match
3. process/report = `Alle Anlagen nach Konten verdichtet`
4. Fachbereich = `Steuerrecht`
5. period = `01/2021-12/2021`
6. newest matching entry

Do not export an arbitrary highlighted row.

The spool can contain related metadata/protocol rows. Match the report entry belonging to the generated report, not a protocol/log entry.

## C8. Right-click the selected spool row

Once the exact report row is selected:

`Right-click → Export → Erweitert`

The submenu transition is part of the workflow and must be implemented; `Erweitert` is what opens the advanced Gitterbox export screen.

Expected screen:

`System - Gitterbox-Export`

## C9. Configure Gitterbox export

Recorded values for this flow:

- Tabellenname = `CTLP1`
- Menge gesamt = `23`
- Format initially = `XLS`
- Ziel = `Excel`
- Datensätze = `Alle`

Mandatory action:

**Explicitly change `XLS → XLSX`.**

Do not rely on a previously remembered format selection.

Interpretation of `Datensätze = Alle`:

- Export all records contained in the **selected spool report**.
- It does **not** export all rows visible in the spool list.

## C10. Trigger the actual export

There is no large form-level `Export` button in the recorded Gitterbox screen.

After ensuring:

`XLSX + Excel + Alle`

click the **green check / execute icon in the top Wilken toolbar**.

Mock locator:

`AutomationId=Toolbar_Execute`

On the Gitterbox screen this toolbar action means:

`Export ausführen`

Then wait for the XLSX file to be created/opened. The recording shows Excel opening the produced file after the toolbar action.

## C11. Validate and finalize the exported file

Use the same production-grade validation rules as Workflow A:

1. snapshot the download/output directory before export
2. trigger export
3. find the new `.xlsx` created after the trigger timestamp
4. wait until its size becomes stable
5. ensure it is no longer locked for writing
6. validate XLSX/ZIP structure
7. optionally validate expected headers/content
8. rename/move it to the deterministic job filename
9. only then mark the job successful

Recommended filename:

`{Client}_{Year}_{Department}_Alle_Anlagen_nach_Konten_verdichtet.xlsx`

## C12. Exact high-level sequence from the full ALDI recording

The complete verified sequence is:

`Prozesse verwalten`
→ `find CAB015 / 003 / Alle Anlagen nach Konten verdichtet`
→ `select exact row`
→ `double-click row`
→ `Anlagenspiegel erstellen` opens with saved process `003`
→ `verify Steuerung values`
→ `green toolbar execute`
→ `Anlagenspiegel erstellen?`
→ `Ja`
→ `Fortschritt: Anlagenselektion gestartet.`
→ `Fortschritt: Ermitteln der Werte gestartet.`
→ `wait until Fortschritt closes`
→ `Liste anzeigen`
→ `Druckauswahl`
→ `Start`
→ `spool list`
→ `locate exact new report row`
→ `right-click`
→ `Export`
→ `Erweitert`
→ `System - Gitterbox-Export`
→ `change XLS to XLSX`
→ `Ziel = Excel`
→ `Datensätze = Alle`
→ `green toolbar execute / Export ausführen`
→ `wait for XLSX/Excel`
→ `validate and finalize file`.

# Required automation state machine

Implement states similar to:

`Idle`
→ `NavigatingToReport`
→ `ConfiguringReport`
→ `ReadyToExecute`
→ `Confirming`
→ `Generating`
→ `WaitingForReportCompletion`
→ `OpeningList`
→ `PrintSelection`
→ `LoadingSpool`
→ `LocatingExactSpoolEntry`
→ `OpeningContextMenu`
→ `OpeningAdvancedExport`
→ `ConfiguringXlsxExport`
→ `Exporting`
→ `WaitingForDownload`
→ `ValidatingFile`
→ `RenamingFile`
→ `Success`

Failure/retry states:

`UiElementNotFound`, `UnexpectedDialog`, `GenerationTimeout`, `SpoolEntryNotFound`, `AmbiguousSpoolMatch`, `ExportDialogTimeout`, `DownloadTimeout`, `InvalidXlsx`, `RetryableFailure`, `FailedFinal`.

Never mark a job successful merely because the Export command was clicked.

---

# Critical spool-selection rule

This is the most important correctness rule:

> The spool contains many reports from different times and report types. The worker must correlate the newly generated spool entry to the current job. Right-clicking a selected report exports that selected report. `Datensätze = Alle` exports all data records of that selected report; it does **not** mean all reports in the spool.

If more than one row matches the same report/year/department, use the creation timestamp relative to `runStartedAt`. If correlation remains ambiguous, fail safely with `AmbiguousSpoolMatch`; never guess.

---

# Timing and waiting rules

The mock's current timing profile intentionally simulates the user's measured totals:

- Zugangsliste ≈ 30 s
- Anlagenspiegel Detailliert nach Anlagen ≈ 65 s

Use these only for performance expectations. Production code must use condition-based waits:

- window appears/disappears
- button enabled/disabled
- title changes
- progress message changes
- spool row created after job start
- export view appears
- file creation + size stability

Every wait must have a configurable upper-bound timeout and cancellation token.

---

# Screenshot usage with Cursor

Give Cursor screenshots for these visual checkpoints, and tell it that this document is the behavioral source of truth:

1. Zugangsliste form.
2. Zugangsliste `Fortschritt` dialog.
3. Anlagenspiegel form.
4. `Anlagenspiegel erstellen?` confirmation.
5. `Ermitteln der Werte gestartet.` progress.
6. `Der Anlagenspiegel wird erstellt.` progress.
7. `Druckauswahl` dialog.
8. Spool list with many rows.
9. Spool right-click menu with `Export → Erweitert`.
10. Gitterbox export with XLS selected.
11. Gitterbox export after XLSX selection.
12. Downloaded XLSX visible in Downloads/browser.
13. Prozesse verwalten grid and the `CAB015 / 003 / Alle Anlagen nach Konten verdichtet` row.

Cursor should refine dimensions/colors/icons against screenshots but must not change the workflow or field semantics documented here.
