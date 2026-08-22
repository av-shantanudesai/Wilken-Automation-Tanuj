# Video observations and timing reference

Timings are approximate visual timestamps and must not be used as hard sleeps in automation.

## Duplicate ALDI upload

`ALDI 1.webm` and `ALDI 1(1).webm` have the same SHA-256:

`60d5b220ed121eca693fc51c4f401a64a2dbcbb2fb20d44866c6070690d904a8`

They are the same 126.866-second recording.

---

## Zugangsliste recording

Duration: **38.033 s**.

Observed sequence:

1. `Anlagenbuchhaltung - Zugangsliste erstellen` is already open.
2. Fields are populated for Handelsrecht / Ist / 2020.
3. Execute is triggered.
4. Confirmation appears: `Die angeforderte Liste erstellen?`.
5. After `Ja`, `Fortschritt` appears; visible recorded text is `Ermitteln der Werte gestartet.`.
6. Progress closes.
7. Main window changes to `Anlagenbuchhaltung - Liste anzeigen`.
8. `Druckauswahl` appears.
9. After `Start`, spool grid loads with many historical entries.
10. The newest `B024 / PRT` metadata row is selected/right-clicked; its following STOP line is `Protokoll: Zugangsliste`.
11. Context menu path: `Export → Erweitert`.
12. `System - Gitterbox-Export` shows `CTLP1`, count `37`, initial `XLS`, target `Excel`, records `Alle`.
13. User changes `XLS → XLSX` and executes export.
14. Browser/download side receives the XLSX.

### Zugangsliste values

- Prozess `001`
- Bezeichnung `D / Zugangsliste`
- Aktiv checked
- Automatisch deaktivieren unchecked
- Laufprotokoll unchecked
- Art `Bericht`
- Bericht `NACH ANLAGEN`
- Fachbereich `Handelsrecht`
- Wertart/Plan `Ist`, secondary `0`
- Zugangsdatum `01.01.2020`–`31.12.2020`
- Zeitraum `01/2020`–`12/2020`
- Erstellung `Druckversion`
- Summe für Anlagenhauptnummer unchecked
- Gegenkonto option unchecked
- Umbuchung Bilanzposition checked
- Umbuchung Anlage checked

---

## Anlagenspiegel Detailliert nach Anlagen recording

Duration: **66.433 s**.

Observed sequence:

1. `Anlagenbuchhaltung - Anlagenspiegel erstellen` is open.
2. Process `001`, `Anlagenspiegel nach Anlagen`.
3. Detailed bookings are enabled.
4. Confirmation: `Anlagenspiegel erstellen?`.
5. Progress states include `Anlagenselektion gestartet.`, then long `Ermitteln der Werte gestartet.`, then `Der Anlagenspiegel wird erstellt.`.
6. Main screen changes to `Liste anzeigen`; Druckauswahl is used.
7. Spool contains many rows.
8. The newest `B015 / PRT` metadata row is the recorded right-click target; following STOP line is `Protokoll: Anlagenspiegel`.
9. `Export → Erweitert`.
10. Gitterbox: `CTLP1`, count `31`, initial `XLS`, `Excel`, `Alle`.
11. User changes to `XLSX` and exports.

### Detailed Anlagenspiegel values

- Prozess `001`
- Bezeichnung `D / Anlagenspiegel nach Anlagen`
- tabs `Steuerung`, `Sortierung für Liste`, `Laufprotokoll`
- Aktiv checked
- Laufprotokoll checked
- Art `Kompletter Datenbestand`
- Bericht blank/disabled
- Fachbereich `Steuerrecht`
- Wertart/Plan `Ist`, secondary `0`
- Zeitraum `01/2021`–`12/2021`
- Erstellung `Druckversion`
- Zugänge checked
- Abgänge checked
- Umbuchung Anlage checked
- export count `31`

---

## ALDI / process-manager recording

Duration: **126.866 s**.

Observed sequence:

1. Wilken Home workspace with left navigation.
2. Navigate `Anlagenbuchhaltung → Prozesse → Prozesse verwalten`.
3. Process manager grid opens.
4. Select/open `CAB015 / 003 / Alle Anlagen nach Konten verdichtet`.
5. `Anlagenbuchhaltung - Anlagenspiegel erstellen` opens for process `003`.
6. Configure/verify Steuerrecht / Ist / `01/2021–12/2021`.
7. Execute and confirm `Anlagenspiegel erstellen?`.
8. Progress: `Anlagenselektion gestartet.` → `Ermitteln der Werte gestartet.` → `Der Anlagenspiegel wird erstellt.`.
9. `Liste anzeigen` → `Druckauswahl` → spool.
10. Current `B015 / PRT` row is used for the recorded right-click export path.
11. `Export → Erweitert`.
12. Gitterbox count `23`, initial `XLS`, `Excel`, `Alle`; user changes to `XLSX` and exports.

### Process manager columns visible

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

Key rows include:

- `CAB015 / 003 / Alle Anlagen nach Konten verdichtet / AKTIV / OK / CA45`
- `CAB015 / 001 / Anlagenspiegel nach Anlagen / AKTIV / OK / CA45`
- `CAB024 / 001 / Zugangsliste / AKTIV / OK / CA50`

### Process 003 values

- process `003`
- `Alle Anlagen nach Konten verdichtet`
- Art `Kompletter Datenbestand`
- Fachbereich `Steuerrecht`
- Wertart/Plan `Ist`
- Zeitraum `01/2021`–`12/2021`
- Zugänge unchecked
- Abgänge unchecked
- Umbuchung Anlage unchecked
- export count `23`

---

## Spool semantics reproduced by the mock

One report execution creates two logical spool outputs:

- protocol (`B024/PRT` or `B015/PRT`) + STOP protocol description
- actual report data (`5J0102/...` or `4J0402/...`) + STOP report description

The supplied videos show the context-menu export being invoked from the newest matching **PRT metadata row**. The mock and Cursor workflow intentionally reproduce that exact behavior.

`Datensätze = Alle` in Gitterbox refers to records in the selected export context; it does not mean every row visible in the spool list.
