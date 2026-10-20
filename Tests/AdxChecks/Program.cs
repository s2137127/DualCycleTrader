using DualCycleTrader.Indicators;
using DualCycleTrader.Data;
using DualCycleTrader.Models;
using DualCycleTrader.Strategy;
using System.Text.Json;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static List<Candle> Candles(int count, bool flat = false)
{
    var result = new List<Candle>();
    for (int i = 0; i < count; i++)
    {
        decimal close = flat ? 100 : 100 + i * 0.13m + (i % 7 - 3) * 0.8m;
        result.Add(new Candle(new DateTime(2024, 1, 1).AddDays(i),
            close - 0.1m, close + (flat ? 0 : 2), close - (flat ? 0 : 2), close, 1000));
    }
    return result;
}

var candles = Candles(500);
var rows = Ta.DebugAdx(candles.Take(28).ToArray());
Check(rows[^2].Adx is null && rows[^1].Adx is not null, "ADX seed position");
var full = Ta.Adx(candles, 14)!.Value;
var shorter = Ta.Adx(candles.TakeLast(300).ToArray(), 14)!.Value;
Check(Math.Abs(full.adx - shorter.adx) < 1e-5, "300/500 bar ADX convergence");
Check(Math.Abs(full.plusDi - shorter.plusDi) < 1e-5, "300/500 bar +DI convergence");
Check(Math.Abs(full.minusDi - shorter.minusDi) < 1e-5, "300/500 bar -DI convergence");
var flat = Ta.Adx(Candles(30, flat: true), 14)!.Value;
Check(flat.adx == 0 && flat.plusDi == 0 && flat.minusDi == 0, "zero-range bars");
foreach (var invalid in new[] { candles.AsEnumerable().Reverse().ToArray(),
    candles.Select((c, i) => i == 1 ? c with { Time = candles[0].Time } : c).ToArray() })
{
    try { Ta.Adx(invalid, 14); throw new Exception("order check missing"); }
    catch (ArgumentException) { }
}
Console.WriteLine("ADX checks passed");

var settings = new StrategySettings();
foreach (TradingMode mode in Enum.GetValues<TradingMode>())
    Check(ScannerCoordinator.ActiveScanners(mode, settings)
        .SequenceEqual(new[] { MarketMode.B_BullRange }), $"B scans in {mode}");
Check(ScannerCoordinator.ActiveScanners(TradingMode.BearTrendD, settings,
    MarketMode.C_BearRange).Single() == MarketMode.B_BullRange,
    "market simulation does not change stock selection");

var debug = StockScanner.DebugScan("X.TW", "X", candles, Candles(500, true),
    MarketMode.B_BullRange, settings);
Check(debug.HardConditions.Count == 6, "exactly six B conditions");
Check(debug.HardConditions.ContainsKey("+DI > -DI"), "stock DI condition");
Check(debug.HardConditions.Keys.Any(key => key.Contains("MA5 > MA20")), "MA5 and MA20 condition");
var customMaSettings = new StrategySettings
    { ScannerMaShortPeriod = 3, ScannerMaLongPeriod = 8 };
var customMaDebug = StockScanner.DebugScan("X.TW", "X", candles,
    Candles(500, true), MarketMode.B_BullRange, customMaSettings);
Check(customMaDebug.HardConditions.ContainsKey("股價 > MA3 > MA8") &&
    customMaDebug.HardConditions.ContainsKey("MA3、MA8 向上（高於前一日）") &&
    Math.Abs(customMaDebug.Ma20 - Ta.Sma(candles, 3)!.Value) < 1e-9 &&
    Math.Abs(customMaDebug.Ma50 - Ta.Sma(candles, 8)!.Value) < 1e-9,
    "both scanner MA conditions use the configured periods");
customMaSettings.ScannerMaLongPeriod = 60;
Check(StockScanner.MinimumBars(customMaSettings) == 61,
    "historical scanner requests enough bars for the configured long MA slope");
Check(debug.HardConditions.Keys.Any(key => key.Contains("今天 > 昨天，昨天 ≤ 前天 ≤ 大前天")),
    "RSI turn-up pattern");
static List<Candle> Prices(IEnumerable<decimal> prices) => prices.Select((close, index) =>
    new Candle(new DateTime(2024, 1, 1).AddDays(index), close - 0.1m,
        close + 0.2m, close - 0.2m, close, 1000)).ToList();
