using System.Text.Json;
using DualCycleTrader.Models;

namespace DualCycleTrader.Data;

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore()
    {
        _path = AppDataPaths.Settings;
    }

    public StrategySettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new StrategySettings();
            return JsonSerializer.Deserialize<StrategySettings>(File.ReadAllText(_path))
                   ?? new StrategySettings();
        }
        catch { return new StrategySettings(); }
    }

    public void Save(StrategySettings settings)
    {
        File.WriteAllText(_path,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
