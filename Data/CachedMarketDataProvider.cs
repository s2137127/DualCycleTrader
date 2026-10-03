using System.Text.Json;
using DualCycleTrader.Models;

namespace DualCycleTrader.Data;

public sealed class CachedMarketDataProvider : IMarketDataProvider
{
    private readonly IMarketDataProvider _remote;
    private readonly StrategySettings _settings;
    private readonly string _cacheDir;
    private readonly string _lastUpdatePath;

    public CachedMarketDataProvider(IMarketDataProvider remote, StrategySettings settings, string? cacheDir = null)
    {
        _remote = remote;
        _settings = settings;
        _cacheDir = cacheDir ?? AppDataPaths.MarketCache;
        _lastUpdatePath = Path.Combine(_cacheDir, "last-update.txt");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<List<Candle>> GetDailyAsync(string symbol, int days = 260)
    {
        var cached = Load(symbol, "D");
        if (HasToday(cached)) return cached;

        // First use: seed enough history. Later: only request a short recent window,
        // merge it into the local cache and avoid redownloading hundreds of bars.
        int requestDays = cached.Count == 0
            ? Math.Max(days, Math.Min(_settings.DailyRetentionBars, 500))
            : 10;

        var fresh = await _remote.GetDailyAsync(symbol, requestDays);
        var merged = Merge(cached, fresh);

        if (_settings.AutoCleanupCache && merged.Count > _settings.DailyRetentionBars)
            merged = merged.TakeLast(_settings.DailyRetentionBars).ToList();

        Save(symbol, "D", merged);
        return merged;
    }

    // Ensure the oldest selectable historical date has at least 130 prior daily
    // bars for MarketClassifier and StockScanner. Existing adequate caches still
    // use the normal short daily refresh.
    public async Task<List<Candle>> GetDailyForHistoryAsync(string symbol, DateTime earliestDate)
    {
        var cached = Load(symbol, "D");
        bool hasHistory = cached.Count(c => c.Time.Date <= earliestDate.Date) >= 130;
        if (hasHistory && HasToday(cached)) return cached;

        int requestDays = hasHistory ? 10 : Math.Max(300,
            (int)Math.Ceiling((DateTime.Today - earliestDate.Date).TotalDays) + 220);
        var fresh = await _remote.GetDailyAsync(symbol, requestDays);
        var merged = Merge(cached, fresh);
        if (_settings.AutoCleanupCache)
        {
            int requiredBars = merged.Count(c => c.Time.Date >= earliestDate.Date) + 130;
            int retention = Math.Max(_settings.DailyRetentionBars, requiredBars);
            if (merged.Count > retention) merged = merged.TakeLast(retention).ToList();
        }
        Save(symbol, "D", merged);
        return merged;
    }

    public async Task<List<Candle>> Get60MinuteAsync(string symbol, int days = 30)
    {
        var cached = Load(symbol, "60");
        bool needsBackfill = cached.Count == 0 || cached[0].Time.Date > DateTime.Today.AddDays(-59);
        if (HasToday(cached) && !needsBackfill) return cached;

        int requestDays = needsBackfill ? 60 : 5;
        var fresh = await _remote.Get60MinuteAsync(symbol, requestDays);
        var merged = Merge(cached, fresh);

        if (_settings.AutoCleanupCache)
        {
            DateTime cutoff = DateTime.Today.AddDays(-Math.Max(190, _settings.IntradayRetentionDays));
            merged = merged.Where(x => x.Time >= cutoff).ToList();
        }

        Save(symbol, "60", merged);
        return merged;
    }

    public async Task<List<Candle>> Get60MinuteForHistoryAsync(string symbol, DateTime earliestDate,
        bool forceRefresh = false)
    {
        DateTime requiredStart = earliestDate.Date.AddDays(-30);
        var cached = Load(symbol, "60");
        if (!forceRefresh && cached.Count > 0 && cached[0].Time.Date <= requiredStart && HasToday(cached))
            return cached;

        int requestDays = Math.Min(730, Math.Max(60,
            (int)Math.Ceiling((DateTime.Today - requiredStart).TotalDays) + 1));
        var fresh = await _remote.Get60MinuteAsync(symbol, requestDays);
        var merged = Merge(cached, fresh);
        if (_settings.AutoCleanupCache)
            merged = merged.Where(c => c.Time.Date >= requiredStart).ToList();
        Save(symbol, "60", merged);
        return merged;
    }

    public List<Candle> GetCachedDaily(string symbol) => Load(symbol, "D");

    public List<Candle> GetCached60Minute(string symbol) => Load(symbol, "60");

    public void MarkDataUpdated()
        => File.WriteAllText(_lastUpdatePath, DateTime.Now.ToString("O"));

    public DateTime? GetLastDataUpdateTime()
    {
        try
        {
            return File.Exists(_lastUpdatePath) &&
                   DateTime.TryParse(File.ReadAllText(_lastUpdatePath), out var value)
                ? value
                : null;
        }
        catch { return null; }
    }

    public (long bytes, int files) GetCacheStats()
    {
        var files = Directory.EnumerateFiles(_cacheDir, "*.json").ToArray();
        return (files.Sum(f => new FileInfo(f).Length), files.Length);
    }

    public void ClearCache()
    {
        foreach (var f in Directory.EnumerateFiles(_cacheDir, "*.json"))
            File.Delete(f);
        if (File.Exists(_lastUpdatePath)) File.Delete(_lastUpdatePath);
    }

    private List<Candle> Load(string symbol, string interval)
    {
        string path = PathFor(symbol, interval);
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<List<Candle>>(File.ReadAllText(path)) ?? new();
        }
        catch { return new(); }
    }

    private void Save(string symbol, string interval, List<Candle> candles)
    {
        File.WriteAllText(PathFor(symbol, interval), JsonSerializer.Serialize(candles));
    }

    private string PathFor(string symbol, string interval)
    {
        string safe = string.Concat(symbol.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(_cacheDir, $"{safe}_{interval}.json");
    }

    private static List<Candle> Merge(IEnumerable<Candle> oldBars, IEnumerable<Candle> newBars)
        => oldBars.Concat(newBars)
                  .GroupBy(x => x.Time)
                  .Select(g => g.Last())
                  .OrderBy(x => x.Time)
                  .ToList();

    private static bool HasToday(IReadOnlyList<Candle> candles)
        => candles.Count > 0 && candles[^1].Time.Date >= DateTime.Today;
}