bool RsiTurnsUp(IEnumerable<decimal> prices) => StockScanner.DebugScan("X.TW", "X",
    Prices(prices), Candles(100, true), MarketMode.B_BullRange, settings)
    .HardConditions[$"RSI({settings.DailyRsiPeriod}) 今天 > 昨天，昨天 ≤ 前天 ≤ 大前天"];
Check(RsiTurnsUp(Enumerable.Repeat(100m, 80).Concat(new[] { 99m, 98m, 97m, 98m })),
    "RSI declines then turns up");
Check(RsiTurnsUp(Enumerable.Repeat(100m, 80).Append(101m)),
    "RSI stays flat then turns up");
Check(!RsiTurnsUp(Enumerable.Repeat(100m, 80).Concat(new[] { 99m, 100m, 101m, 102m })),
    "RSI already rising is not a fresh turn");
const string macdKey = "MACD 四天均為負柱，今天 > 昨天，昨天 ≤ 前天 ≤ 大前天";
List<Candle>? newMacdPattern = null, oldMacdOnly = null, mixedSignPattern = null;
for (int seed = 0; seed < 10000 &&
    (newMacdPattern is null || oldMacdOnly is null || mixedSignPattern is null); seed++)
{
    var random = new Random(seed);
    decimal close = 100;
    var prices = new List<decimal>();
    for (int day = 0; day < 90; day++)
    {
        close += (decimal)(random.NextDouble() - 0.52) * 2;
        prices.Add(close);
    }
    var series = Prices(prices);
    var hist = Enumerable.Range(0, 4).Select(offset => Ta.Macd(
        series.Take(series.Count - offset).ToArray(), 12, 26, 9)!.Value.hist).ToArray();
    if (hist[0] < 0 && hist[0] > hist[1])
    {
        if (hist[1] <= hist[2] && hist[2] <= hist[3])
        {
            if (hist.All(value => value < 0)) newMacdPattern ??= series;
            else mixedSignPattern ??= series;
        }
        else if (hist.All(value => value < 0)) oldMacdOnly ??= series;
    }
}
Check(newMacdPattern is not null && oldMacdOnly is not null && mixedSignPattern is not null,
    "found deterministic MACD examples");
Check(StockScanner.DebugScan("X.TW", "X", newMacdPattern!, candles,
    MarketMode.B_BullRange, settings).HardConditions[macdKey],
    "MACD negative bars turn up after declining or flat bars");
Check(!StockScanner.DebugScan("X.TW", "X", oldMacdOnly!, candles,
    MarketMode.B_BullRange, settings).HardConditions[macdKey],
    "one-day MACD shrinkage without prior decline is rejected");
Check(!StockScanner.DebugScan("X.TW", "X", mixedSignPattern!, candles,
    MarketMode.B_BullRange, settings).HardConditions[macdKey],
    "all four MACD bars must be negative");
var holidayPrices = Enumerable.Repeat(7m, 80).Concat(new[]
    { 8.11m, 8.15m, 8.96m, 8.96m, 9.18m, 8.74m, 9.01m, 8.86m, 8.74m, 9m });
var holidayBars = Prices(holidayPrices);
var laterRevision = holidayBars[^2] with { Time = holidayBars[^2].Time.AddHours(4) };
var duplicateDayBars = holidayBars.Take(holidayBars.Count - 1)
    .Append(laterRevision).Append(holidayBars[^1]).ToList();
var normalized = HistoricalCandles.NormalizeDaily(duplicateDayBars);
Check(normalized.Count == holidayBars.Count && normalized[^2].Time == laterRevision.Time,
    "latest revision is retained once per trading date");
var tradingDates = new[] { new DateTime(2026, 9, 23), new DateTime(2026, 9, 24),
    new DateTime(2026, 9, 29), new DateTime(2026, 9, 30) };
var holidayWindow = tradingDates.Select((day, index) =>
    holidayBars[holidayBars.Count - 4 + index] with { Time = day.AddHours(9) }).ToList();
holidayWindow.Insert(3, holidayWindow[2] with { Time = tradingDates[2].AddHours(13) });
var asOfSeptember30 = HistoricalCandles.DailyAtClose(holidayWindow, tradingDates[3]);
Check(asOfSeptember30.Select(bar => bar.Time.Date).SequenceEqual(tradingDates),
    "historical scan takes four trading dates across the September holiday");
