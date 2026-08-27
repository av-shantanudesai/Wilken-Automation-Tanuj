// Prozesse verwalten screen: seeded process grid and opening a selected process.
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WilkenCs2ReplicaMock.Models;

namespace WilkenCs2ReplicaMock;

public partial class MainWindow
{
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
}
