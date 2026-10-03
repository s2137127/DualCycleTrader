using DualCycleTrader.Data;
using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public sealed record HistoricalSignal(
    DateTime TriggerTime, decimal TriggerPrice, string Symbol, string Name, string Market,
    DateTime DailyDecisionDate, TradingMode TradingMode, IReadOnlyList<MarketMode> Strategies,
    string DailyReason);

public sealed record HistoricalSignalResult(
    IReadOnlyList<HistoricalSignal> Signals, int SymbolsWithHourlyCache,
    int SymbolsScanned, int DailyCandidates, int MissingDailyCandles,
    int HourlyTradingDays);

public static class HistoricalSignalScanner
{
    internal delegate StockCandidate? DailyCandidateCheck(string symbol, string name,
        IReadOnlyList<Candle> daily, IReadOnlyList<Candle> market,
        IReadOnlyList<MarketMode> modes, StrategySettings settings);
    internal delegate bool HourlyEntryCheck(IReadOnlyList<Candle> hourly,
        MarketMode mode, StrategySettings settings);

    public static IReadOnlyList<StockInfo> FindDailyCandidateStocks(
        IReadOnlyList<Candle> market, IReadOnlyList<StockInfo> universe,
        Func<string, List<Candle>> getDaily, StrategySettings settings,
        DateTime earliestDate, DateTime today,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
        => FindDailyCandidatesByDate(market, universe, getDaily, settings, earliestDate, today,
            null, cancellationToken, progress).Values.SelectMany(stocks => stocks)
            .DistinctBy(stock => stock.Symbol).ToArray();

    public static IReadOnlyDictionary<DateTime, IReadOnlyList<StockInfo>> FindDailyCandidatesByDate(
        IReadOnlyList<Candle> market, IReadOnlyList<StockInfo> universe,
        Func<string, List<Candle>> getDaily, StrategySettings settings,
        DateTime earliestDate, DateTime today, IReadOnlySet<DateTime>? targetDates,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        var marketBars = market.Where(c => c.Time.Date < today.Date)
            .OrderBy(c => c.Time).GroupBy(c => c.Time.Date).Select(g => g.Last()).ToArray();
        var contexts = BuildContexts(marketBars, settings, earliestDate, today);
        var selected = new Dictionary<DateTime, List<StockInfo>>();
        var relevantDays = contexts.Where(pair => targetDates is null || targetDates.Contains(pair.Key)).ToArray();
        for (int symbolIndex = 0; symbolIndex < universe.Count; symbolIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stock = universe[symbolIndex];
            var daily = getDaily(stock.Symbol).OrderBy(c => c.Time).ToArray();
            foreach (var (date,context) in relevantDays)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modes = ScannerCoordinator.ActiveScanners(context.State.ConfirmedTradingMode, settings);
                if (modes.Count == 0) continue;
                var dailyAtDecision = daily.Where(c => c.Time.Date <= context.DecisionDate).ToArray();
                if (dailyAtDecision.Length < 130 || dailyAtDecision[^1].Time.Date != context.DecisionDate)
                    continue;
                if (ScannerCoordinator.Scan(stock.Symbol, stock.Name, dailyAtDecision,
                    context.MarketAtDecision, modes, settings).Count == 0) continue;
                if (!selected.TryGetValue(date, out var stocks))
                    selected[date] = stocks = new List<StockInfo>();
                stocks.Add(stock);
            }
            if (symbolIndex % 25 == 0 || symbolIndex == universe.Count - 1)
                progress?.Report(symbolIndex + 1);
        }
        return selected.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<StockInfo>)pair.Value);
    }

    public static HistoricalSignalResult Scan(
        IReadOnlyList<Candle> market, IReadOnlyList<StockInfo> universe,
        Func<string, List<Candle>> getDaily, Func<string, List<Candle>> getHourly,
        StrategySettings settings, DateTime earliestDate, DateTime today,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null,
        IReadOnlySet<DateTime>? targetDates = null)
        => ScanCore(market, universe, getDaily, getHourly, settings, earliestDate, today,
            (symbol, name, daily, index, modes, s) =>
                ScannerCoordinator.Scan(symbol, name, daily, index, modes, s).FirstOrDefault(),
            (hourly, mode, s) => StockScanner.CheckEntry60m(hourly, mode, s).IsMatch,
            cancellationToken, progress, targetDates);

    internal static HistoricalSignalResult ScanCore(
        IReadOnlyList<Candle> market, IReadOnlyList<StockInfo> universe,
        Func<string, List<Candle>> getDaily, Func<string, List<Candle>> getHourly,
        StrategySettings settings, DateTime earliestDate, DateTime today,
        DailyCandidateCheck candidateCheck, HourlyEntryCheck entryCheck,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null,
        IReadOnlySet<DateTime>? targetDates = null)
    {
        var marketBars = market.Where(c => c.Time.Date < today.Date)
            .OrderBy(c => c.Time).GroupBy(c => c.Time.Date).Select(g => g.Last()).ToArray();
        var contexts = BuildContexts(marketBars, settings, earliestDate, today);
        var signals = new List<HistoricalSignal>();
        var coveredDates = new HashSet<DateTime>();
        int hourlySymbols = 0, dailyCandidates = 0, missingDaily = 0;

        for (int symbolIndex = 0; symbolIndex < universe.Count; symbolIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stock = universe[symbolIndex];
            var hourly = getHourly(stock.Symbol).OrderBy(c => c.Time).ToArray();
            var relevant = hourly.Select((bar, index) => (bar, index))
                .Where(x => x.bar.Time.Date >= earliestDate.Date && x.bar.Time.Date < today.Date &&
                    (targetDates is null || targetDates.Contains(x.bar.Time.Date)))
                .GroupBy(x => x.bar.Time.Date).ToArray();
            if (relevant.Length > 0)
            {
                hourlySymbols++;
                var daily = getDaily(stock.Symbol).OrderBy(c => c.Time).ToArray();
                foreach (var day in relevant)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!contexts.TryGetValue(day.Key, out var context)) continue;
                    coveredDates.Add(day.Key);
                    var modes = ScannerCoordinator.ActiveScanners(context.State.ConfirmedTradingMode, settings);
                    if (modes.Count == 0) continue;
                    var dailyAtDecision = daily.Where(c => c.Time.Date <= context.DecisionDate).ToArray();
                    if (dailyAtDecision.Length < 130 || dailyAtDecision[^1].Time.Date != context.DecisionDate)
                    {
                        missingDaily++;
                        continue;
                    }
                    var candidate = candidateCheck(stock.Symbol, stock.Name, dailyAtDecision,
                        context.MarketAtDecision, modes, settings);
                    if (candidate is null) continue;
                    dailyCandidates++;

                    foreach (var (bar, index) in day)
                    {
                        var hourlyThroughBar = hourly.AsSpan(0, index + 1).ToArray();
                        if (hourlyThroughBar.Length < 70) continue;
                        var triggered = candidate.MatchedStrategies.Where(mode =>
                            entryCheck(hourlyThroughBar, mode, settings)).ToArray();
                        if (triggered.Length == 0) continue;
                        signals.Add(new HistoricalSignal(BarCloseTime(bar.Time), bar.Close,
                            stock.Symbol, stock.Name, stock.Market, context.DecisionDate,
                            context.State.ConfirmedTradingMode, triggered, candidate.Reason));
                    }
                }
            }
            if (symbolIndex % 25 == 0 || symbolIndex == universe.Count - 1)
                progress?.Report(symbolIndex + 1);
        }

        return new(signals.OrderByDescending(s => s.TriggerTime).ThenBy(s => s.Symbol).ToArray(),
            hourlySymbols, universe.Count, dailyCandidates, missingDaily, coveredDates.Count);
    }

    // Yahoo 60-minute timestamps identify the start of a bar. Taiwan's final
    // regular-session bar ends at 13:30 rather than 14:00.
    public static DateTime BarCloseTime(DateTime barStart)
    {
        var sessionClose = barStart.Date.AddHours(13).AddMinutes(30);
        var close = barStart.AddHours(1);
        return close > sessionClose && barStart.TimeOfDay >= new TimeSpan(13, 0, 0)
            ? sessionClose : close;
    }

    private static Dictionary<DateTime, HistoricalContext> BuildContexts(
        IReadOnlyList<Candle> market, StrategySettings settings, DateTime earliestDate, DateTime today)
    {
        var contexts = new Dictionary<DateTime, HistoricalContext>();
        var state = new MarketState();
        for (int i = 129; i < market.Count; i++)
        {
            if (i > 129 && market[i].Time.Date >= earliestDate.Date && market[i].Time.Date < today.Date)
                contexts[market[i].Time.Date] = new(market[i - 1].Time.Date, state,
                    market.Take(i).ToArray());
            var regime = MarketClassifier.Analyze(market.Take(i + 1).ToArray(), settings).Mode;
            state = TradingModeManager.Advance(state, regime, market[i].Time.Date,
                settings.MarketRegimeConfirmationDays);
        }
        return contexts;
    }

    private sealed record HistoricalContext(DateTime DecisionDate, MarketState State,
        IReadOnlyList<Candle> MarketAtDecision);
}
