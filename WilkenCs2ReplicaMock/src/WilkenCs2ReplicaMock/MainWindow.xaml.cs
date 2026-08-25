using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WilkenCs2ReplicaMock.Models;
using WilkenCs2ReplicaMock.Services;

namespace WilkenCs2ReplicaMock;

public partial class MainWindow : Window
{
    private readonly TimingService _timing = new();
    private readonly XlsxExportService _xlsx = new();
    private readonly ObservableCollection<SpoolLine> _spool = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private ReportDefinition _activeReport;
    private ReportDefinition? _selectedSpoolReport;
    private DataGrid? _spoolGrid;
    private string _screen = "Home";
    private int _protocolCount = 2;
    private bool _openedFromProcessManager;
    private SpoolLine? _latestDataMeta;

    private readonly ReportDefinition _zugang = new(
        ReportKind.Zugangsliste, "001", "Zugangsliste", "Handelsrecht", "Ist", "01/2020", "12/2020", 37,
        "CAB024", "B024", "PRT", "Zugangsliste");

    private readonly ReportDefinition _anlage = new(
        ReportKind.AnlagenspiegelDetailliert, "001", "Anlagenspiegel nach Anlagen", "Steuerrecht", "Ist", "01/2021", "12/2021", 31,
        "CAB015", "4J0402", "001", "Anlagenspiegel nach Anlagen");

    private readonly ReportDefinition _verdichtet = new(
        ReportKind.AlleAnlagenNachKontenVerdichtet, "003", "Alle Anlagen nach Konten verdichtet", "Steuerrecht", "Ist", "01/2021", "12/2021", 23,
        "CAB015", "4J0402", "003", "Alle Anlagen nach Konten verdichtet");

    public MainWindow()
    {
        InitializeComponent();
        _activeReport = _zugang;
        BuildNavigation();
        SeedSpool();
        _clockTimer.Tick += (_, _) => StatusClock.Text = DateTime.Now.ToString("HH.mm.ss");
        _clockTimer.Start();
        ShowHome();
    }

    private static void AutomationId(DependencyObject control, string id) => AutomationProperties.SetAutomationId(control, id);

    private void BuildNavigation()
    {
        var root = Node("Anlagenbuchhaltung", "Home");
        var anzeigen = Node("Anzeigefunktionen",
            Node("Anlagenübersicht anzeigen"), Node("Buchungsübersicht anzeigen"), Node("Hierarchische Anlagenübersicht anzeigen"));
        var definitions = Node("Einzeldefinitionen",
            Node("Abgangsliste erstellen"), Node("Abstimmung Finanzbuchhaltung ausführen"), Node("AfA Rechnen ausführen"),
            Node("AfA Vergleich ausführen"), Node("AfA Vorschau liste erstellen"), Node("Aktivierungsumbuchung aus Controlling"),
            Node("Anlagenspiegel erstellen", "Anlagenspiegel"), Node("Fachbereich Inventur anlegen"), Node("Zinsen und Versicherung verwalten"),
            Node("Buchungsliste erstellen"), Node("Datenexport ausführen"), Node("Fusion und Datenumschlüsselung ausführen"),
            Node("Geschäftsjahresabschluss ausführen"), Node("Integrative Prüfung ausführen"), Node("Inventarisierung Export ausführen"),
            Node("Inventarisierung Import ausführen"), Node("Inventurliste erstellen"), Node("Konsolidierter Anlagenspiegel ausführen"),
            Node("Lebenslaufliste erstellen"), Node("Maschinelles Buchen ausführen"), Node("Massendatenänderung ausführen"),
            Node("Prüflauf ausführen"), Node("Reportingdaten erstellen"), Node("Versicherungsliste erstellen"),
            Node("Fälligkeitsliste Wertpapiere erstellen"), Node("Zinsliste erstellen"), Node("Zugangsliste erstellen", "Zugangsliste"),
            Node("Übergabe AfA und Buchungen ausführen"), Node("Übergabe von AfA-Planwerten an Controlling"), Node("Übernahme aus Finanzbuchhaltung ausführen"));
        var processes = Node("Prozesse", definitions, Node("Prozesse verwalten", "ProzesseVerwalten"));
        root.Items.Add(anzeigen);
        root.Items.Add(processes);
        root.Items.Add(Node("Schlüsseldaten"));
        root.Items.Add(Node("Systemfunktion"));
        root.Items.Add(Node("Anlagenstamm verwalten"));
        root.Items.Add(Node("Buchung erfassen"));
        root.Items.Add(Node("Buchung verwalten"));
        root.Items.Add(Node("Liste anzeigen", "ListeAnzeigen"));
        root.Items.Add(Node("Report anzeigen"));
        root.Items.Add(Node("Startwerte verwalten"));

        NavigationTree.Items.Add(root);
        foreach (var name in new[] { "Basismodul", "Finanzbuchhaltung", "Anzeigefunktionen", "Berichtswesen", "Buchungsfunktionen", "Electronic Banking", "Mahnwesen", "Meldewesen" })
            NavigationTree.Items.Add(Node(name));

        root.IsExpanded = true;
        processes.IsExpanded = true;
        definitions.IsExpanded = true;
        AutomationId(NavigationTree, "Navigation_Tree");

        // UIA Invoke fallback: re-selecting an already-selected TreeView item does not
        // fire SelectedItemChanged, which blocked the second job after Ausführen.
        var openListe = HiddenAutomationButton("Nav_ListeAnzeigen_Open", "Liste anzeigen öffnen",
            () => _ = ShowPrintSelectionAndSpoolAsync());
        openListe.Width = 1;
        openListe.Height = 1;
        openListe.Opacity = 0.01;
        openListe.Margin = new Thickness(0);
        openListe.HorizontalAlignment = HorizontalAlignment.Left;
        openListe.VerticalAlignment = VerticalAlignment.Top;
        ((Grid)WorkTitleBar.Child).Children.Add(openListe);
    }

