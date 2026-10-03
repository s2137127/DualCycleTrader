using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class ScannerCoordinator
{
    public static IReadOnlyList<MarketMode> ActiveScanners(TradingMode mode, StrategySettings settings,
        MarketMode? simulation = null)
    {
        if (simulation is not null)
            return simulation == MarketMode.D_BearTrend ? Array.Empty<MarketMode>() : new[] { simulation.Value };
        return mode switch
        {
            TradingMode.BullishAB when settings.EnableBullishDualScanner =>
                new[] { MarketMode.A_BullTrend, MarketMode.B_BullRange },
            TradingMode.BullishAB => new[] { MarketMode.A_BullTrend },
            TradingMode.BearRangeC => new[] { MarketMode.C_BearRange },
            TradingMode.TransitionE => new[] { MarketMode.E_Transition },
            _ => Array.Empty<MarketMode>()
        };
    }

    public static IReadOnlyList<StockCandidate> Scan(string symbol, string name,
        IReadOnlyList<Candle> daily, IReadOnlyList<Candle> market,
        IReadOnlyList<MarketMode> modes, StrategySettings settings)
    {
        var matches = modes.Select(m => StockScanner.Scan(symbol, name, daily, market, m, settings))
            .Where(c => c is not null).Cast<StockCandidate>().ToArray();
        return MergeMatches(matches);
    }

    public static IReadOnlyList<StockCandidate> MergeMatches(IReadOnlyList<StockCandidate> matches)
    {
        if (matches.Count == 0) return Array.Empty<StockCandidate>();
        var primary = matches[0];
        return new[] { new StockCandidate
        {
            Symbol = primary.Symbol, Name = primary.Name, Mode = primary.Mode,
            MatchedStrategies = matches.Select(c => c.Mode).ToArray(),
            Close = primary.Close, ChangePercent = primary.ChangePercent,
            Rsi14 = primary.Rsi14, RelativeStrength20 = primary.RelativeStrength20,
            DailyMacdDif = primary.DailyMacdDif, DailyMacdDea = primary.DailyMacdDea,
            DailyK = primary.DailyK, DailyD = primary.DailyD, DailyJ = primary.DailyJ,
            Reason = string.Join("；", matches.Select(c => $"{c.StrategyType}：{c.Reason}"))
        } };
    }

    public static IReadOnlyDictionary<MarketMode, StockScanner.EntryCheckResult> CheckEntries(
        IReadOnlyList<Candle> hourly, StockCandidate candidate, StrategySettings settings)
        => candidate.MatchedStrategies.ToDictionary(m => m,
            m => StockScanner.CheckEntry60m(hourly, m, settings));
}
