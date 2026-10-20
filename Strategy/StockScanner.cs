using DualCycleTrader.Indicators;
using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class StockScanner
{
    // B remains the candidate's legacy mode code. Market classification never gates selection.
    public static StockCandidate? Scan(string symbol, string name, IReadOnlyList<Candle> x,
        IReadOnlyList<Candle> market, MarketMode mode, StrategySettings s)
    {
        if (mode != MarketMode.B_BullRange) return null;
        var checks = Conditions(x, s);
        if (checks.Values.Any(passed => !passed)) return null;
        double close = (double)x[^1].Close;
        double previousClose = (double)x[^2].Close;
        var macd = Ta.Macd(x, s.DailyMacdFast, s.DailyMacdSlow, s.DailyMacdSignal)!.Value;
        var kdj = Ta.Kdj(x, 9) ?? (0, 0, 0);
        return new StockCandidate
        {
            Symbol = symbol, Name = name, Mode = MarketMode.B_BullRange, Close = x[^1].Close,
            ChangePercent = previousClose == 0 ? 0 : (close - previousClose) / previousClose * 100,
            Rsi14 = Ta.Rsi(x, s.DailyRsiPeriod) ?? 0,
            RelativeStrength20 = RelativeStrength20(x, market),
            DailyMacdDif = macd.dif, DailyMacdDea = macd.dea,
            DailyK = kdj.Item1, DailyD = kdj.Item2, DailyJ = kdj.Item3,
            Reason = "收紅 K＋MACD 負柱縮短＋股價 > MA5 > MA20＋MA5/MA20 向上＋RSI 剛上翹＋+DI > -DI"
        };
    }

    public static StockScanDebug DebugScan(string symbol, string name, IReadOnlyList<Candle> x,
        IReadOnlyList<Candle> market, MarketMode mode, StrategySettings s)
    {
        var checks = Conditions(x, s);
        var macd = Ta.Macd(x, s.DailyMacdFast, s.DailyMacdSlow, s.DailyMacdSignal);
        return new StockScanDebug
        {
            Symbol = symbol, Mode = MarketMode.B_BullRange, Date = x.Count > 0 ? x[^1].Time : default,
            Close = x.Count > 0 ? (double)x[^1].Close : 0,
            Ma20 = Ta.Sma(x, 5) ?? 0, Ma50 = Ta.Sma(x, 20) ?? 0,
            Rsi14 = Ta.Rsi(x, s.DailyRsiPeriod) ?? 0,
            MacdDif = macd?.dif ?? 0, MacdDea = macd?.dea ?? 0,
            HardConditions = checks,
            Candidate = Scan(symbol, name, x, market, MarketMode.B_BullRange, s),
            Details = string.Join("、", checks.Select(item => $"{(item.Value ? "✓" : "✗")} {item.Key}"))
        };
    }

    private static Dictionary<string, bool> Conditions(IReadOnlyList<Candle> x, StrategySettings s)
    {
        int minimum = Math.Max(25, Math.Max(s.DailyMacdSlow + s.DailyMacdSignal + 1,
            s.DailyRsiPeriod + 4));
        if (x.Count < minimum)
            return new() { [$"個股日 K 至少 {minimum} 根（目前 {x.Count} 根）"] = false };
        double close = (double)x[^1].Close;
        double ma5 = Ta.Sma(x, 5)!.Value, ma20 = Ta.Sma(x, 20)!.Value;
        var macd = Ta.Macd(x, s.DailyMacdFast, s.DailyMacdSlow, s.DailyMacdSignal);
        var priorMacd = Ta.Macd(x.Take(x.Count - 1).ToArray(), s.DailyMacdFast,
            s.DailyMacdSlow, s.DailyMacdSignal);
        var adx = Ta.Adx(x);
        var rsi = Enumerable.Range(0, 4)
            .Select(offset => Ta.Rsi(x.Take(x.Count - offset).ToArray(), s.DailyRsiPeriod))
            .ToArray();
        return new()
        {
            ["當日收紅 K（收盤 > 開盤）"] = x[^1].Close > x[^1].Open,
            ["MACD 負柱縮短"] = macd is not null && priorMacd is not null &&
                macd.Value.hist < 0 && priorMacd.Value.hist < 0 &&
                macd.Value.hist > priorMacd.Value.hist,
            ["股價 > MA5 > MA20"] = close > ma5 && ma5 > ma20,
            ["MA5、MA20 向上（高於前一日）"] = ma5 > Ta.Sma(x, 5, 1) &&
                ma20 > Ta.Sma(x, 20, 1),
            [$"RSI({s.DailyRsiPeriod}) 今天 > 昨天，昨天 ≤ 前天 ≤ 大前天"] =
                rsi.All(value => value is not null) && rsi[0] > rsi[1] &&
                rsi[1] <= rsi[2] && rsi[2] <= rsi[3],
            ["+DI > -DI"] = adx is not null && adx.Value.plusDi > adx.Value.minusDi
        };
    }

    private static double RelativeStrength20(IReadOnlyList<Candle> stock, IReadOnlyList<Candle> market)
    {
        if (stock.Count < 21 || market.Count < 21) return 0;
        double stockReturn = (double)(stock[^1].Close / stock[^21].Close - 1) * 100;
        double marketReturn = (double)(market[^1].Close / market[^21].Close - 1) * 100;
        return stockReturn - marketReturn;
    }
}