    private static TreeViewItem Node(string text, params TreeViewItem[] children) =>
        Node(text, null, children);

    private static TreeViewItem Node(string text, string? tag, params TreeViewItem[] children)
    {
        var item = new TreeViewItem { Header = text, Tag = tag };
        foreach (var child in children) item.Items.Add(child);
        if (tag is not null) AutomationProperties.SetAutomationId(item, $"Nav_{tag}");
        return item;
    }

    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item || item.Tag is not string tag) return;
        OpenNavigation(tag);
    }

    private void OpenNavigation(string tag)
    {
        switch (tag)
        {
            case "Home":
                // Selecting the Anlagenbuchhaltung root only expands the tree. Do not unwind child windows.
                break;
            case "Zugangsliste":
                _openedFromProcessManager = false;
                ShowZugangsliste();
                break;
            case "Anlagenspiegel":
                _openedFromProcessManager = false;
                ShowAnlagenspiegel(_anlage);
                break;
            case "ProzesseVerwalten":
                // Real Wilken keeps the process manager instance open underneath the child report/list/export windows.
                // Re-opening it from Navigation while one of those child windows is active produces "Funktion gesperrt".
                if (_openedFromProcessManager && (_screen is "Zugangsliste" or "Anlagenspiegel" or "Verdichtet" or "ListeBase" or "Spool" or "Export"))
                    ShowFunctionLockedDialog();
                else
                    ShowProcessManager();
                break;
            case "ListeAnzeigen":
                _ = ShowPrintSelectionAndSpoolAsync();
                break;
        }
    }

    private void InternalCloseButton_Click(object sender, RoutedEventArgs e)
    {
        // This is the small X inside the blue Wilken child-window title bar, not the operating-system close button.
        // The real application must be unwound one active child window at a time after each export.
        switch (_screen)
        {
            case "Export":
                ShowSpool();
                StatusText.Text = "Gitterbox-Export geschlossen";
                break;

            case "Spool":
            case "ListeBase":
                HidePrintSelectionOverlay();
                ReturnToActiveReport();
                StatusText.Text = "Liste anzeigen geschlossen";
                break;

            case "Zugangsliste":
            case "Anlagenspiegel":
            case "Verdichtet":
                if (_openedFromProcessManager)
                {
                    ShowProcessManager();
                    StatusText.Text = "Prozessübersicht";
                }
                else
                {
                    ShowHome();
                }
                break;

            case "ProcessManager":
                ShowHome();
                break;
        }
    }

    private void ReturnToActiveReport()
    {
        switch (_activeReport.Kind)
        {
            case ReportKind.Zugangsliste:
                ShowZugangsliste();
                break;
            case ReportKind.AnlagenspiegelDetailliert:
                ShowAnlagenspiegel(_anlage);
                break;
            default:
                ShowAnlagenspiegel(_verdichtet);
                break;
        }
    }

    private void ShowFunctionLockedDialog()
    {
        // Defer so UIA SelectionItem.Select() can return. A synchronous ShowDialog
        // here would block the worker on the same call that opened the lock dialog.
        Dispatcher.BeginInvoke(() =>
        {
            var dialog = new FunctionLockedWindow { Owner = this };
            dialog.ShowDialog();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_screen is not ("Zugangsliste" or "Anlagenspiegel" or "Verdichtet")) return;
        StatusText.Text = "Prozess aktualisiert";
    }

    private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_screen == "Export") { await ExportAsync(); return; }
        if (_screen is "Spool" or "ProcessManager" or "ListeBase") return;
        await RunReportAsync(_activeReport);
    }

    private void SetRightPanel(bool followActions, int followCount = 1)
    {
        RightPrimaryItems.Children.Clear();
        if (followActions)
        {
            RightPrimaryTitle.Text = "Folgeaktionen";
            foreach (var line in new[] { "⌕  Suchen", "⚙  Logging", "ABC  Bezeichnung" })
                RightPrimaryItems.Children.Add(new TextBlock { Text = line, Margin = new Thickness(2, 4, 0, 4), Foreground = new SolidColorBrush(Color.FromRgb(57, 86, 119)) });
            FollowUpCount.Text = $"➜  Folgeaktionen ({followCount})";
        }
        else
        {
            RightPrimaryTitle.Text = "Dokumente";
            FollowUpCount.Text = $"➜  Folgeaktionen ({followCount})";
        }
    }

    private void SetChrome(bool exportMode, bool processManager = false)
    {
        HidePrintSelectionOverlay();
        WorkTitleRow.Height = new GridLength(25);
        WorkMenuRow.Height = new GridLength(20);
        WorkToolbarRow.Height = new GridLength(31);
        WorkTitleBar.Visibility = Visibility.Visible;
        WorkMenuBar.Visibility = Visibility.Visible;
        WorkToolbarBar.Visibility = Visibility.Visible;

        if (exportMode)
        {
            RightPanel.Visibility = Visibility.Collapsed;
            RightColumn.Width = new GridLength(0);
            ToolbarStandardControls.Visibility = Visibility.Collapsed;
            ToolbarTailControls.Visibility = Visibility.Collapsed;
            SaveButton.Visibility = Visibility.Collapsed;
            MenuSecond.Visibility = Visibility.Visible;
            MenuSecond.Text = "Optionen";

            // In the recorded Wilken Gitterbox screen there is NO large text button named
            // "Export" inside the form. The export is started by the green check icon in
            // the top toolbar after XLSX / Excel / Alle are selected. Make that exact action
            // visually obvious while keeping the recorded Wilken interaction model.
            ExecuteButton.Content = "✔";
            ExecuteButton.ToolTip = "Export ausführen";
            ExecuteButton.Foreground = new SolidColorBrush(Color.FromRgb(0, 145, 58));
            ExecuteButton.FontWeight = FontWeights.Bold;
            ExecuteButton.FontSize = 15;
            AutomationProperties.SetName(ExecuteButton, "Export ausführen");
        }
        else
        {
            RightPanel.Visibility = Visibility.Visible;
            RightColumn.Width = new GridLength(215);
            ToolbarStandardControls.Visibility = Visibility.Visible;
            ToolbarTailControls.Visibility = Visibility.Visible;
            SaveButton.Visibility = Visibility.Visible;
            MenuSecond.Text = "Aktionen";
            MenuSecond.Visibility = processManager ? Visibility.Collapsed : Visibility.Visible;

            ExecuteButton.Content = "✓";
            ExecuteButton.ToolTip = "Ausführen";
            ExecuteButton.Foreground = new SolidColorBrush(Color.FromRgb(49, 87, 128));
            ExecuteButton.FontWeight = FontWeights.Normal;
            ExecuteButton.FontSize = 11;
            AutomationProperties.SetName(ExecuteButton, "Ausführen");
        }
    }

    private void ShowHome()
    {
        _screen = "Home";
        _selectedSpoolReport = null;
        _openedFromProcessManager = false;

        WorkTitleRow.Height = new GridLength(0);
        WorkMenuRow.Height = new GridLength(0);
        WorkToolbarRow.Height = new GridLength(0);
        WorkTitleBar.Visibility = Visibility.Collapsed;
        WorkMenuBar.Visibility = Visibility.Collapsed;
        WorkToolbarBar.Visibility = Visibility.Collapsed;
        RightPanel.Visibility = Visibility.Collapsed;
        RightColumn.Width = new GridLength(0);

        StatusModule.Text = string.Empty;
        StatusPage.Text = string.Empty;
        StatusText.Text = "Bereit";

        var home = new Grid { Background = new SolidColorBrush(Color.FromRgb(153, 187, 226)) };
        AutomationId(home, "Home_Workspace");

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 90, 70)
        };
        brand.Children.Add(new TextBlock
        {
            Text = "Wilken",
            Foreground = Brushes.White,
            FontSize = 68,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        brand.Children.Add(new TextBlock
        {
            Text = "  ////",
            Foreground = Brushes.White,
            FontSize = 76,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        home.Children.Add(brand);
        MainContent.Content = home;
    }

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

    private GroupBox Group(string header)
    {
        return new GroupBox
        {
            Header = header,
            Margin = new Thickness(9, 7, 9, 7),
            Padding = new Thickness(10, 8, 8, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(126, 149, 180)),
            Foreground = new SolidColorBrush(Color.FromRgb(43, 72, 121)),
            Background = Brushes.Transparent
        };
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

    private static CheckBox Check(string text, bool value, string id)
    {
        var c = new CheckBox { Content = text, IsChecked = value };
        AutomationId(c, id); return c;
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

    private void ShowListeBase()
    {
        SetChrome(false);
        _screen = "ListeBase";
        ScreenTitle.Text = "Anlagenbuchhaltung - Liste anzeigen";
        ExecuteButton.Content = "";
        StatusModule.Text = "CTPR";
        StatusPage.Text = "1/2";
        SetRightPanel(false, 1);
        ProtocolCount.Text = "▱  Protokolle/Listen";
        var grid = new Grid { Margin = new Thickness(15, 18, 15, 25) };
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        var empty = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(133, 156, 185)), BorderThickness = new Thickness(1) };
        Grid.SetRow(empty, 0); grid.Children.Add(empty);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var all = new Button { Content = "Alle auswählen", Width = 210, Margin = new Thickness(30, 14, 30, 8) }; AutomationId(all, "Spool_SelectAll");
        var none = new Button { Content = "Auswahl aufheben", Width = 210, Margin = new Thickness(30, 14, 30, 8) }; AutomationId(none, "Spool_ClearSelection");
        buttons.Children.Add(all); buttons.Children.Add(none); Grid.SetRow(buttons, 1); grid.Children.Add(buttons);
        MainContent.Content = grid;
    }

    private async Task ShowPrintSelectionAndSpoolAsync()
    {
        if (PrintSelectionOverlay.Visibility == Visibility.Visible)
            return;

        ShowListeBase();
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        ShowPrintSelectionOverlay();
    }

    private void ShowPrintSelectionOverlay()
    {
        if (PrintSelectionHost.Content is not PrintSelectionWindow)
        {
            var panel = new PrintSelectionWindow();
            panel.Started += (_, _) => _ = ContinueFromPrintSelectionAsync();
            panel.Cancelled += (_, _) => HidePrintSelectionOverlay();
            PrintSelectionHost.Content = panel;
        }

        PrintSelectionOverlay.Visibility = Visibility.Visible;
        StatusText.Text = "Druckauswahl";
    }

    private void HidePrintSelectionOverlay()
    {
        if (PrintSelectionOverlay is null) return;
        PrintSelectionOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task ContinueFromPrintSelectionAsync()
    {
        HidePrintSelectionOverlay();
        var profile = _timing.For(_activeReport.Kind);
        StatusText.Text = "Spool wird geladen...";
        await Task.Delay(profile.SpoolLoadMs);
        ShowSpool();
    }

    private void ShowSpool()
    {
        SetChrome(false);
        _screen = "Spool";
        ScreenTitle.Text = "Anlagenbuchhaltung - Liste anzeigen";
        ExecuteButton.Content = "";
        StatusText.Text = "Spool geladen";
        StatusModule.Text = "CTPR";
        StatusPage.Text = "1/2";
        SetRightPanel(false, 1);
        ProtocolCount.Text = "▱  Protokolle/Listen";

        var grid = new Grid { Margin = new Thickness(15, 18, 15, 25) };
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        _spoolGrid = new DataGrid
        {
            ItemsSource = _spool, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
            IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Brushes.White, CanUserAddRows = false
        };
        AutomationId(_spoolGrid, "Spool_Grid");
        AddCol(_spoolGrid, "Gebiet", "Gebiet", 58); AddCol(_spoolGrid, "Ko...", "Konzern", 42); AddCol(_spoolGrid, "M...", "Mandant", 48); AddCol(_spoolGrid, "W...", "Werk", 42);
        AddCol(_spoolGrid, "Erstelldatum", "Erstelldatum", 98); AddCol(_spoolGrid, "Uhrzeit", "Uhrzeit", 82); AddCol(_spoolGrid, "Listenname", "Listenname", 110); AddCol(_spoolGrid, "Erweiterung", "Erweiterung", 90);
        AddCol(_spoolGrid, "Benutzer", "Benutzer", 75); AddCol(_spoolGrid, "Drucker", "Drucker", 75); AddCol(_spoolGrid, "Seite", "Seite", 55); AddCol(_spoolGrid, "Disp...", "Disp", 55); AddCol(_spoolGrid, "Anzahl K...", "Anzahl", 70);
        _spoolGrid.Columns.Add(new DataGridTextColumn { Header = "Listenbezeichnung / Pfad", Binding = new Binding("DisplayDescription"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _spoolGrid.LoadingRow += (_, e) =>
        {
            if (e.Row.Item is not SpoolLine s) return;
            if (s.IsDescriptionLine) e.Row.Foreground = s.Beschreibung.StartsWith("Protokoll") ? Brushes.DarkRed : Brushes.DarkGreen;
            else e.Row.Foreground = new SolidColorBrush(Color.FromRgb(37, 74, 153));
            AutomationProperties.SetAutomationId(e.Row,
                !s.IsDescriptionLine && ReferenceEquals(s, _latestDataMeta)
                    ? "Spool_LatestDataRow"
                    : $"Spool_Row_{s.Listenname}_{s.Erweiterung}_{s.Uhrzeit}");
        };

        var context = new ContextMenu();
        foreach (var h in new[] { "Listenanzeige", "Listendruck", "Ändern", "Druckauftrag löschen", "Auswahl", "Übersicht", "Drucker starten", "Drucker anhalten", "Format", "Filter", "Trenner-Position", "Suchen", "Drucken", "In Zwischenablage kopieren" })
            context.Items.Add(new MenuItem { Header = h });
        var export = new MenuItem { Header = "Export" }; AutomationId(export, "Spool_Context_Export");
        export.Items.Add(new MenuItem { Header = "Exportiere als HTML-Format in Excel" });
        var advanced = new MenuItem { Header = "Erweitert" }; AutomationId(advanced, "Spool_Context_ExportAdvanced"); advanced.Click += (_, _) => OpenAdvancedExport();
        export.Items.Add(advanced); context.Items.Add(export); _spoolGrid.ContextMenu = context;

        Grid.SetRow(_spoolGrid, 0); grid.Children.Add(_spoolGrid);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var all = new Button { Content = "Alle auswählen", Width = 210, Margin = new Thickness(30, 14, 30, 8) }; AutomationId(all, "Spool_SelectAll");
        var none = new Button { Content = "Auswahl aufheben", Width = 210, Margin = new Thickness(30, 14, 30, 8) }; AutomationId(none, "Spool_ClearSelection");
        // UIA-only path for Export → Erweitert. Near-invisible so the recorded spool chrome stays unchanged.
        var openAdvanced = HiddenAutomationButton("Spool_OpenAdvancedExport", "Export → Erweitert", OpenAdvancedExport);
        buttons.Children.Add(all); buttons.Children.Add(none); buttons.Children.Add(openAdvanced); Grid.SetRow(buttons, 1); grid.Children.Add(buttons);
        MainContent.Content = grid;
        _spoolGrid.Loaded += (_, _) => ScrollLatestSpoolRowIntoView();
        ScrollLatestSpoolRowIntoView();
    }

    private void ScrollLatestSpoolRowIntoView()
    {
        if (_spoolGrid is null || _latestDataMeta is null) return;
        _spoolGrid.UpdateLayout();
        _spoolGrid.ScrollIntoView(_latestDataMeta);
        _spoolGrid.SelectedItem = _latestDataMeta;
    }

    private static Button HiddenAutomationButton(string id, string name, Action click)
    {
        var button = new Button
        {
            Width = 16,
            Height = 22,
            Opacity = 0.15,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 14, 0, 8),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = true,
            IsTabStop = false
        };
        AutomationId(button, id);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private static void AddCol(DataGrid grid, string header, string path, double width) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });

    private void OpenAdvancedExport()
    {
        if (_spoolGrid?.SelectedItem is not SpoolLine selected)
        {
            StatusText.Text = "Bitte zuerst eine Spool-Zeile auswählen.";
            return;
        }
        var index = _spool.IndexOf(selected);
        SpoolLine? logical = selected;
        if (selected.IsDescriptionLine && index > 0) logical = _spool[index - 1];
        if (logical?.ReportKind is null)
        {
            StatusText.Text = "Diese Spool-Zeile ist kein exportierbarer Bericht.";
            return;
        }
        _selectedSpoolReport = logical.ReportKind switch
        {
            ReportKind.Zugangsliste => _zugang,
            ReportKind.AnlagenspiegelDetailliert => _anlage,
            _ => _verdichtet
        };
        ShowExportScreen(_selectedSpoolReport);
    }

    private void ShowExportScreen(ReportDefinition report)
    {
        SetChrome(true);
        _screen = "Export";
        ScreenTitle.Text = "System - Gitterbox-Export";
        ExecuteButton.IsEnabled = true;
        StatusText.Text = "Exportparameter auswählen – Export mit grünem Haken in der Symbolleiste starten";
        StatusModule.Text = "CTPR";
        StatusPage.Text = "1/6";
        SetRightPanel(false, 1);

        var grid = new Grid { Margin = new Thickness(70, 38, 70, 40) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(500) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(Row("Beschreibung", ""));
        left.Children.Add(Row("Tabellenname", "CTLP1", "Export_TableName", false));
        left.Children.Add(Section("Format"));
        var formats = new StackPanel();
        formats.Children.Add(Radio("CSV", false, "Export_Format_CSV", "format"));
        formats.Children.Add(Radio("XML", false, "Export_Format_XML", "format"));
        formats.Children.Add(Radio("HTML", false, "Export_Format_HTML", "format"));
        formats.Children.Add(Radio("XLS", true, "Export_Format_XLS", "format"));
        formats.Children.Add(Radio("XLSX", false, "Export_Format_XLSX", "format"));
        left.Children.Add(formats);
        left.Children.Add(Section("Ziel"));
        var target = new StackPanel();
        target.Children.Add(Radio("Browser", false, "Export_Target_Browser", "target"));
        target.Children.Add(Radio("Excel", true, "Export_Target_Excel", "target"));
        target.Children.Add(Radio("Staroffice Calc", false, "Export_Target_Staroffice", "target"));
        target.Children.Add(Radio("Zwischenablage", false, "Export_Target_Clipboard", "target"));
        left.Children.Add(target);
        left.Children.Add(Section("Datensätze"));
        var records = new StackPanel();
        records.Children.Add(Radio("Alle", true, "Export_Records_All", "records"));
        records.Children.Add(Radio("Markierte", false, "Export_Records_Marked", "records"));
        records.Children.Add(Radio("Nicht markierte", false, "Export_Records_Unmarked", "records"));
        records.Children.Add(Radio("Von/bis", false, "Export_Records_Range", "records"));
        var range = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(205, -28, 0, 0) };
        var from = new TextBox { Width = 98, Text = "1" }; AutomationId(from, "Export_Range_From");
        var to = new TextBox { Width = 98, Text = report.RecordCount.ToString() }; AutomationId(to, "Export_Range_To");
        range.Children.Add(from); range.Children.Add(to); records.Children.Add(range); left.Children.Add(records);
        Grid.SetColumn(left, 0); grid.Children.Add(left);

        var total = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(35, 29, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        total.Children.Add(new TextBlock { Text = "Menge gesamt", Width = 205, VerticalAlignment = VerticalAlignment.Center });
        var totalBox = new TextBox { Text = report.RecordCount.ToString(), Width = 100, IsReadOnly = true, Background = new SolidColorBrush(Color.FromRgb(218, 230, 246)) };
        AutomationId(totalBox, "Export_TotalRecords"); total.Children.Add(totalBox); Grid.SetColumn(total, 1); grid.Children.Add(total);
        MainContent.Content = grid;
    }

    private static FrameworkElement Row(string label, string text, string? id = null, bool enabled = true)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        p.Children.Add(new TextBlock { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center });
        var b = new TextBox { Text = text, Width = 165, IsReadOnly = !enabled, Background = enabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(218, 230, 246)) };
        if (id is not null) AutomationId(b, id); p.Children.Add(b); return p;
    }

    private static TextBlock Section(string title) => new()
    {
        Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 27, 0, 6), Foreground = new SolidColorBrush(Color.FromRgb(41, 66, 111))
    };

    private static RadioButton Radio(string text, bool check, string id, string group)
    {
        var r = new RadioButton { Content = text, IsChecked = check, GroupName = group };
        AutomationId(r, id); return r;
    }

    private async Task ExportAsync()
    {
        if (_selectedSpoolReport is null) return;
        var root = MainContent.Content as DependencyObject;
        var xlsx = FindByAutomationId<RadioButton>(root, "Export_Format_XLSX");
        var excel = FindByAutomationId<RadioButton>(root, "Export_Target_Excel");
        var all = FindByAutomationId<RadioButton>(root, "Export_Records_All");
        if (xlsx?.IsChecked != true || excel?.IsChecked != true || all?.IsChecked != true)
        {
            StatusText.Text = "Für den aufgezeichneten Workflow XLSX, Excel und Alle auswählen.";
            return;
        }
        ExecuteButton.IsEnabled = false;
        StatusText.Text = "XLSX wird exportiert...";
        await Task.Delay(_timing.For(_selectedSpoolReport.Kind).ExportMs);
        var path = _xlsx.Export(_selectedSpoolReport);
        ExecuteButton.IsEnabled = true;
        StatusText.Text = $"Export abgeschlossen: {path}";
        // Intentionally no completion dialog: the recordings proceed directly to the browser/download UI.
    }

    private static T? FindByAutomationId<T>(DependencyObject? root, string id) where T : DependencyObject
    {
        if (root is null) return null;
        if (root is T t && AutomationProperties.GetAutomationId(t) == id) return t;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindByAutomationId<T>(VisualTreeHelper.GetChild(root, i), id);
            if (found is not null) return found;
        }
        return null;
    }

    private void ShowProcessManager()
    {
        SetChrome(false, processManager: true);
        _screen = "ProcessManager";
        _openedFromProcessManager = false;
        ScreenTitle.Text = "Anlagenbuchhaltung - Prozesse verwalten";
        ExecuteButton.Content = "";
        ExecuteButton.IsEnabled = false;
        StatusText.Text = "Prozessübersicht";
        StatusModule.Text = "CA18";
        StatusPage.Text = "1/2";
        SetRightPanel(true, 3);

        var rows = new ObservableCollection<ProcessRow>
        {
            new("02","","CAB011","001","Übernahme aus Fibu","AKTIV","OK","CA41","13.07.2021","",""),
            new("02","","CAB013","001","Lebenslauf alle Anlagen","AKTIV","R3","CA43","24.02.2007","",""),
            new("02","","CAB014","001","AfA-Vorschau 1 Jahr","AKTIV","OK","CA44","04.02.2020","",""),
            new("02","","CAB014","002","AfA-Vorschau 3 Jahre","AKTIV","OK","CA44","11.02.2020","",""),
            new("02","","CAB015","621","ANIKO","AKTIV","OK","CA45","30.08.2018","",""),
            new("02","","CAB015","003","Alle Anlagen nach Konten verdichtet","AKTIV","OK","CA45","21.08.2026","",""),
            new("02","","CAB015","123","Anlagenspiegel","AKTIV","","CA45","","",""),
            new("02","","CAB015","TTT","Anlagenspiegel int. Accounting","AKTIV","OK","CA45","22.01.2024","",""),
            new("02","","CAB015","001","Anlagenspiegel nach Anlagen","AKTIV","OK","CA45","19.08.2026","",""),
            new("02","","CAB015","004","Anlagenspiegel nach Anlagen alt","AKTIV","OK","CA45","27.12.2011","",""),
            new("02","","CAB015","002","Anlagenspiegel nach Bilanzpositionen","AKTIV","OK","CA45","06.01.2019","",""),
            new("02","","CAB016","001","Übergabe AfA, Buchungen an Fibu","AKTIV","OK","CA46","13.07.2021","",""),
            new("02","","CAB020","001","Abschluss Anbu-Geschäftsjahr","AKTIV","OK","CA47","01.08.2026","",""),
            new("02","","CAB024","001","Zugangsliste","AKTIV","OK","CA50","19.08.2026","",""),
            new("02","","CAB025","002","Abgangsliste Teilabgänge","AKTIV","OK","CA51","19.08.2026","",""),
            new("02","","CAB025","001","Abgangsliste Vollabgänge","AKTIV","OK","CA51","19.08.2026","",""),
            new("02","","CAB028","001","Buchungsliste","AKTIV","OK","CA53","10.11.2020","",""),
            new("02","","CAB030","001","Massendatenänderung / Fachbereich ST","AKTIV","OK","CA4C","10.12.2012","",""),
            new("02","","CAB034","002","Reporting Anlagenspiegel","AKTIV","","CA4R","","",""),
            new("02","","CAB034","001","Reportingdaten Anlagenspiegel","AKTIV","OK","CA4R","26.03.2014","",""),
            new("02","","CAB037","001","D A T E N E X P O R T","AKTIV","OK","CA41","20.03.2019","",""),
            new("02","","CAB048","001","Jahrestabelle aktualisieren","AKTIV","OK","CA4P","31.01.2023","","")
        };

        var outer = new Grid { Margin = new Thickness(16, 18, 16, 18) };
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        var tabHeader = new Border { Background = new SolidColorBrush(Color.FromRgb(236, 242, 250)), BorderBrush = new SolidColorBrush(Color.FromRgb(130, 151, 178)), BorderThickness = new Thickness(1,1,1,0), Width = 72, HorizontalAlignment = HorizontalAlignment.Left };
        tabHeader.Child = new TextBlock { Text = "Allgemein", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8,4,0,0) };
        Grid.SetRow(tabHeader,0); outer.Children.Add(tabHeader);

        var dg = new DataGrid
        {
            ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true, Background = Brushes.White,
            CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
            EnableRowVirtualization = false, EnableColumnVirtualization = false
        };
        VirtualizingPanel.SetIsVirtualizing(dg, false);
        AutomationId(dg, "ProcessManager_Grid");
        AddProcessCol(dg,"Mandant","Mandant",65); AddProcessCol(dg,"Werk","Werk",50); AddProcessCol(dg,"Programm","Programm",80); AddProcessCol(dg,"Prozess","Prozess",65); AddProcessCol(dg,"Bezeichnung","Bezeichnung",340);
        AddProcessCol(dg,"Status","Status",75); AddProcessCol(dg,"Zustand","Zustand",70); AddProcessCol(dg,"Prozess","ProzessCode",70); AddProcessCol(dg,"Letztes Laufdatum","LetztesLaufdatum",120); AddProcessCol(dg,"Nächstes Laufdatum","NaechstesLaufdatum",130); AddProcessCol(dg,"Rhythmus","Rhythmus",95);
        dg.LoadingRow += (_, e) =>
        {
            if (e.Row.Item is ProcessRow row)
                AutomationProperties.SetAutomationId(e.Row, $"ProcessRow_{row.Programm}_{row.Prozess}");
        };
        dg.MouseDoubleClick += (_, _) => OpenSelectedProcess(dg.SelectedItem as ProcessRow);
        dg.KeyDown += (_, e) => { if (e.Key == Key.Enter) OpenSelectedProcess(dg.SelectedItem as ProcessRow); };
        Grid.SetRow(dg, 1); outer.Children.Add(dg);

        var bottom = Group("Auswahl"); bottom.Margin = new Thickness(0, 15, 0, 0);
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = new StackPanel(); l.Children.Add(Row("Prozess", "", "ProcessManager_Filter")); l.Children.Add(Check("Alle Mandanten anzeigen", false, "ProcessManager_AllClients"));
        var openSelected = new Button { Content = "Öffnen", Width = 90, Height = 22, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationId(openSelected, "ProcessManager_OpenSelected");
        AutomationProperties.SetName(openSelected, "Öffnen");
        openSelected.Click += (_, _) => OpenSelectedProcess(dg.SelectedItem as ProcessRow);
        l.Children.Add(openSelected);
        var r = new StackPanel(); r.Children.Add(Row("Anzeige", "Alle Prozesse")); r.Children.Add(Row("Status ändern", "Keine"));
        Grid.SetColumn(l,0); Grid.SetColumn(r,1); g.Children.Add(l); g.Children.Add(r); bottom.Content = g; Grid.SetRow(bottom,2); outer.Children.Add(bottom);
        MainContent.Content = outer;
    }

    private static void AddProcessCol(DataGrid grid, string header, string path, double width) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });

    private void OpenSelectedProcess(ProcessRow? selected)
    {
        if (selected is null) return;
        _openedFromProcessManager = true;
        if (selected.Bezeichnung == "Alle Anlagen nach Konten verdichtet") ShowAnlagenspiegel(_verdichtet);
        else if (selected.Bezeichnung == "Anlagenspiegel nach Anlagen") ShowAnlagenspiegel(_anlage);
        else if (selected.Bezeichnung == "Zugangsliste") ShowZugangsliste();
        else _openedFromProcessManager = false;
    }

    private void SeedSpool()
    {
        // Historical data-report entries visible in the recordings.
        AddHistorical(ReportKind.AlleAnlagenNachKontenVerdichtet, "20.03.2025", "07:16:46", "4J0402", "003", "Alle Anlagen nach Konten verdichtet   Steuerrecht   Ist   01.2024-12.2024", "/data/wilken/as/wlkp/cs2work/spool/CT224401.SPL", 2);
        AddHistorical(ReportKind.AlleAnlagenNachKontenVerdichtet, "14.08.2026", "14:17:16", "4J0402", "003", "Alle Anlagen nach Konten verdichtet   Steuerrecht   Ist   01.2021-12.2021", "/data/wilken/as/wlkp/cs2work/spool/CT224420.SPL", 2);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "14.08.2026", "14:30:05", "4J0402", "001", "Anlagenspiegel nach Anlagen   Steuerrecht   Ist   01.1975-12.2021", "/data/wilken/as/wlkp/cs2work/spool/CT224421.SPL", 1537);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "19.08.2026", "14:58:12", "4J0402", "001", "Anlagenspiegel nach Anlagen   Steuerrecht   Ist   01.2021-12.2021", "/data/wilken/as/wlkp/cs2work/spool/CT224440.SPL", 602);
        AddHistorical(ReportKind.AlleAnlagenNachKontenVerdichtet, "21.08.2026", "13:51:53", "4J0402", "003", "Alle Anlagen nach Konten verdichtet   Steuerrecht   Ist   01.2021-12.2021", "/data/wilken/as/wlkp/cs2work/spool/CT224474.SPL", 2);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "21.08.2026", "15:05:44", "4J0402", "001", "Anlagenspiegel nach Anlagen   Steuerrecht   Ist   01.2021-12.2021", "/data/wilken/as/wlkp/cs2work/spool/CT224480.SPL", 602);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "21.08.2026", "15:22:55", "4J0402", "001", "Anlagenspiegel nach Anlagen   Steuerrecht   Ist   01.2021-12.2021", "/data/wilken/as/wlkp/cs2work/spool/CT224482.SPL", 602);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "21.08.2026", "15:29:26", "4J0402", "001", "Anlagenspiegel nach Anlagen   Steuerrecht   Ist   01.2021-12.2021", "/data/wilken/as/wlkp/cs2work/spool/CT224484.SPL", 602);
        AddHistorical(ReportKind.Zugangsliste, "19.08.2026", "15:34:03", "5J0102", "001", "Zugangsliste   Handelsrecht   Ist   01.2020-12.2020", "/data/wilken/as/wlkp/cs2work/spool/CT224451.SPL", 6);

        // Protocol entries. The recordings right-click the newest matching PRT row.
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "20.03.2025", "07:16:25", "B015", "PRT", "Protokoll: Anlagenspiegel", "/data/wilken/as/wlkp/cs2work/spool/CT224400.SPL", 2);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "19.08.2026", "14:56:04", "B015", "PRT", "Protokoll: Anlagenspiegel", "/data/wilken/as/wlkp/cs2work/spool/CT224439.SPL", 2);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "19.08.2026", "15:07:40", "B015", "PRT", "Protokoll: Anlagenspiegel", "/data/wilken/as/wlkp/cs2work/spool/CT224443.SPL", 2);
        AddHistorical(ReportKind.AnlagenspiegelDetailliert, "21.08.2026", "15:29:05", "B015", "PRT", "Protokoll: Anlagenspiegel", "/data/wilken/as/wlkp/cs2work/spool/CT224483.SPL", 2);
        AddHistorical(ReportKind.Zugangsliste, "19.08.2026", "15:33:43", "B024", "PRT", "Protokoll: Zugangsliste", "/data/wilken/as/wlkp/cs2work/spool/CT224450.SPL", 2);
        AddHistorical(ReportKind.Zugangsliste, "21.08.2026", "15:34:03", "B024", "PRT", "Protokoll: Zugangsliste", "/data/wilken/as/wlkp/cs2work/spool/CT224486.SPL", 2);
        SortSpoolRows();
    }

    private SpoolLine AddHistorical(ReportKind kind, string date, string time, string list, string ext, string description, string path, int page)
    {
        var meta = new SpoolLine
        {
            Gebiet = "CSA", Konzern = "1", Mandant = "02", Erstelldatum = date, Uhrzeit = time,
            Listenname = list, Erweiterung = ext, Benutzer = "BHL", Drucker = "LOCW", Seite = page.ToString(),
            Disp = "K", Anzahl = "1", ReportKind = kind
        };
        _spool.Add(meta);
        _spool.Add(new SpoolLine
        {
            Gebiet = "STOP", Beschreibung = description, Pfad = path, IsDescriptionLine = true, ReportKind = kind
        });
        return meta;
    }

    private void AddGeneratedSpool(ReportDefinition report)
    {
        // A successful report run creates two logical outputs in the recordings:
        // the PRT protocol and the actual data report. This is why the sidebar count rises by 2.
        // Generated rows must be dated "now" so the worker can correlate them to runStartedAt.
        // Historical seed rows keep the recording dates (21.08.2026 and earlier).
        var now = DateTime.Now;
        var date = now.ToString("dd.MM.yyyy");
        var stamp = now.ToString("HHmmss");

        var protocolList = report.Kind == ReportKind.Zugangsliste ? "B024" : "B015";
        var protocolDescription = report.Kind == ReportKind.Zugangsliste ? "Protokoll: Zugangsliste" : "Protokoll: Anlagenspiegel";
        AddHistorical(report.Kind, date, now.ToString("HH:mm:ss"), protocolList, "PRT", protocolDescription,
            $"/data/wilken/as/wlkp/cs2work/spool/CT{stamp}P.SPL", 2);

        var dataList = report.Kind == ReportKind.Zugangsliste ? "5J0102" : "4J0402";
        var dataExtension = report.Kind == ReportKind.Zugangsliste ? "001" : report.Extension;
        var dataPage = report.Kind == ReportKind.Zugangsliste ? 6 : report.Kind == ReportKind.AnlagenspiegelDetailliert ? 602 : 2;
        var dataDescription = $"{report.LogicalDescription}   {report.Fachbereich}   {report.Wertart}   {report.PeriodFrom}-{report.PeriodTo}";
        _latestDataMeta = AddHistorical(report.Kind, date, now.AddSeconds(20).ToString("HH:mm:ss"), dataList, dataExtension, dataDescription,
            $"/data/wilken/as/wlkp/cs2work/spool/CT{stamp}D.SPL", dataPage);
        SortSpoolRows();
    }

    private void SortSpoolRows()
    {
        var pairs = new List<(SpoolLine Meta, SpoolLine Stop)>();
        for (var i = 0; i + 1 < _spool.Count; i += 2)
        {
            pairs.Add((_spool[i], _spool[i + 1]));
        }

        var ordered = pairs
            .OrderBy(x => x.Meta.Listenname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => ParseSpoolDateTime(x.Meta.Erstelldatum, x.Meta.Uhrzeit))
            .ToList();
        _spool.Clear();
        foreach (var pair in ordered)
        {
            _spool.Add(pair.Meta);
            _spool.Add(pair.Stop);
        }
    }

    private static DateTime ParseSpoolDateTime(string date, string time)
    {
        return DateTime.TryParseExact($"{date} {time}", "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value) ? value : DateTime.MinValue;
    }
}
