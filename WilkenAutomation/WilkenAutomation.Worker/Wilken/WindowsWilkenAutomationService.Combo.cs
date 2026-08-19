using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

public partial class WindowsWilkenAutomationService
{
    /// <summary>
    /// Select a combo/text field without mouse, keyboard, or focus theft.
    /// Prefer UIA Value / editable text; ExpandCollapse + SelectionItem is the
    /// only fallback. Never Click() or Keyboard.Type().
    /// </summary>
    private async Task SetSelectorValueAsync(string selectorKey, string value, CancellationToken ct)
    {
        var field = Find(selectorKey);
        if (field.ControlType == ControlType.ComboBox)
            await SelectComboItemAsync(selectorKey, field, value, ct);
        else
            SetControlValue(field, value);

        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFind(selectorKey), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{selectorKey} = '{value}'", ct);
    }

    private async Task SelectComboItemAsync(string selectorKey, AutomationElement field, string value, CancellationToken ct)
    {
        var applied = false;
        WithoutStealingInput(() =>
            applied = TrySetComboText(field, value) && ControlShowsValue(Find(selectorKey), value));
        if (applied)
            return;

        if (await TrySelectComboViaExpandAsync(selectorKey, value, ct))
            return;

        throw new WilkenAutomationException("COMBO_SELECT_FAILED",
            $"Could not set '{selectorKey}' to '{value}' via UIA Value/Selection patterns (mouse/keyboard are disabled).");
    }

    private static bool TrySetComboText(AutomationElement field, string value)
    {
        try
        {
            var combo = field.AsComboBox();
            if (combo.IsEditable)
            {
                combo.EditableText = value;
                if (ControlShowsValue(field, value)) return true;
            }
        }
        catch
        {
            // Not an editable combo.
        }

        try
        {
            if (field.Patterns.Value.IsSupported && !field.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault)
            {
                field.Patterns.Value.Pattern.SetValue(value);
                return true;
            }
        }
        catch
        {
            // Read-only or unsupported Value pattern.
        }

        return false;
    }

    private async Task<bool> TrySelectComboViaExpandAsync(string selectorKey, string value, CancellationToken ct)
    {
        var expanded = false;
        WithoutStealingInput(() =>
        {
            var field = Find(selectorKey);
            try
            {
                if (field.Patterns.ExpandCollapse.IsSupported)
                    field.Patterns.ExpandCollapse.Pattern.Expand();
                else
                    field.AsComboBox().Expand();
                expanded = true;
            }
            catch
            {
                expanded = false;
            }
        });
        if (!expanded) return false;

        AutomationElement? item = null;
        try
        {
            await WaitHelper.WaitUntilAsync(() =>
            {
                item = FindComboListItem(value);
                return item is not null;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(Math.Max(100, _options.PollingIntervalMs)),
            $"combo list item '{value}'", ct);
        }
        catch (WaitTimeoutException)
        {
            WithoutStealingInput(() => TryCollapse(TryFind(selectorKey)));
            return false;
        }

        var selected = false;
        WithoutStealingInput(() =>
        {
            selected = TrySelectListItem(item!);
            TryCollapse(TryFind(selectorKey));
        });
        return selected && ControlShowsValue(TryFind(selectorKey), value);
    }

    private static bool TrySelectListItem(AutomationElement item)
    {
        try
        {
            if (item.Patterns.SelectionItem.IsSupported)
            {
                item.Patterns.SelectionItem.Pattern.Select();
                return true;
            }
        }
        catch { }

        try
        {
            if (item.Patterns.Invoke.IsSupported)
            {
                item.Patterns.Invoke.Pattern.Invoke();
                return true;
            }
        }
        catch { }

        return false;
    }

    private AutomationElement? FindComboListItem(string value)
    {
        bool Matches(AutomationElement e)
        {
            var name = e.Name ?? "";
            if (name.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                if (e.Patterns.Value.IsSupported)
                {
                    var text = e.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
                    if (text.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }

            try
            {
                var child = e.FindFirstDescendant(cf => cf.ByName(value));
                if (child is not null) return true;
            }
            catch { }

            return false;
        }

        IEnumerable<AutomationElement> ListItems(AutomationElement root)
        {
            try
            {
                return root.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            }
            catch
            {
                return Array.Empty<AutomationElement>();
            }
        }

        if (_mainWindow is not null)
        {
            foreach (var candidate in ListItems(_mainWindow))
                if (Matches(candidate)) return candidate;
        }

        if (_automation is null || _app is null) return null;

        AutomationElement? NamedListItem(AutomationElement root)
        {
            try
            {
                return root.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.ListItem).And(cf.ByName(value)));
            }
            catch
            {
                return null;
            }
        }

        foreach (var window in _app.GetAllTopLevelWindows(_automation))
        {
            var named = NamedListItem(window);
            if (named is not null) return named;
            foreach (var candidate in ListItems(window))
                if (Matches(candidate)) return candidate;
        }

        try
        {
            var desktopHit = NamedListItem(_automation.GetDesktop());
            if (desktopHit is not null && desktopHit.Properties.ProcessId == _app.ProcessId)
                return desktopHit;

            foreach (var list in _automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.List)))
            {
                if (list.Properties.ProcessId != _app.ProcessId) continue;
                foreach (var candidate in ListItems(list))
                    if (Matches(candidate)) return candidate;
            }
        }
        catch { }

        return null;
    }

    private static void TryCollapse(AutomationElement? field)
    {
        if (field is null) return;
        try
        {
            if (field.Patterns.ExpandCollapse.IsSupported)
                field.Patterns.ExpandCollapse.Pattern.Collapse();
            else
                field.AsComboBox().Collapse();
        }
        catch { }
    }

    private static bool ControlShowsValue(AutomationElement? element, string value)
    {
        if (element is null) return false;
        if (ReadValue(element).Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var selected = element.AsComboBox().SelectedItem;
            var text = selected?.Text ?? selected?.Name ?? "";
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }

        try
        {
            var combo = element.AsComboBox();
            if (combo.IsEditable && (combo.EditableText ?? "").Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }

        return false;
    }
}
