# Screenshot set to give Cursor

Because Cursor cannot receive the videos, provide screenshots representing these states. The workflow docs tell Cursor what happens between them.

Minimum recommended set:

1. **Home** — ALDI recording beginning.
2. **Prozesse verwalten** — process grid with `CAB015 / 003` visible.
3. **Anlagenspiegel process 003 / Steuerung** — full screen including right Folgeaktionen pane.
4. **Zugangsliste / Steuerung** — full screen.
5. **Anlagenspiegel detailed / Steuerung** — full screen.
6. **Anlagenspiegel / Sortierung für Liste**.
7. **Zugangsliste confirmation** — `Die angeforderte Liste erstellen?`.
8. **Anlagenspiegel confirmation** — `Anlagenspiegel erstellen?`.
9. **Fortschritt** — `Ermitteln der Werte gestartet.`.
10. **Fortschritt** — `Der Anlagenspiegel wird erstellt.`.
11. **Liste anzeigen base screen** before Druckauswahl.
12. **Druckauswahl** full dialog.
13. **Spool grid** with many rows.
14. **Spool context menu** with `Export` submenu expanded.
15. **Gitterbox initial state** — XLS selected.
16. **Gitterbox XLSX state** — XLSX selected just before Execute.

When sending screenshots to Cursor, name them `01_Home.png`, `02_ProcessManager.png`, ... so Cursor can map them directly to `SCREEN_BY_SCREEN_SPEC.md`.

Cursor instruction for screenshots:

> Treat screenshots as the visual source of truth and `AUTOMATION_WORKFLOW.md` as the interaction/state source of truth. Match the screen represented by each image without redesigning, deleting controls, or combining steps.

## Added screenshots: repeated-job close behavior

The supplied test-environment screenshots show the small **X** at the top-right of the internal blue Wilken title bar on Gitterbox Export, Liste anzeigen, and Anlagenspiegel erstellen. Use these screenshots as the visual reference for `InternalWindow_Close`. One screenshot also shows `Funktion gesperrt` after trying to open Prozesse verwalten again while the previous process context is still active; the mock reproduces this with `FunctionLockedDialog`.
