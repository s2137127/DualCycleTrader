using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DualCycleTrader.Models;
using DualCycleTrader.Strategy;

namespace DualCycleTrader.Data;

public sealed record HistoricalSignalArchive
{
    public int SchemaVersion { get; init; } = 1;
    public string Fingerprint { get; init; } = "";
    public List<DateTime> AnalyzedDates { get; init; } = new();
    public List<DateTime> CompletedDates { get; init; } = new();
    public List<HistoricalSignal> Signals { get; init; } = new();
}

public sealed class HistoricalSignalStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _path;

    public HistoricalSignalStore(string? path = null)
        => _path = path ?? AppDataPaths.HistoricalSignals;

    public HistoricalSignalArchive Load(StrategySettings settings, IEnumerable<string> symbols)
    {
        string fingerprint = Fingerprint(settings);
        string legacyFingerprint = LegacyFingerprint(settings, symbols);
        try
        {
            if (File.Exists(_path))
            {
                var archive = JsonSerializer.Deserialize<HistoricalSignalArchive>(File.ReadAllText(_path), JsonOptions);
                if (archive is not null && archive.SchemaVersion == CurrentSchemaVersion &&
                    (archive.Fingerprint == fingerprint || archive.Fingerprint == legacyFingerprint))
                    return archive with
                    {
                        Fingerprint = fingerprint,
                        AnalyzedDates = archive.AnalyzedDates.Concat(archive.CompletedDates)
                            .Concat(archive.Signals.Select(s => s.TriggerTime.Date))
                            .Select(d => d.Date).Distinct().OrderBy(d => d).ToList()
                    };
            }
        }
        catch { /* A damaged cache is rebuilt from market data. */ }
        return new HistoricalSignalArchive { Fingerprint = fingerprint };
    }

    public HistoricalSignalArchive Merge(HistoricalSignalArchive archive,
        IEnumerable<DateTime> analyzedDates, IEnumerable<DateTime> completedDates,
        IEnumerable<HistoricalSignal> signals)
    {
        var analyzed = analyzedDates.Select(d => d.Date).ToHashSet();
        var complete = completedDates.Select(d => d.Date).ToHashSet();
        return archive with
        {
            AnalyzedDates = archive.AnalyzedDates.Concat(archive.CompletedDates)
                .Select(d => d.Date).Concat(analyzed).Distinct().OrderBy(d => d).ToList(),
            CompletedDates = archive.CompletedDates.Where(d => !analyzed.Contains(d.Date))
                .Select(d => d.Date).Concat(complete).Distinct().OrderBy(d => d).ToList(),
            Signals = archive.Signals.Where(s => !analyzed.Contains(s.TriggerTime.Date))
                .Concat(signals).OrderByDescending(s => s.TriggerTime).ThenBy(s => s.Symbol).ToList()
        };
    }

    public void Save(HistoricalSignalArchive archive)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(archive, JsonOptions));
        File.Move(temporary, _path, true);
    }

    public static string Fingerprint(StrategySettings settings)
    {
        string input = "historical-entry-v2-original-a-b-ma20-ma50\n" + JsonSerializer.Serialize(settings);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private static string LegacyFingerprint(StrategySettings settings, IEnumerable<string> symbols)
    {
        string input = "historical-entry-v2-original-a-b-ma20-ma50\n" + JsonSerializer.Serialize(settings) + "\n" +
            string.Join("\n", symbols.OrderBy(s => s, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
