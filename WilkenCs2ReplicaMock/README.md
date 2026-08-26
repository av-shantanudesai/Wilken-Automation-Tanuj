# Wilken CS/2 Export Replica Mock — revised video-matched WPF build

.NET 8 WPF training/test replica of the Wilken CS/2 Asset Accounting export workflow visible in the supplied recordings.

## Implemented recorded flows

1. `Zugangsliste` — process `001`, Handelsrecht, 01/2020–12/2020, 37 export records.
2. `Anlagenspiegel nach Anlagen` — process `001`, Steuerrecht, 01/2021–12/2021, 31 export records.
3. `Alle Anlagen nach Konten verdichtet` — process `003`, selected from `Prozesse verwalten` as in the ALDI recording.
4. Full `Sortierung für Liste` tab for Anlagenspiegel.
5. Confirmation dialogs for both Zugangsliste and Anlagenspiegel flows.
6. Multi-stage Fortschritt messages.
7. `Liste anzeigen` base screen before `Druckauswahl`.
8. Video-like `Druckauswahl` filters.
9. Historical multi-row spool and exact row context menu.
10. `Export → Erweitert` and `System - Gitterbox-Export`.
11. Recorded initial export state `XLS`, requiring explicit `XLSX` selection.
12. Real `.xlsx` output written to the Windows Downloads folder.

See:

- `docs/REVIEW_AND_FIXES.md` — gaps found during the second video review and what changed.
- `docs/AUTOMATION_WORKFLOW.md` — exact workflow Cursor/FlaUI should implement.
- `docs/VIDEO_OBSERVATIONS.md` — earlier video observations.

## Run

Open `WilkenCs2ReplicaMock.sln` in Visual Studio 2022 on Windows with `.NET desktop development`, or:

```powershell
dotnet restore
dotnet run --project .\src\WilkenCs2ReplicaMock\WilkenCs2ReplicaMock.csproj
```

Target: `net8.0-windows`, WPF.

## Timing

`src/WilkenCs2ReplicaMock/appsettings.json` contains stage delays derived from the recordings. The revised build does not incorrectly add the entire video duration as a single system delay; operator navigation/click time is separate.

## UI automation

Stable `AutomationProperties.AutomationId` values are retained/expanded for FlaUI/UIA testing. Use UI state changes as success conditions and configured timeout values only as upper bounds.

## Fidelity note

The mock closely reproduces the observed labels, field defaults, workflow, screen proportions and state transitions. Exact proprietary Wilken icons/fonts and Citrix/browser chrome are not copied; use supplied screenshots as the final visual authority for pixel-level refinement.


## Update: repeated-job close/unwind flow

The mock now includes the internal Wilken child-window close X (`InternalWindow_Close`) shown in the test-environment screenshots. After an export, close Gitterbox → spool/list → report until `Prozesse verwalten` is visible before starting the next process. Re-opening `Prozesse verwalten` prematurely intentionally shows the `Funktion gesperrt` dialog. See `docs/AUTOMATION_WORKFLOW.md`.
