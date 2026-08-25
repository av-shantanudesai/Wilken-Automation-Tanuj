# Close and repeat flow added from test-environment screenshots

## Problem observed

After a report export, `Prozesse verwalten` is still open underneath the report/list/export child windows. Clicking `Prozesse verwalten` again from Navigation can show **Funktion gesperrt** because function `CAD18` is already being used in another process context.

## Required automation

After XLSX creation and validation:

1. On `System - Gitterbox-Export`, click the small internal **X** at the top-right of the blue Wilken title bar.
2. Verify `Anlagenbuchhaltung - Liste anzeigen` / spool is visible.
3. Click the internal **X** again.
4. Verify the active report definition is visible.
5. Click the internal **X** again.
6. Verify `Anlagenbuchhaltung - Prozesse verwalten` and `ProcessManager_Grid`.
7. Start the next job by selecting/double-clicking the next exact process row.

Do not click the outer Windows/Citrix close button. Do not blindly click X a fixed number of times: verify each screen and stop as soon as the process-manager grid is visible.

## Mock controls

- `InternalWindow_Close` — small internal blue-title-bar X, Name `Schließen`.
- `FunctionLockedDialog` — simulated Wilken function-lock error.
- `FunctionLocked_OK` — closes that error.
- `ProcessManager_Grid` — positive signal that cleanup is complete and the next job may start.
