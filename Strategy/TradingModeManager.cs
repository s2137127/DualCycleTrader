using System.Text.Json;
using System.Text.Json.Serialization;
using DualCycleTrader.Data;
using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public enum TradingMode { Initializing, BullishAB, BearRangeC, BearTrendD, TransitionE }

public sealed record MarketState
{
    public TradingMode ConfirmedTradingMode { get; init; } = TradingMode.Initializing;
    public MarketMode CandidateMarketRegime { get; init; } = MarketMode.Unknown;
    public int CandidateRegimeDays { get; init; }
    public DateTime? LastEvaluationDate { get; init; }
    public bool DisableNewLongEntries => ConfirmedTradingMode == TradingMode.BearTrendD;
}

public sealed class TradingModeManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _path;
    private readonly StrategySettings _settings;

    public TradingModeManager(StrategySettings settings, string? path = null)
    {
        _settings = settings;
        _path = path ?? AppDataPaths.MarketState;
    }

    public MarketState Load()
    {
        if (!_settings.PersistMarketState || !File.Exists(_path)) return new();
        try { return JsonSerializer.Deserialize<MarketState>(File.ReadAllText(_path), JsonOptions) ?? new(); }
        catch { return new(); }
    }

    public MarketState Evaluate(IReadOnlyList<Candle> market)
    {
        var state = Load();
        // Only closed daily candles advance confirmation. Today's live regime can
        // still be shown separately by MarketClassifier.
        var closed = market.Where(c => c.Time.Date < DateTime.Today ||
            (c.Time.Date == DateTime.Today && DateTime.Now.TimeOfDay >= new TimeSpan(13, 35, 0)))
            .OrderBy(c => c.Time).GroupBy(c => c.Time.Date).Select(g => g.Last()).ToArray();
        int firstCount = state.LastEvaluationDate is null
            ? Math.Max(130, closed.Length - Math.Max(1, _settings.MarketRegimeConfirmationDays) + 1)
            : 130;
        for (int count = firstCount; count <= closed.Length; count++)
        {
            var date = closed[count - 1].Time.Date;
            if (state.LastEvaluationDate >= date) continue;
            var regime = MarketClassifier.Analyze(closed.AsSpan(0, count).ToArray(), _settings).Mode;
            state = Advance(state, regime, date, _settings.MarketRegimeConfirmationDays);
        }
        if (_settings.PersistMarketState && state.LastEvaluationDate != Load().LastEvaluationDate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));
        }
        return state;
    }

    // Replays every available completed trading day up to the requested date.
    // Historical analysis never loads or writes the live market-state file.
    public MarketState EvaluateHistorical(IReadOnlyList<Candle> market, DateTime asOfDate)
    {
        var closed = market.Where(c => c.Time.Date <= asOfDate.Date)
            .OrderBy(c => c.Time).GroupBy(c => c.Time.Date).Select(g => g.Last()).ToArray();
        var state = new MarketState();
        for (int count = 130; count <= closed.Length; count++)
        {
            var date = closed[count - 1].Time.Date;
            var regime = MarketClassifier.Analyze(closed.AsSpan(0, count).ToArray(), _settings).Mode;
            state = Advance(state, regime, date, _settings.MarketRegimeConfirmationDays);
        }
        return state;
    }

    public static TradingMode Map(MarketMode regime) => regime switch
    {
        MarketMode.A_BullTrend or MarketMode.B_BullRange => TradingMode.BullishAB,
        MarketMode.C_BearRange => TradingMode.BearRangeC,
        MarketMode.D_BearTrend => TradingMode.BearTrendD,
        _ => TradingMode.TransitionE
    };

    public static MarketState Advance(MarketState state, MarketMode candidate, DateTime date, int requiredDays)
    {
        if (state.LastEvaluationDate >= date) return state;
        int days = state.CandidateMarketRegime == candidate ? state.CandidateRegimeDays + 1 : 1;
        var confirmed = state.ConfirmedTradingMode;
        if (days >= Math.Max(1, requiredDays)) confirmed = Map(candidate);
        return state with { ConfirmedTradingMode = confirmed, CandidateMarketRegime = candidate,
            CandidateRegimeDays = days, LastEvaluationDate = date };
    }
}
