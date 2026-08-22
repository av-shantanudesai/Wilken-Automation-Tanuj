using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

public partial class WindowsWilkenAutomationService
{
    /// <summary>
    /// Dismiss informational popups by clicking a named OK/Close button via UIA.
    /// Known dialogs (save export) are left alone. Unknown dialogs without a
    /// dismiss button abort the attempt so the executor can screenshot and retry.
    /// </summary>
    private void HandleDialogs()
    {
        for (var i = 0; i < 5; i++)
        {
            if (!TryDismissOneDialog()) break;
        }
    }

    private bool TryDismissOneDialog()
    {
        var overlay = TryFind("UnexpectedDialog");
        if (overlay is not null)
        {
            var ok = TryFind("DialogOkButton") ?? FindButtonByName(overlay, DismissButtonNames());
            if (ok is null)
                throw new WilkenAutomationException("UNEXPECTED_DIALOG",
                    "Unexpected overlay dialog has no OK/Close button. Aborting attempt for safe recovery.");
            _logger.LogWarning("Dismissing unexpected overlay dialog.");
            InvokeControl(ok);
            return true;
        }

        if (_mainWindow is null) return false;
        Window[] modals;
        try { modals = _mainWindow.ModalWindows; }
        catch { return false; }

        foreach (var modal in modals)
        {
            var title = modal.Title ?? "<untitled>";
            if (IsKnownDialog(title)) continue;

            var ok = TryFindIn(modal, "DialogOkButton") ?? FindButtonByName(modal, DismissButtonNames());
            if (ok is not null)
            {
                _logger.LogWarning("Dismissing unexpected dialog '{Title}'.", title);
                InvokeControl(ok);
                return true;
            }

            throw new WilkenAutomationException("UNEXPECTED_DIALOG",
                $"Unexpected modal dialog '{title}' has no dismiss button. Aborting attempt for safe recovery.");
        }

        return false;
    }

    private bool IsKnownDialog(string title)
    {
        if (IsReplica && (
            title.Contains("Zugangsliste", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Anlagenspiegel", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Fortschritt", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Druckauswahl", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Gitterbox", StringComparison.OrdinalIgnoreCase)))
            return true;

        var known = _options.Selectors.GetValueOrDefault("KnownDialogTitles", "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return known.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private string[] DismissButtonNames() =>
        (_options.Selectors.GetValueOrDefault("DismissibleButtonNames", "OK|Close|Ja|Yes|Weiter")
            ?? "OK|Close|Ja|Yes|Weiter")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static AutomationElement? FindButtonByName(AutomationElement root, string[] names)
    {
        try
        {
            foreach (var name in names)
            {
                var button = root.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
                if (button is not null) return button;
            }
        }
        catch { }
        return null;
    }
}
