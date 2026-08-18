# WilkenAutomation.TestDesktop

Dummy Windows UI for testing **real FlaUI desktop automation** without Wilken CS/2 installed.

It is not a product. Switch the worker back to `Mock` or `Wilken` when you do not need it.

## What it has

- Login (`tester` / `tester`)
- Client combo (`001`–`010`)
- Asset Accounting (menu)
- Fiscal year
- Department (`Handelsrecht` / `Steuerrecht`)
- Execute → report status Idle / Processing / Ready
- Spool list
- Export + in-app Save dialog
- CSV format matching `ExportFileValidator` (including empty period: client **002** + **Steuerrecht**)

AutomationIds match `WilkenAutomation.Worker/appsettings.DesktopTest.json`.

## Run with the worker

```powershell
$env:DOTNET_ENVIRONMENT='DesktopTest'
dotnet run --project WilkenAutomation.Worker
```

The worker launches this app (or attaches if it is already running).
