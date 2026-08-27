using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
}
