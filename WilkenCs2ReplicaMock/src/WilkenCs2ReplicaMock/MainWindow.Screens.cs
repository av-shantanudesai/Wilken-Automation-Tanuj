// Report screens (Zugangsliste/Anlagenspiegel), their form-builder helpers, and the report run flow.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WilkenCs2ReplicaMock.Models;

namespace WilkenCs2ReplicaMock;

public partial class MainWindow
{
    private void ShowZugangsliste()
    {
        SetChrome(false);
        _screen = "Zugangsliste";
        _activeReport = _zugang;
        _selectedSpoolReport = null;
        _protocolCount = 2;
        ProtocolCount.Text = "▱  Protokolle/Listen (2)";
        ScreenTitle.Text = "Anlagenbuchhaltung - Zugangsliste erstellen";
        ExecuteButton.Content = "✓";
        ExecuteButton.IsEnabled = true;
        StatusText.Text = "Bereit";
        StatusModule.Text = "CA50";
        StatusPage.Text = "1/2";
        SetRightPanel(false, 1);
        MainContent.Content = BuildReportScreen(_zugang, isAccessList: true, isCondensed: false);
    }

    private void ShowAnlagenspiegel(ReportDefinition report)
    {
        SetChrome(false);
        _screen = report.Kind == ReportKind.AlleAnlagenNachKontenVerdichtet ? "Verdichtet" : "Anlagenspiegel";
        _activeReport = report;
        _selectedSpoolReport = null;
        _protocolCount = report.Kind == ReportKind.AlleAnlagenNachKontenVerdichtet ? 8 : 12;
        ProtocolCount.Text = $"▱  Protokolle/Listen ({_protocolCount})";
        ScreenTitle.Text = "Anlagenbuchhaltung - Anlagenspiegel erstellen";
        ExecuteButton.Content = "✓";
        ExecuteButton.IsEnabled = true;
        StatusText.Text = "Bereit";
        StatusModule.Text = "CA45";
        StatusPage.Text = "1/2";
        SetRightPanel(report.Kind == ReportKind.AlleAnlagenNachKontenVerdichtet, 3);
        MainContent.Content = BuildReportScreen(report, isAccessList: false, isCondensed: report.Kind == ReportKind.AlleAnlagenNachKontenVerdichtet);
    }

