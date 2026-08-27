using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using WilkenAutomation.Application.Enums;
using WilkenAutomation.Application.Interfaces;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// Control-level interactions: verify and set fields, checkboxes and radios via UIA patterns only, and tree-navigation activation.
/// </summary>
public partial class WindowsWilkenAutomationService
{
    private async Task VerifyValueAsync(string automationId, string expected, CancellationToken ct)
    {
        if (!IsReplica && TryFindByAutomationId(automationId) is null)
        {
            _logger.LogWarning("Field '{Id}' not found on real Wilken; skipping verify '{Expected}'.", automationId, expected);
            return;
        }

        try
        {
            await WaitUntilUiAsync(
                () => ControlShowsValue(TryFindByAutomationId(automationId), expected),
                TimeSpan.FromSeconds(Math.Min(_options.NavigationTimeoutSeconds, 15)),
                $"{automationId} = '{expected}'", ct);
        }
        catch (WaitTimeoutException ex)
        {
            var actual = TryFindByAutomationId(automationId) is { } el ? (SafeUiName(el) + " " + ReadValue(el)) : "<missing>";
            throw new WilkenAutomationException("FIELD_MISMATCH",
                $"Field '{automationId}' is '{actual}', expected '{expected}'.", inner: ex);
        }
    }

    private async Task SetCheckAsync(string automationId, bool expected, CancellationToken ct)
    {
        var box = TryFindByAutomationId(automationId);
        if (box is null)
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Checkbox '{Id}' not found on real Wilken; skipping.", automationId);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 checkbox '{automationId}' was not found.");
        }

        if (CheckIsOn(box) != expected)
        {
            WithoutStealingInput(() =>
            {
                try
                {
                    if (box.Patterns.Toggle.IsSupported)
                    {
                        box.Patterns.Toggle.Pattern.Toggle();
                        return;
                    }
                }
                catch { }

                try
                {
                    box.AsCheckBox().IsChecked = expected;
                    return;
                }
                catch { }

                InvokeControl(box);
            });
        }

        await WaitUntilUiAsync(
            () => CheckIsOn(TryFindByAutomationId(automationId) ?? box) == expected,
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{automationId} {(expected ? "checked" : "unchecked")}", ct);
    }

    private static bool CheckIsOn(AutomationElement? element)
    {
        if (element is null) return false;
        try
        {
            if (element.Patterns.Toggle.IsSupported)
                return element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault == ToggleState.On;
        }
        catch { }

        try { return element.AsCheckBox().IsChecked == true; }
        catch { return false; }
    }

    private async Task SetIdValueAsync(string automationId, string value, CancellationToken ct)
    {
        var field = TryFindByAutomationId(automationId);
        if (field is null)
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Field '{Id}' not found on real Wilken; skipping value '{Value}'.", automationId, value);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 control '{automationId}' was not found.");
        }

        SetControlValue(field, value);
        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFindByAutomationId(automationId), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{automationId} = '{value}'", ct);
    }

    private async Task SetSelectorOrIdAsync(string automationId, string value, CancellationToken ct)
    {
        var field = TryFindByAutomationId(automationId);
        if (field is null)
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Field '{Id}' not found on real Wilken; skipping value '{Value}'.", automationId, value);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 control '{automationId}' was not found.");
        }

        if (field.ControlType == ControlType.ComboBox)
        {
            WithoutStealingInput(() =>
            {
                if (!TrySetComboText(field, value))
                    SetControlValue(field, value);
            });
        }
        else
        {
            SetControlValue(field, value);
        }

        await WaitUntilUiAsync(
            () => ControlShowsValue(TryFindByAutomationId(automationId), value),
            TimeSpan.FromSeconds(_options.NavigationTimeoutSeconds),
            $"{automationId} = '{value}'", ct);
    }

    private void SelectRadio(string automationId)
    {
        var radio = TryFindByAutomationId(automationId);
        if (radio is null)
        {
            if (!IsReplica)
            {
                _logger.LogWarning("Radio '{Id}' not found on real Wilken; skipping.", automationId);
                return;
            }
            throw new WilkenAutomationException("CONTROL_NOT_FOUND", $"CS/2 control '{automationId}' was not found.");
        }

        WithoutStealingInput(() =>
        {
            try
            {
                if (radio.Patterns.SelectionItem.IsSupported)
                {
                    radio.Patterns.SelectionItem.Pattern.Select();
                    return;
                }
            }
            catch { }

            try
            {
                radio.AsRadioButton().IsChecked = true;
                return;
            }
            catch { }

            InvokeControl(radio);
        });
    }

    private bool RadioIsSelected(string automationId)
    {
        var radio = TryFindByAutomationId(automationId);
        if (radio is null) return false;
        try
        {
            if (radio.Patterns.SelectionItem.IsSupported)
                return radio.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
        }
        catch { }

        try
        {
            if (radio.Patterns.Toggle.IsSupported)
                return radio.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault == ToggleState.On;
        }
        catch { }

        try { return radio.AsRadioButton().IsChecked; }
        catch { return false; }
    }

    private AutomationElement FindByAutomationId(string automationId) =>
        TryFindByAutomationId(automationId)
        ?? throw new WilkenAutomationException("CONTROL_NOT_FOUND",
            $"CS/2 control '{automationId}' was not found.");

    /// <summary>
    /// TreeView items expose SelectionItem, not Invoke. Re-selecting an already
    /// selected node does not fire SelectedItemChanged, so force a toggle via Nav_Home
    /// and wait until the previous selection is actually cleared.
    /// </summary>
    private async Task ActivateReplicaControlAsync(string automationId, CancellationToken ct)
    {
        var element = FindByAutomationId(automationId);
        var alreadySelected = false;
        try
        {
            alreadySelected = element.Patterns.SelectionItem.IsSupported
                && element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
        }
        catch { }

        if (alreadySelected && !string.Equals(automationId, "Nav_Home", StringComparison.OrdinalIgnoreCase))
        {
            var home = TryFindByAutomationId("Nav_Home");
            WithoutStealingInput(() =>
            {
                try
                {
                    if (home?.Patterns.SelectionItem.IsSupported == true)
                        home.Patterns.SelectionItem.Pattern.Select();
                }
                catch { }
            });

            try
            {
                await WaitUntilUiAsync(() =>
                {
                    var current = TryFindByAutomationId(automationId);
                    if (current is null) return true;
                    try
                    {
                        return !current.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
                    }
                    catch
                    {
                        return true;
                    }
                }, TimeSpan.FromSeconds(3), $"deselect {automationId} via Nav_Home", ct);
            }
            catch (WaitTimeoutException)
            {
                _logger.LogWarning("Nav_Home did not deselect {NavId}; will re-select anyway.", automationId);
            }

            element = FindByAutomationId(automationId);
        }

        WithoutStealingInput(() =>
        {
            try
            {
                if (element.Patterns.SelectionItem.IsSupported)
                {
                    element.Patterns.SelectionItem.Pattern.Select();
                    return;
                }
            }
            catch { }

            InvokeControl(element);
        });
    }
}
