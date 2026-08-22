# Cursor prompt — Wilken CS/2 replica + export automation

## Task / Objective

Build and refine a **.NET 8 WPF desktop replica of the Wilken CS/2 screens shown in the supplied screenshots** and implement/test the same export workflow used in the recordings. The replica is a test harness for our automation worker; it must expose stable UI Automation properties and reproduce the same visible fields, dialogs, spool behavior, export options, and realistic delays.

## Visual implementation

Use the screenshots as the pixel-level visual reference. Reproduce:

- Window title: `1/02 - Frank Krauss GmbH & Co. KG - Wilken_CS/2_Finanzmanagement`
- blue Wilken-style shell/header
- left Navigation tree + search
- top menus/toolbars
- center working area
- right panels (`Dokumente`, `Protokolle/Listen`, `Notizen`, `Tooltips`, `Folgeaktionen`, `Informationen`)
- bottom status area
- the exact visible labels/values documented in `docs/AUTOMATION_WORKFLOW.md`

Do not redesign or modernize the UI. Preserve the old desktop/rich-client appearance because the application is for automation testing.

## Required screens/workflows

Implement all of the following:

1. `Prozesse verwalten`
2. `Zugangsliste erstellen`
3. `Anlagenspiegel erstellen` for `Anlagenspiegel nach Anlagen`
4. `Anlagenspiegel erstellen` for `Alle Anlagen nach Konten verdichtet`
5. `Anlagenspiegel erstellen?` confirmation dialog
6. `Fortschritt` dialogs and message transitions
7. `Druckauswahl`
8. `Liste anzeigen` spool with many historical rows
9. right-click context menu and `Export → Erweitert`
10. `System - Gitterbox-Export`
11. physical XLSX creation in Downloads

## Exact business workflow

Treat `docs/AUTOMATION_WORKFLOW.md` as mandatory. The expected flow is:

`Navigate → configure/verify fields → execute → confirm if required → wait for progress completion → Liste anzeigen → Druckauswahl Start → correlate exact newly generated spool entry → right-click exact entry → Export → Erweitert → force XLSX → Excel → Alle → export → wait for file → validate XLSX → rename/move → success`.

## Timing

Make timing configurable in `appsettings.json`.

Default test timings:

- Zugangsliste ≈ 30 sec
- Anlagenspiegel Detailliert nach Anlagen ≈ 65 sec

The UI must stay responsive during waits; use async/await and cancellation tokens. Do not block the UI thread.

## Spool correctness

The spool has many old rows and multiple report types. Do not simply select row 0. The automation must match the current job by report name + Fachbereich + period/year + user + creation time after job start. If multiple candidates remain, fail safely as ambiguous rather than exporting a random report.

Remember: `Datensätze = Alle` means all records in the selected report, not every spool report.

## Export behavior

For Zugangsliste:

- Tabellenname `CTLP1`
- Menge gesamt `37`
- XLSX / Excel / Alle

For Anlagenspiegel nach Anlagen:

- Tabellenname `CTLP1`
- Menge gesamt `31`
- export view initially shows XLS as in the recording
- automation must switch to XLSX
- Excel / Alle

For Alle Anlagen nach Konten verdichtet:

- Tabellenname `CTLP1`
- Menge gesamt `23`
- XLS initially selected
- switch to XLSX
- Excel / Alle

Create a valid `.xlsx` file, not a fake extension.

## UI automation requirements

Every automation-critical element must have a stable `AutomationProperties.AutomationId`. Prefer FlaUI/UIA selectors; keyboard/image/coordinates are fallback layers only. Keep the IDs already present in this repo unless there is a compelling reason to add aliases.

## Reliability requirements

- no fixed sleeps as success conditions
- condition-based waits with upper-bound timeout
- no blocking UI thread
- cancellation support
- retry only for known transient states
- screenshot + log on failure
- detect unexpected dialogs
- validate exported file before marking success
- deterministic output naming to prevent files from different jobs being mixed
- no random spool-row selection

## Output

1. Working WPF application.
2. All screenshots visually matched as closely as possible.
3. Automation workflow implementation/test harness.
4. Clear logs showing state transitions and timings.
5. A short gap report listing anything that cannot be reproduced exactly from screenshots alone.

## Mandatory constraint

Do not simplify or redesign the flow. The purpose is to reproduce the Wilken behavior closely enough that the same automation logic can later be used against the real Citrix/Windows application.