    private FrameworkElement BuildReportScreen(ReportDefinition report, bool isAccessList, bool isCondensed)
    {
        var outer = new Grid { Margin = new Thickness(16, 20, 16, 12) };
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(128) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var processGrid = new Grid { Margin = new Thickness(18, 0, 20, 5) };
        processGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        processGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        processGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
        processGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        processGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
        processGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        processGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });

        AddText(processGrid, "Prozess", 0, 0);
        var processInput = AddBox(processGrid, "", 1, 0, isAccessList ? "Zugang_Prozess_Input" : "Anlage_Prozess_Input", true);
        processInput.Background = new SolidColorBrush(Color.FromRgb(255, 235, 135));
        var processCode = AddBox(processGrid, report.Process, 1, 1, isAccessList ? "Zugang_Prozess" : "Anlage_Prozess", false);
        processCode.Background = new SolidColorBrush(Color.FromRgb(218, 230, 246));
        AddText(processGrid, "Bezeichnung", 0, 2);
        AddBox(processGrid, "D", 1, 2, "Report_Language", false);
        var desc = AddBox(processGrid, report.Description, 2, 2, isAccessList ? "Zugang_Bezeichnung" : "Anlage_Bezeichnung", false);
        Grid.SetColumnSpan(desc, 2);
        var processSeparator = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(117, 145, 178)), BorderThickness = new Thickness(0, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom };
        processGrid.Children.Add(processSeparator);
        Grid.SetRow(processSeparator, 1); Grid.SetColumnSpan(processSeparator, 4);
        Grid.SetRow(processGrid, 0);
        outer.Children.Add(processGrid);

        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 0) };
        var controlTab = new TabItem { Header = "Steuerung", Content = BuildControlTab(report, isAccessList, isCondensed) };
        AutomationId(controlTab, "Tab_Steuerung");
        tabs.Items.Add(controlTab);
        if (!isAccessList)
        {
            var sort = new TabItem { Header = "Sortierung für Liste", Content = BuildSortTab() };
            AutomationId(sort, "Tab_Sortierung");
            tabs.Items.Add(sort);
        }
        var log = new TabItem { Header = "Laufprotokoll", Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(238, 244, 252)) } };
        AutomationId(log, "Tab_Laufprotokoll");
        tabs.Items.Add(log);
        Grid.SetRow(tabs, 1);
        outer.Children.Add(tabs);
        return outer;
    }

    private FrameworkElement BuildControlTab(ReportDefinition report, bool access, bool condensed)
    {
        var bg = new SolidColorBrush(Color.FromRgb(239, 245, 252));
        var root = new Grid { Background = bg, Margin = new Thickness(0), MinHeight = 515 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(145) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var mode = BuildModeGroup(!access); Grid.SetColumn(mode, 0); Grid.SetRow(mode, 0); root.Children.Add(mode);
        var schedule = BuildScheduleGroup(access ? "19.08.2026" : "21.08.2026"); Grid.SetColumn(schedule, 1); Grid.SetRow(schedule, 0); root.Children.Add(schedule);
        var status = BuildStatusGroup(); Grid.SetColumn(status, 2); Grid.SetRow(status, 0); root.Children.Add(status);
        var selection = BuildSelectionGroup(report, access); Grid.SetColumn(selection, 0); Grid.SetRow(selection, 1); root.Children.Add(selection);
        var creation = BuildCreationGroup(access, condensed); Grid.SetColumn(creation, 1); Grid.SetRow(creation, 1); root.Children.Add(creation);
        var right = BuildPermissionsAndCurrencyGroup(); Grid.SetColumn(right, 2); Grid.SetRow(right, 1); root.Children.Add(right);
        return root;
    }

    private GroupBox BuildModeGroup(bool logChecked)
    {
        var g = Group("Modus");
        var s = new StackPanel();
        s.Children.Add(Check("Aktiv", true, "Field_Aktiv"));
        s.Children.Add(Check("Automatisch deaktivieren", false, "Field_AutoDeactivate"));
        s.Children.Add(Check("Laufprotokoll", logChecked, "Field_Laufprotokoll"));
        g.Content = s;
        return g;
    }

    private GroupBox BuildScheduleGroup(string lastRunDate)
    {
        var g = Group("Laufsteuerung");
        var p = FormGrid(3, 140, 150);
        AddLabeledArrow(p, "Rhythmus", "", 0, "Field_Rhythmus");
        AddLabeled(p, "Nächstes Laufdatum", "", 1, "Field_NextRun");
        AddLabeled(p, "Letztes Laufdatum", lastRunDate, 2, "Field_LastRun", false);
        g.Content = p;
        return g;
    }

    private GroupBox BuildStatusGroup()
    {
        var g = Group("Status");
        var p = FormGrid(3, 150, 110);
        AddLabeled(p, "Bearbeitungszustand", "OK", 0, "Field_Status", false);
        AddLabeled(p, "Letzter Returncode", "", 1, "Field_ReturnCode", false);
        AddLabeled(p, "Letzte Fehlernummer", "", 2, "Field_LastError", false);
        g.Content = p;
        return g;
    }

    private GroupBox BuildSelectionGroup(ReportDefinition report, bool access)
    {
        var g = Group("Auswahl");
        var root = new StackPanel();
        var p = FormGrid(access ? 7 : 6, 135, 285);
        AddLabeledCombo(p, "Art", access ? "Bericht" : "Kompletter Datenbestand", 0, access ? "Zugang_Art" : "Anlage_Art");
        AddLabeledSearch(p, "Bericht", access ? "NACH ANLAGEN" : "", 1, access ? "Zugang_Bericht" : "Anlage_Bericht", !access);
        AddLabeledCombo(p, "Fachbereich", report.Fachbereich, 2, access ? "Zugang_Fachbereich" : "Anlage_Fachbereich");
        AddValuePlanRow(p, report.Wertart, 3, access ? "Zugang_Wertart" : "Anlage_Wertart");
        AddFromToHeader(p, 4);
        if (access)
        {
            AddRangeRow(p, "Zugangsdatum", "01.01.2020", "31.12.2020", 5, "Zugang_DateFrom", "Zugang_DateTo");
            AddPeriodRow(p, "Zeitraum", "01", "2020", "12", "2020", 6, "Zugang_Period");
        }
        else
        {
            AddPeriodRow(p, "Zeitraum", "01", "2021", "12", "2021", 5, "Anlage_Period");
        }
        root.Children.Add(p);
        g.Content = root;
        return g;
    }

    private GroupBox BuildCreationGroup(bool access, bool condensed)
    {
        var g = Group("Erstellung");
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(108) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var top = new StackPanel();
        var p = FormGrid(1, 135, 250);
        AddLabeledCombo(p, "Art", "Druckversion", 0, "Field_ErstellungArt");
        top.Children.Add(p);
        top.Children.Add(Check("Summe für Anlagenhauptnummer", false, "Field_SumMainAsset"));
        if (access) top.Children.Add(Check("Mit Umbuchungs-Gegenkonto bei Kontenselektion", false, "Zugang_Gegenkonto"));
        Grid.SetRow(top, 0); root.Children.Add(top);

        var lower = Group(access ? "Buchungsart" : "Einzelne Buchungen für Anlagen");
        lower.Margin = new Thickness(0, 4, 0, 0);
        var bs = new StackPanel();
        if (access)
        {
            bs.Children.Add(Check("Umbuchung Bilanzposition", true, "Zugang_UmbuchungBilanzposition"));
            bs.Children.Add(Check("Umbuchung Anlage", true, "Zugang_UmbuchungAnlage"));
        }
        else
        {
            bs.Children.Add(Check("Zugänge", !condensed, "Anlage_Zugaenge"));
            bs.Children.Add(Check("Abgänge", !condensed, "Anlage_Abgaenge"));
            bs.Children.Add(Check("Umbuchung Anlage", !condensed, "Anlage_Umbuchung"));
        }
        lower.Content = bs;
        Grid.SetRow(lower, 1); root.Children.Add(lower);
        g.Content = root;
        return g;
    }

    private GroupBox BuildPermissionsAndCurrencyGroup()
    {
        var g = Group("Berechtigung");
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(118) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var p = FormGrid(2, 125, 95);
        AddLabeledArrow(p, "Verwalten", "", 0, "Field_Manage");
        AddLabeledArrow(p, "Ausführen", "", 1, "Field_RunPermission");
        Grid.SetRow(p, 0); root.Children.Add(p);
        var currency = Group("Fremdwährung");
        currency.Margin = new Thickness(0, 4, 0, 0);
        var c = FormGrid(2, 125, 95);
        AddLabeledArrow(c, "Währungsschlüssel", "", 0, "Field_CurrencyKey");
        AddLabeledArrow(c, "Kennzeichen", "", 1, "Field_CurrencyFlag");
        currency.Content = c;
        Grid.SetRow(currency, 1); root.Children.Add(currency);
        g.Content = root;
        return g;
    }

    private FrameworkElement BuildSortTab()
    {
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(239, 245, 252)), Margin = new Thickness(18, 12, 18, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(235) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
        root.RowDefinitions.Add(new RowDefinition());

        var sortGroup = Group("Sortierung");
        sortGroup.Margin = new Thickness(0, 0, 0, 8);
        var sortGrid = new Grid();
        sortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(315) });
        sortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        sortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        sortGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        for (var i = 0; i < 5; i++) sortGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        AddSortHeader(sortGrid, "Sortierkriterium", 0); AddSortHeader(sortGrid, "Summen", 1); AddSortHeader(sortGrid, "Seitenwechsel", 2);
        var values = new[] { "Hauptkonto", "Anlagennummer", "", "", "" };
        for (var i = 0; i < values.Length; i++)
        {
            var combo = new ComboBox { IsEditable = true, Text = values[i], Width = 290, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationId(combo, $"Sort_Kriterium_{i + 1}"); Grid.SetColumn(combo, 0); Grid.SetRow(combo, i + 1); sortGrid.Children.Add(combo);
            var sum = new CheckBox { IsChecked = i == 0, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 4, 0, 0) };
            AutomationId(sum, $"Sort_Summe_{i + 1}"); Grid.SetColumn(sum, 1); Grid.SetRow(sum, i + 1); sortGrid.Children.Add(sum);
            var page = new CheckBox { IsChecked = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 4, 0, 0) };
            AutomationId(page, $"Sort_Seitenwechsel_{i + 1}"); Grid.SetColumn(page, 2); Grid.SetRow(page, i + 1); sortGrid.Children.Add(page);
        }
        sortGroup.Content = sortGrid; Grid.SetRow(sortGroup, 0); root.Children.Add(sortGroup);

        var amount = Group("Betragseinschränkung"); amount.Margin = new Thickness(0, 8, 0, 0);
        var ag = new Grid(); ag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) }); ag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(265) }); ag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); ag.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        ag.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); ag.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        AddText(ag, "AHK", 0, 0); AddAmountBox(ag, "0.00", 1, 0, "Sort_AHK"); AddSortCombo(ag, 2, 0, "Sort_AHK_Operator"); AddSortCombo(ag, 3, 0, "Sort_AHK_Unit");
        AddText(ag, "Restbuchwert", 0, 1); AddAmountBox(ag, "0.00", 1, 1, "Sort_Restbuchwert"); AddSortCombo(ag, 2, 1, "Sort_Rest_Operator"); AddSortCombo(ag, 3, 1, "Sort_Rest_Unit");
        amount.Content = ag; Grid.SetRow(amount, 1); root.Children.Add(amount);
        return root;
    }

    private static void AddSortHeader(Grid g, string text, int col)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 2, 0, 4), Foreground = new SolidColorBrush(Color.FromRgb(40, 63, 105)) };
        Grid.SetColumn(t, col); Grid.SetRow(t, 0); g.Children.Add(t);
    }

    private static void AddSortCombo(Grid g, int col, int row, string id)
    {
        var c = new ComboBox { Width = col == 2 ? 200 : 130, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationId(c, id); Grid.SetColumn(c, col); Grid.SetRow(c, row); g.Children.Add(c);
    }

    private static void AddAmountBox(Grid g, string value, int col, int row, string id)
    {
        var b = new TextBox { Text = value, Width = 250, HorizontalContentAlignment = HorizontalAlignment.Right };
        AutomationId(b, id); Grid.SetColumn(b, col); Grid.SetRow(b, row); g.Children.Add(b);
    }

    private static Grid FormGrid(int rows, double labelWidth = 135, double inputWidth = 250)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(inputWidth) });
        for (var i = 0; i < rows; i++) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        return g;
    }

    private static void AddLabeled(Grid g, string label, string value, int row, string id, bool enabled = true)
    {
        AddText(g, label, 0, row);
        AddBox(g, value, 1, row, id, enabled);
    }

    private static void AddLabeledArrow(Grid g, string label, string value, int row, string id)
    {
        AddText(g, label, 0, row);
        var wrap = new Grid(); wrap.ColumnDefinitions.Add(new ColumnDefinition()); wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        var box = new TextBox { Text = value }; AutomationId(box, id); wrap.Children.Add(box);
        var arrow = new Button { Content = "›", Padding = new Thickness(0), Width = 25 }; Grid.SetColumn(arrow, 1); wrap.Children.Add(arrow);
        Grid.SetColumn(wrap, 1); Grid.SetRow(wrap, row); g.Children.Add(wrap);
    }

    private static void AddLabeledCombo(Grid g, string label, string value, int row, string id)
    {
        AddText(g, label, 0, row);
        var c = new ComboBox { IsEditable = true, Text = value };
        AutomationId(c, id); Grid.SetColumn(c, 1); Grid.SetRow(c, row); g.Children.Add(c);
    }

    private static void AddLabeledSearch(Grid g, string label, string value, int row, string id, bool disabled)
    {
        AddText(g, label, 0, row);
        var wrap = new Grid(); wrap.ColumnDefinitions.Add(new ColumnDefinition()); wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        var b = new TextBox { Text = value, IsReadOnly = disabled, Background = disabled ? new SolidColorBrush(Color.FromRgb(218, 230, 246)) : Brushes.White };
        AutomationId(b, id); wrap.Children.Add(b);
        var search = new Button { Content = "⌕", Padding = new Thickness(0), Width = 25 }; AutomationId(search, id + "_Search"); Grid.SetColumn(search, 1); wrap.Children.Add(search);
        Grid.SetColumn(wrap, 1); Grid.SetRow(wrap, row); g.Children.Add(wrap);
    }

    private static void AddValuePlanRow(Grid g, string value, int row, string id)
    {
        AddText(g, "Wertart/Plan", 0, row);
        var s = new Grid(); s.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) }); s.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var combo = new ComboBox { IsEditable = true, Text = value }; AutomationId(combo, id); s.Children.Add(combo);
        var number = new Grid(); number.ColumnDefinitions.Add(new ColumnDefinition()); number.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        number.Children.Add(new TextBox { Text = "0" }); var arrow = new Button { Content = "›", Padding = new Thickness(0) }; Grid.SetColumn(arrow, 1); number.Children.Add(arrow);
        Grid.SetColumn(number, 1); s.Children.Add(number); Grid.SetColumn(s, 1); Grid.SetRow(s, row); g.Children.Add(s);
    }

    private static void AddFromToHeader(Grid g, int row)
    {
        var s = new Grid();
        s.ColumnDefinitions.Add(new ColumnDefinition());
        s.ColumnDefinitions.Add(new ColumnDefinition());
        var from = new TextBlock { Text = "Von", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 };
        var to = new TextBlock { Text = "Bis", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 };
        Grid.SetColumn(to, 1);
        s.Children.Add(from); s.Children.Add(to);
        Grid.SetColumn(s, 1); Grid.SetRow(s, row); g.Children.Add(s);
    }

    private static void AddRangeRow(Grid g, string label, string from, string to, int row, string fromId, string toId)
    {
        AddText(g, label, 0, row);
        var s = new Grid(); s.ColumnDefinitions.Add(new ColumnDefinition()); s.ColumnDefinitions.Add(new ColumnDefinition());
        var a = new TextBox { Text = from }; AutomationId(a, fromId); s.Children.Add(a);
        var b = new TextBox { Text = to }; AutomationId(b, toId); Grid.SetColumn(b, 1); s.Children.Add(b);
        Grid.SetColumn(s, 1); Grid.SetRow(s, row); g.Children.Add(s);
    }

    private static void AddPeriodRow(Grid g, string label, string fm, string fy, string tm, string ty, int row, string id)
    {
        AddText(g, label, 0, row);
        var s = new Grid();
        s.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); s.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        s.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); s.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        var values = new[] { fm, fy, tm, ty };
        for (var i = 0; i < values.Length; i++) { var b = new TextBox { Text = values[i] }; AutomationId(b, $"{id}_{i}"); Grid.SetColumn(b, i); s.Children.Add(b); }
        AutomationId(s, id); Grid.SetColumn(s, 1); Grid.SetRow(s, row); g.Children.Add(s);
    }

    private static TextBlock AddText(Grid g, string text, int col, int row)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, col); Grid.SetRow(t, row); g.Children.Add(t); return t;
    }

    private static TextBox AddBox(Grid g, string text, int col, int row, string id, bool enabled = true)
    {
        var b = new TextBox { Text = text, IsReadOnly = !enabled, Background = enabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(218, 230, 246)) };
        AutomationId(b, id); Grid.SetColumn(b, col); Grid.SetRow(b, row); g.Children.Add(b); return b;
    }

    private async Task RunReportAsync(ReportDefinition report)
    {
        // All three supplied recordings show an explicit confirmation before the run.
        var title = report.Kind == ReportKind.Zugangsliste ? "Zugangsliste" : "Anlagenspiegel";
        var text = report.Kind == ReportKind.Zugangsliste ? "Die angeforderte Liste erstellen?" : "Anlagenspiegel erstellen?";
        var confirm = new ConfirmWindow(title, text, report.Kind == ReportKind.Zugangsliste ? "Confirm_Zugangsliste" : "Confirm_Anlagenspiegel") { Owner = this };
        if (confirm.ShowDialog() != true) return;

        var profile = _timing.For(report.Kind);
        StatusText.Text = $"{report.Description}: Verarbeitung läuft";
        ExecuteButton.IsEnabled = false;
        var progress = new ProgressWindow { Owner = this };
        progress.Show();
        SetInteractiveEnabled(false);

        try
        {
            if (report.Kind == ReportKind.Zugangsliste)
            {
                // Zugangsliste recording: the visible progress state is "Ermitteln der Werte gestartet.".
                await RunProgressStage(progress, "Ermitteln der Werte gestartet.", profile.GenerationStage1Ms, 0, 98);
            }
            else
            {
                // Anlagenspiegel recordings show three distinct application states.
                var selectionMs = Math.Max(500, profile.GenerationStage1Ms / 3);
                var valuesMs = Math.Max(500, profile.GenerationStage1Ms - selectionMs);
                await RunProgressStage(progress, "Anlagenselektion gestartet.", selectionMs, 0, 24);
                await RunProgressStage(progress, "Ermitteln der Werte gestartet.", valuesMs, 24, 78);
                if (profile.GenerationStage2Ms > 0)
                    await RunProgressStage(progress, "Der Anlagenspiegel wird erstellt.", profile.GenerationStage2Ms, 78, 98);
            }
        }
        finally
        {
            SetInteractiveEnabled(true);
            progress.Close();
        }

        AddGeneratedSpool(report);
        _protocolCount += 2;
        ProtocolCount.Text = $"▱  Protokolle/Listen ({_protocolCount})";
        var lastRun = FindByAutomationId<TextBox>(MainContent.Content as DependencyObject, "Field_LastRun");
        if (lastRun is not null) lastRun.Text = "21.08.2026";
        StatusText.Text = $"{report.Description} erstellt";
        ExecuteButton.IsEnabled = true;
    }

    private void SetInteractiveEnabled(bool enabled)
    {
        NavigationTree.IsEnabled = enabled;
        MainContent.IsEnabled = enabled;
        ExecuteButton.IsEnabled = enabled;
        RightPrimaryItems.IsEnabled = enabled;
    }

    private static async Task RunProgressStage(ProgressWindow progress, string message, int ms, int start, int end)
    {
        progress.SetMessage(message);
        await DelayWithProgress(progress, ms, start, end);
    }

    private static async Task DelayWithProgress(ProgressWindow progress, int ms, int start, int end)
    {
        if (ms <= 0) return;
        var chunks = Math.Max(1, ms / 250);
        for (var i = 0; i < chunks; i++)
        {
            await Task.Delay(ms / chunks);
            progress.SetProgress(start + (end - start) * (i + 1) / chunks);
        }
    }
}
