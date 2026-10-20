using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class ScannerCoordinator
{
    public static IReadOnlyList<MarketMode> ActiveScanners(TradingMode mode, StrategySettings settings,
        MarketMode? simulation = null) => new[] { MarketMode.B_BullRange };

    public static IReadOnlyList<StockCandidate> Scan(string symbol, string name,
        IReadOnlyList<Candle> daily, IReadOnlyList<Candle> market,
        IReadOnlyList<MarketMode> modes, StrategySettings settings)
    {
        var candidate = StockScanner.Scan(symbol, name, daily, market, MarketMode.B_BullRange, settings);
        if (candidate is null) return Array.Empty<StockCandidate>();
        return new[] { new StockCandidate
        {
            Symbol = candidate.Symbol, Name = candidate.Name, Mode = candidate.Mode,
            MatchedStrategies = new[] { MarketMode.B_BullRange },
            Close = candidate.Close, ChangePercent = candidate.ChangePercent,
            Rsi14 = candidate.Rsi14, RelativeStrength20 = candidate.RelativeStrength20,
            CandidateScore = candidate.CandidateScore,
            DailyMacdDif = candidate.DailyMacdDif, DailyMacdDea = candidate.DailyMacdDea,
            DailyK = candidate.DailyK, DailyD = candidate.DailyD, DailyJ = candidate.DailyJ,
            Reason = candidate.Reason
        } };
    }

    public static IReadOnlyList<StockCandidate> MergeMatches(IReadOnlyList<StockCandidate> matches)
        => matches.Count == 0 ? Array.Empty<StockCandidate>() : new[] { matches[0] };
}
