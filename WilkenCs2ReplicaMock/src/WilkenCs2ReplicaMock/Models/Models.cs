namespace WilkenCs2ReplicaMock.Models;

public enum ReportKind
{
    Zugangsliste,
    AnlagenspiegelDetailliert,
    AlleAnlagenNachKontenVerdichtet
}

public sealed record ReportDefinition(
    ReportKind Kind,
    string Process,
    string Description,
    string Fachbereich,
    string Wertart,
    string PeriodFrom,
    string PeriodTo,
    int RecordCount,
    string Program,
    string ListName,
    string Extension,
    string LogicalDescription);

public sealed class SpoolLine
{
    public string Gebiet { get; set; } = "";
    public string Konzern { get; set; } = "";
    public string Mandant { get; set; } = "";
    public string Werk { get; set; } = "";
    public string Erstelldatum { get; set; } = "";
    public string Uhrzeit { get; set; } = "";
    public string Listenname { get; set; } = "";
    public string Erweiterung { get; set; } = "";
    public string Benutzer { get; set; } = "";
    public string Drucker { get; set; } = "";
    public string Seite { get; set; } = "";
    public string Disp { get; set; } = "";
    public string Anzahl { get; set; } = "";
    public string Beschreibung { get; set; } = "";
    public string Pfad { get; set; } = "";
    public string DisplayDescription => string.IsNullOrWhiteSpace(Beschreibung) ? "" : $"{Beschreibung}    {Pfad}";
    public ReportKind? ReportKind { get; set; }
    public bool IsDescriptionLine { get; set; }
}

public sealed record TimingProfile(int GenerationStage1Ms, int GenerationStage2Ms, int SpoolLoadMs, int ExportMs, int ExpectedApproxSeconds);

public sealed record ProcessRow(
    string Mandant,
    string Werk,
    string Programm,
    string Prozess,
    string Bezeichnung,
    string Status,
    string Zustand,
    string ProzessCode,
    string LetztesLaufdatum,
    string NaechstesLaufdatum,
    string Rhythmus);
