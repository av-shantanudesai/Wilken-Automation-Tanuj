# Video re-review: gaps found and corrections applied

This document records the second-pass comparison of the WPF mock against all supplied recordings:

- `ALDI 1.webm`
- `ALDI 1(1).webm` — byte-for-byte identical to `ALDI 1.webm`; used as confirmation of the same reference UI.
- `Zugangsliste 1.webm`
- `Anlagenspiegel Detailliert nach Anlagen 1.webm`

## Important gaps found in the first mock

1. **Zugangsliste confirmation was missing.**
   - Recorded dialog title: `Zugangsliste`
   - Recorded question: `Die angeforderte Liste erstellen?`
   - Actions: `Ja` / `Abbrechen`
   - The mock now shows this confirmation before report processing.

2. **Recorded progress has multiple named stages, not a single generic wait.**
   - Zugangsliste:
     1. `Anlagenselektion gestartet.`
     2. `Ermitteln der Werte gestartet.`
     3. `Erstellen der Zugangsliste gestartet.`
   - Anlagenspiegel / process 003:
     1. `Anlagenselektion gestartet.`
     2. `Ermitteln der Werte gestartet.`
     3. `Der Anlagenspiegel wird erstellt.`
   - The mock now changes the text in the same progress window as processing advances.

3. **`Sortierung für Liste` was present but empty.**
   The real Anlagenspiegel screen contains a complete tab. It is now implemented with:
   - `Sortierkriterium`
   - row 1 = `Hauptkonto`
   - row 2 = `Anlagennummer`
   - three additional blank sort rows
   - `Summen` checkboxes, first one checked
   - `Seitenwechsel` checkboxes
   - `Betragseinschränkung`
   - `AHK = 0.00`
   - `Restbuchwert = 0.00`
   - associated combo fields

4. **The process header layout was wrong.**
   The real screen has:
   - `Prozess` label
   - a top yellow input field
   - the selected process number directly underneath (`001` or `003`)
   - separator line
   - `Bezeichnung`
   - language `D`
   - long description field
   This has been recreated.

5. **`Liste anzeigen` screen was skipped before Druckauswahl.**
   In the recording, navigation first changes the main window to `Anlagenbuchhaltung - Liste anzeigen`; the blank list area and bottom buttons are already visible, and then `Druckauswahl` opens on top. The mock now follows this exact order.

6. **Druckauswahl layout/details were incomplete.**
   The dialog now contains the recorded columns and values:
   - Sortierung
   - Von/nur Wert
   - Bis Wert
   - Auswahl
   - E/A
   - Konzern `1`
   - Mandant `02`
   - Version/Release `3` / `0`
   - Gebiet `CSA`
   - Benutzer `BHL`
   - `Listenname` selected as sorting criterion
   - `Alle anzeigen`
   - `Start` and `Abbrechen`

7. **Spool context menu was too short / simplified.**
   It now reproduces the visible menu sequence including:
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

8. **Initial export format was wrong for Zugangsliste.**
   The recordings show `XLS` selected when `System - Gitterbox-Export` opens. The operator then selects `XLSX`. The mock now starts with `XLS` for all recorded report flows and requires explicit `XLSX` selection.

9. **Extra completion message box was not in the videos.**
   The first mock showed an artificial `Export abgeschlossen` dialog. The recordings proceed directly to the browser/download side. This extra dialog has been removed. The mock creates the XLSX and updates status only.

10. **Process manager did not match the ALDI recording closely enough.**
    It now includes:
    - `Allgemein` tab header
    - process rows visible in the video
    - process `003` = `Alle Anlagen nach Konten verdichtet`
    - process `001` = `Anlagenspiegel nach Anlagen`
    - `Zugangsliste`
    - Status / Zustand / process-code / run-date columns
    - bottom `Auswahl` area
    - `Alle Mandanten anzeigen`
    - `Anzeige = Alle Prozesse`
    - `Status ändern = Keine`
    - right-side `Folgeaktionen` panel with `Suchen`, `Logging`, `Bezeichnung`

11. **Main shell placement was inaccurate.**
    The WPF shell was adjusted to more closely match the recordings:
    - top blue screen-title strip
    - separate menu and toolbar rows
    - left navigation width/placement
    - Wilken logo area and search field
    - right document/follow-action panel
    - bottom navigation/favorites/active-window/login sections
    - status bar with `BHL`, clock, module code and page indicator

12. **Spool historical content was too sparse.**
    Additional protocol/history rows from the recordings were added so the automation cannot succeed by simply selecting the only row.

## Timing correction

The first mock incorrectly used the whole observed end-to-end duration as internal system delay, which made manual use longer than the videos.

The revised timing profile separates actual system waits from human navigation/click time:

- Zugangsliste recording: about 38 seconds total video; system processing is only a few seconds.
- Anlagenspiegel Detailliert nach Anlagen recording: about 66 seconds total; the main Wilken calculation occupies roughly 27 seconds.
- Process 003 flow uses a similar multi-stage processing pattern.

`appsettings.json` contains the configurable stage delays. These are test-simulation values, while the workflow automation must always wait for UI state changes instead of assuming the delay.

## Fidelity boundary

The mock intentionally reproduces the observed workflow, labels, defaults, layout proportions and state transitions. Proprietary Wilken icon artwork, exact legacy control rendering, browser/Citrix chrome and pixel-perfect font rasterization cannot be guaranteed from a screen recording alone. Screenshots should remain the visual authority for final pixel-level adjustment in Cursor/Visual Studio.

10. **Final Gitterbox export trigger clarified/fixed.**
    The recordings do not contain a large text `Export` button on the Gitterbox form. Export is triggered by the green check icon in the top Wilken toolbar after selecting `XLSX`, `Excel`, and `Alle`. The mock now makes that toolbar action visually green, exposes the accessible name `Export ausführen`, keeps `Toolbar_Execute` for automation compatibility, and wires it to actual XLSX creation.