var previousTradingDay = HistoricalCandles.DailyAtClose(
    holidayWindow.Take(holidayWindow.Count - 1).ToList(), tradingDates[3]);
Check(previousTradingDay[^1].Time.Date == tradingDates[2] &&
    previousTradingDay.Count == 3,
    "historical scan uses the latest earlier trading date when the selected day is absent");
Check(HistoricalCandles.HasCandleOn(holidayWindow, tradingDates[2]) &&
    !HistoricalCandles.HasCandleOn(holidayWindow, new DateTime(2026, 9, 28)),
    "historical date check finds the selected date even when later bars exist");
var shortMacdSettings = new StrategySettings
    { DailyMacdFast = 5, DailyMacdSlow = 13, DailyMacdSignal = 5 };
var rawHist = Enumerable.Range(0, 4).Select(offset => Ta.Macd(
    duplicateDayBars.Take(duplicateDayBars.Count - offset).ToArray(), 5, 13, 5)!.Value.hist).ToArray();
Check(rawHist.All(value => value < 0) && rawHist[0] > rawHist[1] &&
    rawHist[1] <= rawHist[2] && rawHist[2] <= rawHist[3],
    "duplicate same-day candle would have caused a false MACD match");
var holidayDebug = StockScanner.DebugScan("X.TW", "X", duplicateDayBars, candles,
    MarketMode.B_BullRange, shortMacdSettings);
Check(!holidayDebug.HardConditions[macdKey] &&
    holidayDebug.Details.Contains($"{laterRevision.Time:MM/dd}"),
    "scanner uses four unique trading dates and reports their MACD bars");
Check(debug.Passed == debug.HardConditions.Values.All(value => value),
    "debug agrees with scanner");
var doji = candles.ToList();
doji[^1] = doji[^1] with { Open = doji[^1].Close };
Check(!StockScanner.DebugScan("X.TW", "X", doji, candles,
    MarketMode.B_BullRange, settings).HardConditions["當日收紅 K（收盤 > 開盤）"],
    "doji fails red-candle condition");
Console.WriteLine("B scanner checks passed");

var september30 = new DateTime(2026, 9, 30);
var remoteBars = Candles(140).Select((bar, index) =>
    bar with { Time = new DateTime(2026, 4, 1).AddDays(index) }).ToList();
remoteBars[^4] = remoteBars[^4] with { Time = new DateTime(2026, 9, 23) };
remoteBars[^3] = remoteBars[^3] with { Time = new DateTime(2026, 9, 24) };
remoteBars[^2] = remoteBars[^2] with { Time = new DateTime(2026, 9, 29) };
remoteBars[^1] = remoteBars[^1] with { Time = september30 };
var cacheDirectory = Path.Combine(Path.GetTempPath(), "DualCycleTraderHistoryTest-" + Guid.NewGuid());
Directory.CreateDirectory(cacheDirectory);
try
{
    File.WriteAllText(Path.Combine(cacheDirectory, "2064_TWO_D.json"),
        JsonSerializer.Serialize(remoteBars.Take(remoteBars.Count - 1)));
    var remote = new HistoricalTestProvider(remoteBars);
    var provider = new CachedMarketDataProvider(remote, settings, cacheDirectory);
    var repaired = await provider.GetDailyAtAsync("2064.TWO", september30, 25);
    Check(HistoricalCandles.HasCandleOn(repaired, september30) &&
        remote.Calls == 1 && provider.GetCachedDaily("2064.TWO").Count == remoteBars.Count,
        "desktop history analysis backfills a missing selected trading date");
    await provider.GetDailyAtAsync("2064.TWO", september30, 25);
    Check(remote.Calls == 1, "desktop history analysis reuses the repaired cache");
}
finally
{
    var fullCachePath = Path.GetFullPath(cacheDirectory);
    if (fullCachePath.StartsWith(Path.GetFullPath(Path.GetTempPath()),
        StringComparison.OrdinalIgnoreCase))
        Directory.Delete(fullCachePath, recursive: true);
}

sealed class HistoricalTestProvider(List<Candle> bars) : IMarketDataProvider
{
    public int Calls { get; private set; }
    public Task<List<Candle>> GetDailyAsync(string symbol, int days = 260)
    {
        Calls++;
        return Task.FromResult(bars);
    }
}
