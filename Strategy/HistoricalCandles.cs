using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class HistoricalCandles
{
    public static List<Candle> DailyAtClose(IReadOnlyList<Candle> candles, DateTime date)
        => candles.Where(c => c.Time.Date <= date.Date).ToList();

    public static List<Candle> HourlyAtClose(IReadOnlyList<Candle> candles, DateTime date)
        => candles.Where(c => c.Time.Date < date.Date ||
            (c.Time.Date == date.Date && c.Time.TimeOfDay <= new TimeSpan(13, 30, 0))).ToList();

    public static bool HasCandleOn(IReadOnlyList<Candle> candles, DateTime date)
        => candles.Count > 0 && candles[^1].Time.Date == date.Date;
}
