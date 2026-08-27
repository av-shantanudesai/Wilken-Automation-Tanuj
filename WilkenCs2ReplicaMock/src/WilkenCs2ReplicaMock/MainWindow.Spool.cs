// Liste anzeigen / Druckauswahl overlay, spool grid with context menu, and spool row seeding/sorting.
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using WilkenCs2ReplicaMock.Models;

namespace WilkenCs2ReplicaMock;

public partial class MainWindow
{
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
        AutomationId(context, "Spool_ContextMenu");
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
        // Opens the real spool context menu (last item Export → last submenu Erweitert).
        // Does not skip straight to Gitterbox — that is not the recorded Wilken path.
        var openMenu = HiddenAutomationButton("Spool_OpenContextMenu", "Kontextmenü öffnen", OpenSpoolContextMenu);
        buttons.Children.Add(all); buttons.Children.Add(none); buttons.Children.Add(openMenu); Grid.SetRow(buttons, 1); grid.Children.Add(buttons);
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

    private static void AddCol(DataGrid grid, string header, string path, double width) =>
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });

    private void OpenSpoolContextMenu()
    {
        if (_spoolGrid?.ContextMenu is null) return;
        if (_spoolGrid.SelectedItem is null && _latestDataMeta is not null)
            _spoolGrid.SelectedItem = _latestDataMeta;
        var menu = _spoolGrid.ContextMenu;
        menu.PlacementTarget = _spoolGrid;
        menu.Placement = PlacementMode.Right;
        menu.IsOpen = true;
    }

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
