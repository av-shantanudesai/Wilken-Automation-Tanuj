# Cursor task — exact Wilken CS/2 replica refinement

You are working on the supplied `.NET 8 WPF` project `WilkenCs2ReplicaMock`.

## Objective

Make the test desktop application reproduce the supplied Wilken CS/2 screenshots and the documented video workflow as closely as possible so a FlaUI/.NET automation worker can be developed against it before using the real Wilken/Citrix UI.

## Sources of truth

1. **Screenshots supplied with this task = visual/pixel authority.**
2. `docs/SCREEN_BY_SCREEN_SPEC.md` = screen composition/placement authority.
3. `docs/AUTOMATION_WORKFLOW.md` = behavioral/order/state authority.
4. `docs/VIDEO_OBSERVATIONS.md` = evidence/timing reference.
5. Existing AutomationIds = automation contract; preserve them.

Do not modernize or redesign the UI.

## Mandatory screens/states

Implement/preserve all of these as separate states:

`Home → Prozesse verwalten → report screen → confirmation → Fortschritt → Liste anzeigen base → Druckauswahl → spool grid → right-click Export/Erweitert → Gitterbox initial XLS → Gitterbox XLSX → file export → internal-close unwind → Prozesse verwalten ready for next job`

Also preserve direct individual-definition access for:

- Zugangsliste process `001`
- Anlagenspiegel nach Anlagen process `001`

and process-manager flow for:

- `CAB015 / 003 / Alle Anlagen nach Konten verdichtet`

## Exact behavioral rules

- Zugang confirmation: title `Zugangsliste`, text `Die angeforderte Liste erstellen?`.
- Anlagenspiegel confirmation: title `Anlagenspiegel`, text `Anlagenspiegel erstellen?`.
- Zugang visible progress: `Ermitteln der Werte gestartet.`.
- Anlagenspiegel progress sequence: `Anlagenselektion gestartet.` → `Ermitteln der Werte gestartet.` → `Der Anlagenspiegel wird erstellt.`.
- Successful report run creates two spool outputs: protocol + report data.
- Right-side `Protokolle/Listen` count increases by 2.
- `Liste anzeigen` must become visible before `Druckauswahl` opens.
- Spool must contain many old entries; never make target the only row.
- **The supplied recordings right-click the newest matching PRT metadata row:** Zugang `B024/PRT`, Anlagenspiegel `B015/PRT`.
- Context path: `Export → Erweitert`.
- Gitterbox initial format must be `XLS` for every recorded flow.
- Automation/user explicitly changes `XLS → XLSX`.
- target `Excel`, records `Alle`.
- Gitterbox record counts: Zugang `37`, detailed `31`, condensed process 003 `23`.
- Do not show an artificial export-completed modal.
- Create a valid XLSX named `CTLP12.xlsx`; on duplicate use browser-like suffix `(1)`, `(2)`, etc.

## UI fidelity rules

Match screenshots in this order:

1. overall window/shell proportions
2. left navigation placement and width
3. title/menu/toolbar heights
4. right context pane start position/width
5. report header coordinates
6. tab positions
7. section/group coordinates
8. field widths/heights/spacing
9. Druckauswahl geometry
10. spool density/columns/context menu
11. Gitterbox layout

Do not introduce:

- rounded cards
- Fluent/Material components
- large modern margins
- modern icon substitutions that move controls
- hidden/removal of blank fields visible in screenshots

Keep classic dense Wilken-style controls.

## Automation contract

Preserve existing AutomationIds. If a screenshot requires a new control, add a stable AutomationId.

Locator order expected by worker:

`AutomationId → type/name → parent-child → keyboard → image matching → coordinates`

Do not build the worker around fixed sleeps. Timing config is only simulation. Wait on visible state/window/control conditions with bounded timeouts.

## Validation before finishing

- build solution on Windows/.NET 8
- run all three workflows manually
- verify every modal and intermediate screen appears in documented order
- verify process run adds two spool logical outputs
- verify PRT row can open Gitterbox export
- verify initial XLS must be changed to XLSX
- verify generated XLSX opens successfully
- compare each supplied screenshot against the corresponding state in `SCREEN_BY_SCREEN_SPEC.md`
- do not declare visual completion while obvious placement/size differences remain

### Gitterbox final export trigger — mandatory

Do **not** add a large text `Export` button to the form; that would differ from the video. The real recorded flow starts export using the **green check icon in the top Wilken toolbar**. After `XLSX + Excel + Alle` are selected, clicking the top toolbar check must create the XLSX. Keep `AutomationId=Toolbar_Execute`; its semantic action on the Gitterbox screen is `Export ausführen`.


## Mandatory repeated-job close/unwind behavior

The real Wilken test environment keeps the original `Prozesse verwalten` context open underneath child report/list/export windows. If the automation clicks `Prozesse verwalten` again before closing those child windows, Wilken displays `Funktion gesperrt` (function `CAD18` already used by another process). Reproduce this behavior in the mock.

Add/preserve a small internal close X at the far right of every blue Wilken child-window title bar, matching the supplied screenshots. This is not the outer Windows/Citrix application close button.

Automation contract:

- internal close X: `AutomationId=InternalWindow_Close`, Name=`Schließen`
- locked dialog: `AutomationId=FunctionLockedDialog`
- locked OK: `AutomationId=FunctionLocked_OK`
- process grid: `AutomationId=ProcessManager_Grid`

After each validated export, the automation must close one active child screen at a time and verify the state after every click:

`Gitterbox-Export → internal X → Liste anzeigen/spool → internal X → report definition → internal X → Prozesse verwalten`.

Stop when `ProcessManager_Grid` is visible. Never close the whole Wilken application. Do not use a blind fixed count of close clicks; verify each screen transition. Only after returning to `Prozesse verwalten` may the next process row be selected and executed.

Also make a navigation attempt to `Prozesse verwalten` while one of its child screens is still active show the Wilken-style `Funktion gesperrt` dialog instead of silently replacing the current screen.
