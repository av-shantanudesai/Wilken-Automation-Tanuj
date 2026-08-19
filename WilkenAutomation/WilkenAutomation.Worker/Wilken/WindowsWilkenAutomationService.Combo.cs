using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

public partial class WindowsWilkenAutomationService
{
    /// <summary>
    /// WPF ComboBox.Select() expands a popup whose items are not children of the
    /// combo in the UIA tree, so FlaUI leaves the dropdown open and never selects.
    /// Prefer Value/editable text, then click the popup ListItem, then keyboard.
    /// </summary>
    private async Task SetSelectorValueAsync(string selectorKey, string value, CancellationToken ct)
    {
        var field = Find(selectorKey);
        if (field.ControlType == ControlType.ComboBox)
            await SelectComboItemAsync(selectorKey, field, value, ct);
        else
            field.AsTextBox().Text = value;

        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFind(selectorKey), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{selectorKey} = '{value}'", ct);
    }

    private async Task SelectComboItemAsync(string selectorKey, AutomationElement field, string value, CancellationToken ct)
    {
        TryCollapse(field);

        if (await TryClickComboListItemAsync(selectorKey, value, ct))
            return;

        field = Find(selectorKey);
        if (TrySetComboText(field, value) && ControlShowsValue(field, value))
            return;

        TryTypeComboValue(Find(selectorKey), value);
    }

    private static bool TrySetComboText(AutomationElement field, string value)
    {
        try
        {
            var combo = field.AsComboBox();
            if (combo.IsEditable)
            {
                combo.EditableText = value;
                return true;
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

    private async Task<bool> TryClickComboListItemAsync(string selectorKey, string value, CancellationToken ct)
    {
        var field = Find(selectorKey);
        try { field.AsComboBox().Expand(); }
        catch { field.Click(); }

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
            TryCollapse(TryFind(selectorKey));
            return false;
        }

        try
        {
            if (item!.Patterns.SelectionItem.IsSupported)
                item.Patterns.SelectionItem.Pattern.Select();
            else if (item.Patterns.Invoke.IsSupported)
                item.Patterns.Invoke.Pattern.Invoke();
            else
                item.Click();
        }
        catch
        {
            item!.Click();
        }

        TryCollapse(TryFind(selectorKey));
        return ControlShowsValue(TryFind(selectorKey), value);
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

    private static void TryTypeComboValue(AutomationElement field, string value)
    {
        TryCollapse(field);
        field.Focus();
        Keyboard.Type(value);
        Keyboard.Press(VirtualKeyShort.ENTER);
        TryCollapse(field);
    }

    private static void TryCollapse(AutomationElement? field)
    {
        if (field is null) return;
        try { field.AsComboBox().Collapse(); }
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
