using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WilkenAutomation.Application.Validators;

namespace WilkenAutomation.TestDesktop;

/// <summary>
/// Dummy Wilken-like desktop used only to test FlaUI automation.
/// Login: tester / tester
/// Empty period: client 002 + Steuerrecht (SUCCESS_EMPTY).
/// After Execute, a spool report is generated and an informational popup is shown
/// so the worker can prove it dismisses unexpected dialogs and still exports.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _reportTimer;
    private readonly DispatcherTimer _noticeTimer;
    private int _lastRecordCount;
    private string _lastExpectedName = "";

    public MainWindow()
    {
        InitializeComponent();

        for (var i = 1; i <= 10; i++)
            ClientCombo.Items.Add(i.ToString("D3"));

        DepartmentCombo.Items.Add("Handelsrecht");
        DepartmentCombo.Items.Add("Steuerrecht");

        ClientCombo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => SyncComboSelection(ClientCombo)));
        DepartmentCombo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => SyncComboSelection(DepartmentCombo)));

        _reportTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _reportTimer.Tick += (_, _) => CompleteReport();

        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _noticeTimer.Tick += (_, _) => ShowNotice();
    }

    /// <summary>
    /// UIA SetValue/Invoke tries to focus controls. If the user is working in
    /// another app (this window is not active), refuse keyboard focus so typing
    /// is not interrupted. When the user restores this window to watch, it is
    /// active and focus is allowed.
    /// </summary>
    protected override void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (!IsActive || WindowState == WindowState.Minimized)
        {
            e.Handled = true;
            return;
        }

        base.OnPreviewGotKeyboardFocus(e);
    }

    private Panel RootPanel => (Panel)Content;

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var user = LoginUsernameBox.Text.Trim();
        var password = LoginPasswordBox.Text;
        if (user != "tester" || password != "tester")
        {
            LoginError.Text = "Invalid credentials. Use tester / tester.";
            return;
        }

        RootPanel.Children.Remove(LoginPanel);
        MainWorkspace.Visibility = Visibility.Visible;
        FooterStatus.Text = "Logged in. Open Asset Accounting after selecting a client.";
    }

    private void OpenAssetAccounting_Click(object sender, RoutedEventArgs e)
    {
        AssetAccountingPanel.Visibility = Visibility.Visible;
        FooterStatus.Text = "Asset Accounting open. Set fiscal year and department.";
    }

    private static void SyncComboSelection(ComboBox combo)
    {
        var text = combo.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;
        foreach (var item in combo.Items)
        {
            if (string.Equals(item?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                if (!Equals(combo.SelectedItem, item))
                    combo.SelectedItem = item;
                return;
            }
        }
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        SyncComboSelection(ClientCombo);
        SyncComboSelection(DepartmentCombo);
        if (ClientCombo.SelectedItem is null || string.IsNullOrWhiteSpace(FiscalYearBox.Text) || DepartmentCombo.SelectedItem is null)
        {
            SetReportStatus("Error: filters incomplete");
            return;
        }

        SetReportStatus("Processing");
        ExecuteButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        SpoolList.Items.Clear();
        ReportPreviewBox.Text = "Evaluation running...";
        ReportSummaryLabel.Text = "Generating report...";
        ExpectedFileLabel.Text = BuildExpectedArchiveName();
        FooterStatus.Text = "Evaluation running...";
        _reportTimer.Stop();
        _noticeTimer.Stop();
        _noticeTimer.Start();
        _reportTimer.Start();
    }

    private void ShowNotice()
    {
        _noticeTimer.Stop();
        NoticePanel.Visibility = Visibility.Visible;
        FooterStatus.Text = "Unexpected notice is open. Automation should click OK.";
    }

    private void DismissNotice_Click(object sender, RoutedEventArgs e) => HideNotice();

    private void HideNotice()
    {
        NoticePanel.Visibility = Visibility.Collapsed;
        if (ReportStatusLabel.Text == "Ready")
            FooterStatus.Text = "Report ready. Open spool, then export.";
    }

    private void CompleteReport()
    {
        _reportTimer.Stop();
        ExecuteButton.IsEnabled = true;

        var client = ClientText();
        var year = FiscalYearBox.Text.Trim();
        var department = DepartmentText();
        _lastRecordCount = IsEmptyPeriod(client, department)
            ? 0
            : 12 + Math.Abs((client + year + department).GetHashCode() % 40);
        _lastExpectedName = BuildExpectedArchiveName();

        var csv = BuildCsv(client, year, department, _lastRecordCount);
        var spoolItem = $"Asset Accounting | Client {client} | {year} | {department} | {_lastRecordCount} records";

        SpoolList.Items.Clear();
        SpoolList.Items.Add(spoolItem);
        SpoolList.SelectedIndex = 0;
        ExportButton.IsEnabled = true;

        ExpectedFileLabel.Text = _lastExpectedName;
        ReportSummaryLabel.Text = _lastRecordCount == 0
            ? $"Empty period (SUCCESS_EMPTY). Archive as {_lastExpectedName}"
            : $"{_lastRecordCount} asset records ready. Archive as {_lastExpectedName}";
        ReportPreviewBox.Text = csv;
        SetReportStatus("Ready");
        FooterStatus.Text = NoticePanel.Visibility == Visibility.Visible
            ? "Report ready, but a notice is still open."
            : "Report ready. Open spool, then export.";
    }

    private void OpenSpool_Click(object sender, RoutedEventArgs e)
    {
        if (ReportStatusLabel.Text != "Ready" || SpoolList.Items.Count == 0)
        {
            FooterStatus.Text = "No report in spool yet. Execute evaluation first.";
            return;
        }

        if (SpoolList.SelectedIndex < 0)
            SpoolList.SelectedIndex = 0;
        ExportButton.IsEnabled = true;
        FooterStatus.Text = "Spool open. Export the selected report.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SpoolList.SelectedItem is null) return;
        var suggested = Path.Combine(Path.GetTempPath(), "WilkenAutomationExports",
            $"test-{ClientText()}-{FiscalYearBox.Text}-{DepartmentText()}.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(suggested)!);
        SaveFileNameBox.Text = suggested;
        SaveHintLabel.Text = $"After Save, worker archives the original as {_lastExpectedName}";
        SaveDialogPanel.Visibility = Visibility.Visible;
    }

    private void CancelSave_Click(object sender, RoutedEventArgs e) => HideSaveDialog();

    private void ConfirmSave_Click(object sender, RoutedEventArgs e)
    {
        var path = SaveFileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path)) return;

        var client = ClientText();
        var year = FiscalYearBox.Text.Trim();
        var department = DepartmentText();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, BuildCsv(client, year, department, _lastRecordCount), Encoding.UTF8);

        HideSaveDialog();
        FooterStatus.Text = $"Exported {_lastRecordCount} records. Expected archive: {_lastExpectedName}";
    }

    private void HideSaveDialog() => SaveDialogPanel.Visibility = Visibility.Collapsed;

    private void SetReportStatus(string status)
    {
        ReportStatusLabel.Text = status;
        AutomationProperties.SetName(ReportStatusLabel, status);
    }

    private string ClientText()
    {
        SyncComboSelection(ClientCombo);
        return ClientCombo.SelectedItem?.ToString() ?? ClientCombo.Text.Trim();
    }

    private string DepartmentText()
    {
        SyncComboSelection(DepartmentCombo);
        return DepartmentCombo.SelectedItem?.ToString() ?? DepartmentCombo.Text.Trim();
    }

    private string BuildExpectedArchiveName()
    {
        var client = ClientText();
        var year = FiscalYearBox.Text.Trim();
        var department = DepartmentText();
        return $"Exports/Mandant_{client}/{year}/Mandant_{client}_{year}_{department}.csv";
    }

    private static bool IsEmptyPeriod(string client, string department) =>
        client == "002" && department == "Steuerrecht";

    private static string BuildCsv(string client, string year, string department, int recordCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{ExportFileValidator.ReportMarker} V2.4");
        sb.AppendLine($"Client;{client}");
        sb.AppendLine($"FiscalYear;{year}");
        sb.AppendLine($"Department;{department}");
        sb.AppendLine($"RecordCount;{recordCount}");
        sb.AppendLine($"GeneratedAt;{DateTime.UtcNow:O}");
        sb.AppendLine(ExportFileValidator.DataMarker);
        sb.AppendLine("AssetNo;Description;AcquisitionDate;AcquisitionValue;Depreciation;BookValue");
        for (var i = 1; i <= recordCount; i++)
        {
            var value = 1000 + i * 25;
            var dep = i * 10;
            sb.AppendLine($"A{i:D5};Asset {i} ({department});{year}-01-15;{value};{dep};{value - dep}");
        }
        sb.AppendLine(ExportFileValidator.EndMarker);
        return sb.ToString();
    }
}
