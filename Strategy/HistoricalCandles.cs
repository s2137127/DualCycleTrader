using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class HistoricalCandles
{
    // Daily indicators must see one candle per trading date. Keep the latest
    // intraday revision when both an early and a final candle were stored.
    public static List<Candle> NormalizeDaily(IEnumerable<Candle> candles)
        => candles.OrderBy(c => c.Time).GroupBy(c => c.Time.Date)
            .Select(group => group.Last()).ToList();

    public static List<Candle> DailyAtClose(IReadOnlyList<Candle> candles, DateTime date)
        => NormalizeDaily(candles.Where(c => c.Time.Date <= date.Date));

    public static bool HasCandleOn(IReadOnlyList<Candle> candles, DateTime date)
        => candles.Count > 0 && candles[^1].Time.Date == date.Date;
}
