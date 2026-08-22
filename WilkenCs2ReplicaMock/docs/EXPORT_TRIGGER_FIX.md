# Gitterbox export trigger fix

## What looked missing

After selecting a spool row and choosing:

`right-click → Export → Erweitert`

the Gitterbox screen shows `Format`, `Ziel`, `Datensätze`, and the record count, but no large button labeled `Export`.

## What the videos actually show

This is expected. Wilken starts the final export from the **top toolbar green check icon**, not from a text button inside the form.

Exact sequence:

1. Select the exact report/spool row.
2. Right-click.
3. `Export → Erweitert`.
4. On `System - Gitterbox-Export`, change `XLS` to `XLSX`.
5. Keep `Ziel = Excel`.
6. Keep `Datensätze = Alle`.
7. Click the **green ✓ in the top toolbar** (`Export ausführen`).
8. Wait for the XLSX file to appear and become stable.

## Mock implementation

- Stable automation id: `Toolbar_Execute`
- Accessible action name on Gitterbox: `Export ausführen`
- The button is rendered as a green check on the Gitterbox screen.
- Clicking it calls the XLSX export service.
- No artificial success popup is shown, matching the recordings.
