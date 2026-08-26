using System.IO;
using System.Text.Json;
using WilkenCs2ReplicaMock.Models;

namespace WilkenCs2ReplicaMock.Services;

public sealed class TimingService
{
    private readonly Dictionary<ReportKind, TimingProfile> _profiles;

    public TimingService()
    {
        // These are the application-owned wait states visible in the recordings.
        // The full video duration also includes manual navigation and clicks.
        _profiles = new()
        {
            [ReportKind.Zugangsliste] = new(2400, 0, 900, 1800, 38),
            [ReportKind.AnlagenspiegelDetailliert] = new(20500, 6500, 1400, 2200, 66),
            [ReportKind.AlleAnlagenNachKontenVerdichtet] = new(20500, 6500, 1400, 2200, 65)
        };

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement.GetProperty("timing");
            Load(root, "Zugangsliste", ReportKind.Zugangsliste);
            Load(root, "AnlagenspiegelDetailliert", ReportKind.AnlagenspiegelDetailliert);
            Load(root, "AlleAnlagenNachKontenVerdichtet", ReportKind.AlleAnlagenNachKontenVerdichtet);
        }
        catch
        {
            // Keep defaults when the local config is incomplete.
        }
    }

    private void Load(JsonElement root, string key, ReportKind kind)
    {
        var e = root.GetProperty(key);
        _profiles[kind] = new TimingProfile(
            e.GetProperty("generationStage1Ms").GetInt32(),
            e.GetProperty("generationStage2Ms").GetInt32(),
            e.GetProperty("spoolLoadMs").GetInt32(),
            e.GetProperty("exportMs").GetInt32(),
            e.GetProperty("expectedApproxSeconds").GetInt32());
    }

    public TimingProfile For(ReportKind kind) => _profiles[kind];
}
