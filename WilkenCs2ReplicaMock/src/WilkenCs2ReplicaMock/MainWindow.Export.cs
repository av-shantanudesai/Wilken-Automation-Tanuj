// Gitterbox-Export screen and the XLSX export flow.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WilkenCs2ReplicaMock.Models;

namespace WilkenCs2ReplicaMock;

public partial class MainWindow
{
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
        try
        {
            await Task.Delay(_timing.For(_selectedSpoolReport.Kind).ExportMs);
            var path = _xlsx.Export(_selectedSpoolReport);
            StatusText.Text = $"Export abgeschlossen: {path}";
            // Intentionally no completion dialog: the recordings proceed directly to the browser/download UI.
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Disk/permission failures must not crash the mock's UI thread.
            StatusText.Text = $"Export fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            ExecuteButton.IsEnabled = true;
        }
    }
}
