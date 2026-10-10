using DualCycleTrader.Indicators;
using DualCycleTrader.Models;
using DualCycleTrader.Strategy;
using DualCycleTrader.Data;
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
            close, close + (flat ? 0 : 2), close - (flat ? 0 : 2), close, 1000));
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
Check(double.IsFinite(full.adx) && double.IsFinite(full.plusDi) && double.IsFinite(full.minusDi), "finite values");

foreach (var invalid in new[] { candles.AsEnumerable().Reverse().ToArray(),
    candles.Select((c, i) => i == 1 ? c with { Time = candles[0].Time } : c).ToArray() })
{
    try { Ta.Adx(invalid, 14); throw new Exception("order check missing"); }
    catch (ArgumentException) { }
}

Console.WriteLine("ADX checks passed");

var settings = new StrategySettings();
var state = new MarketState();
var first = new DateTime(2026, 9, 28);
state = TradingModeManager.Advance(state, MarketMode.E_Transition, first, 3);
state = TradingModeManager.Advance(state, MarketMode.B_BullRange, first.AddDays(1), 3);
Check(state.ConfirmedTradingMode == TradingMode.Initializing && state.CandidateRegimeDays == 1, "E to B one day");
state = TradingModeManager.Advance(state, MarketMode.E_Transition, first.AddDays(2), 3);
Check(state.ConfirmedTradingMode != TradingMode.BullishAB, "E-B-E must not confirm B");
state = new MarketState();
state = TradingModeManager.Advance(state, MarketMode.A_BullTrend, first, 3);
state = TradingModeManager.Advance(state, MarketMode.B_BullRange, first.AddDays(1), 3);
state = TradingModeManager.Advance(state, MarketMode.A_BullTrend, first.AddDays(2), 3);
Check(state.ConfirmedTradingMode == TradingMode.Initializing && state.CandidateRegimeDays == 1,
    "A/B alternation is not three matching days");

state = new MarketState();
for (int i = 0; i < 3; i++)
    state = TradingModeManager.Advance(state, MarketMode.B_BullRange, first.AddDays(i), 3);
Check(state.ConfirmedTradingMode == TradingMode.BullishAB, "third B day confirms");
for (int i = 0; i < 10; i++)
    state = TradingModeManager.Advance(state, MarketMode.D_BearTrend, first.AddDays(2), 3);
Check(state.ConfirmedTradingMode == TradingMode.BullishAB && state.CandidateRegimeDays == 3,
    "same-day repeat does not advance");
state = TradingModeManager.Advance(state, MarketMode.D_BearTrend, first.AddDays(3), 3);
Check(!state.DisableNewLongEntries, "one D day keeps existing mode");
state = TradingModeManager.Advance(state, MarketMode.D_BearTrend, first.AddDays(4), 3);
state = TradingModeManager.Advance(state, MarketMode.D_BearTrend, first.AddDays(5), 3);
Check(state.DisableNewLongEntries && ScannerCoordinator.ActiveScanners(state.ConfirmedTradingMode, settings).Count == 0,
    "confirmed D disables new entries");

var modes = ScannerCoordinator.ActiveScanners(TradingMode.BullishAB, settings);
Check(modes.SequenceEqual(new[] { MarketMode.A_BullTrend, MarketMode.B_BullRange }), "A+B modes");
Check(ScannerCoordinator.ActiveScanners(TradingMode.BearRangeC, settings).Single() == MarketMode.C_BearRange,
    "C is independent");
Check(ScannerCoordinator.ActiveScanners(TradingMode.BullishAB, settings, MarketMode.C_BearRange)
    .Single() == MarketMode.C_BearRange, "simulation selection");
var merged = ScannerCoordinator.MergeMatches(new[]
{
    new StockCandidate { Symbol = "X.TW", Mode = MarketMode.A_BullTrend, Reason = "breakout" },
    new StockCandidate { Symbol = "X.TW", Mode = MarketMode.B_BullRange, Reason = "pullback" }
});
Check(merged.Count == 1 && merged[0].MatchedStrategies.SequenceEqual(modes), "A/B overlap deduplicated");
var path = Path.Combine(Path.GetTempPath(), $"dct-market-state-{Guid.NewGuid():N}.json");
try
{
    var manager = new TradingModeManager(settings, path);
    var history = Candles(160);
    var persisted = manager.Evaluate(history);
    Check(File.Exists(path) && manager.Load() == persisted, "state survives restart");
    Check(manager.Evaluate(history) == persisted, "repeat evaluation is idempotent");
    var simulated = ScannerCoordinator.ActiveScanners(persisted.ConfirmedTradingMode, settings,
        MarketMode.C_BearRange);
    Check(simulated.Single() == MarketMode.C_BearRange && manager.Load() == persisted,
        "simulation does not persist");
}
finally { if (File.Exists(path)) File.Delete(path); }
Console.WriteLine("Trading mode checks passed");

static List<Candle> DailySeries(IEnumerable<decimal> prices, decimal lastVolume = 1000)
{
    var values = prices.ToArray();
    return values.Select((close, index) => new Candle(
        new DateTime(2024, 1, 1).AddDays(index), close - 0.1m,
        close + 0.2m, close - 0.2m, close,
        index == values.Length - 1 ? lastVolume : 1000)).ToList();
}

var aPrices = Enumerable.Range(0, 120).Select(i => 100m + i * 0.2m).ToList();
foreach (var change in new[] { 0.61m, 0.10m, 0.89m, 0.78m, 0.44m,
    -0.48m, 0.45m, 0.45m, -0.97m, 0.19m })
    aPrices.Add(aPrices[^1] + change);
var aDaily = DailySeries(aPrices, 2000);
var aMarket = DailySeries(Enumerable.Repeat(100m, aDaily.Count));
var aScan = StockScanner.Scan("A.TW", "A", aDaily, aMarket,
    MarketMode.A_BullTrend, settings);
var aDebug = StockScanner.DebugScan("A.TW", "A", aDaily, aMarket,
    MarketMode.A_BullTrend, settings);
Check(aScan is not null && aDebug.Passed &&
    aDebug.HardConditions["股價 > MA20 > MA50 > MA100"] &&
    aDebug.HardConditions["當日收紅 K（收盤 > 開盤）"] &&
    aDebug.HardConditions["RSI(5) < 60"] &&
    aDebug.HardConditions["今日 RSI(5) > 昨日 RSI(5)"] &&
    aDebug.HardConditions["MACD DIF > DEA"],
    "A: rising RSI(5) below 60 and original MACD qualify");
var customMaSettings = new StrategySettings
{ BullishMaShortPeriod = 10, BullishMaMediumPeriod = 30, BullishMaLongPeriod = 80 };
var customMaDebug = StockScanner.DebugScan("A.TW", "A", aDaily, aMarket,
    MarketMode.A_BullTrend, customMaSettings);
Check(customMaDebug.HardConditions.ContainsKey("股價 > MA10 > MA30 > MA80") &&
    Math.Abs(customMaDebug.Ma20 - Ta.Sma(aDaily,10)!.Value) < 0.00001 &&
    Math.Abs(customMaDebug.Ma50 - Ta.Sma(aDaily,30)!.Value) < 0.00001 &&
    Math.Abs(customMaDebug.Ma100 - Ta.Sma(aDaily,80)!.Value) < 0.00001 &&
    customMaDebug.Passed == (StockScanner.Scan("A.TW", "A", aDaily, aMarket,
        MarketMode.A_BullTrend, customMaSettings) is not null),
    "A: configurable MA periods are used by scanner and debug");
var aDoji = aDaily.ToList();
aDoji[^1] = aDoji[^1] with { Open = aDoji[^1].Close };
Check(StockScanner.Scan("A.TW", "A", aDoji, aMarket,
    MarketMode.A_BullTrend, settings) is null &&
    !StockScanner.DebugScan("A.TW", "A", aDoji, aMarket,
        MarketMode.A_BullTrend, settings).HardConditions["當日收紅 K（收盤 > 開盤）"],
    "A: doji is rejected");
var aHighRsi = DailySeries(Enumerable.Range(0, 130).Select(i => 100m + i * 0.2m));
Check(!StockScanner.DebugScan("A.TW", "A", aHighRsi, aMarket,
    MarketMode.A_BullTrend, settings).HardConditions["RSI(5) < 60"],
    "A: RSI(5) at 60 or above is rejected");
var relaxedRsiSettings = new StrategySettings { Rsi5UpperLimit = 101 };
Check(StockScanner.DebugScan("A.TW", "A", aHighRsi, aMarket,
    MarketMode.A_BullTrend, relaxedRsiSettings).HardConditions["RSI(5) < 101"] &&
    StockScanner.DebugScan("A.TW", "A", aDaily, aMarket,
        MarketMode.A_BullTrend, relaxedRsiSettings).Passed,
    "A: configurable RSI(5) upper limit is used by debug and scan");
var aFallingRsi = aDaily.ToList();
aFallingRsi[^1] = aFallingRsi[^1] with
{ Close = aFallingRsi[^2].Close - 0.1m, Open = aFallingRsi[^2].Close - 0.2m };
Check(!StockScanner.DebugScan("A.TW", "A", aFallingRsi, aMarket,
    MarketMode.A_BullTrend, settings).HardConditions["今日 RSI(5) > 昨日 RSI(5)"],
    "A: RSI(5) must rise");

var bPrices = Enumerable.Range(0, 100).Select(i => 150m - i * 0.5m)
    .Concat(Enumerable.Range(0, 30).Select(i => 100m + i * 0.7m)).ToArray();
var bDaily = DailySeries(bPrices);
var bMarket = DailySeries(Enumerable.Repeat(100m, bDaily.Count));
var bDebug = StockScanner.DebugScan("B.TW", "B", bDaily, bMarket,
    MarketMode.B_BullRange, settings);
Check(bDebug.HardConditions["股價 > MA20 > MA50"] &&
    bDebug.Ma50 < bDebug.Ma100,
    "B: new MA20/MA50 order can pass while old MA50/MA100 order fails");
Check(bDebug.Passed == (StockScanner.Scan("B.TW", "B", bDaily, bMarket,
    MarketMode.B_BullRange, settings) is not null),
    "B: debug matches scanner");
Check(StockScanner.DebugScan("B.TW", "B", bDaily, bMarket,
    MarketMode.B_BullRange, customMaSettings).HardConditions.ContainsKey("股價 > MA10 > MA30"),
    "B: configurable MA periods are used by debug");
Check(bDebug.HardConditions.ContainsKey("MACD 負柱縮短") &&
    bDebug.HardConditions.ContainsKey("RSI 介於 40～55") &&
    bDebug.HardConditions.ContainsKey("成交量低於前 20 日均量") &&
    bDebug.HardConditions.ContainsKey("當日收紅 K（收盤 > 開盤）") &&
    bDebug.HardConditions.ContainsKey("RSI(5) < 60") &&
    bDebug.HardConditions.ContainsKey("今日 RSI(5) > 昨日 RSI(5)"),
    "B: original pullback filters are restored");
Check(StockScanner.DebugScan("B.TW", "B", bDaily, bMarket,
    MarketMode.B_BullRange, relaxedRsiSettings).HardConditions.ContainsKey("RSI(5) < 101"),
    "B: configurable RSI(5) upper limit appears in debug");
var oldDefaults = JsonSerializer.Deserialize<StrategySettings>(
    "{\"PullbackMinPercent\":3,\"PullbackMaxPercent\":10,\"ABScannerVersion\":2,\"BreakoutVolumeMultiple\":1.3}")!;
oldDefaults.MigrateLegacyScannerDefaults();
Check(oldDefaults.PullbackMinPercent == 5 && oldDefaults.PullbackMaxPercent == 12 &&
    oldDefaults.BreakoutStrongVolumeRatio == 1.3,
    "short-cycle defaults migrate back without losing custom volume ratio");
Console.WriteLine("Original A/B scanner checks passed");

var historicalMarket = Candles(180);
var historicalDate = historicalMarket[159].Time.Date;
var asOf = HistoricalCandles.DailyAtClose(historicalMarket, historicalDate);
Check(asOf.Count == 160 && asOf[^1].Time.Date == historicalDate, "daily as-of close");
var historicalPath = Path.Combine(Path.GetTempPath(), $"dct-history-no-write-{Guid.NewGuid():N}.json");
var historicalManager = new TradingModeManager(settings, historicalPath);
var originalHistory = historicalManager.EvaluateHistorical(asOf, historicalDate);
var changedFuture = historicalMarket.ToArray();
changedFuture[^1] = changedFuture[^1] with { Close = 99999 };
Check(historicalManager.EvaluateHistorical(changedFuture, historicalDate) == originalHistory,
    "future candles cannot change historical mode");
Check(!File.Exists(historicalPath), "historical analysis is read only");
Console.WriteLine("Historical close checks passed");

var cacheDir = Path.Combine(Path.GetTempPath(), $"dct-history-cache-{Guid.NewGuid():N}");
Directory.CreateDirectory(cacheDir);
var remote = new FakeMarketDataProvider();
var provider = new CachedMarketDataProvider(remote, settings, cacheDir);
var recent = Enumerable.Range(0, 20).Select(i =>
    new Candle(DateTime.Today.AddDays(i - 19), 100, 100, 100, 100, 1)).ToList();
File.WriteAllText(Path.Combine(cacheDir, "TEST_TW_D.json"), JsonSerializer.Serialize(recent));
var earliest = DateTime.Today.AddMonths(-6);
var backfilled = await provider.GetDailyForHistoryAsync("TEST.TW", earliest);
Check(remote.LastDailyDays >= (DateTime.Today - earliest).Days + 220,
    "update requests enough calendar history for six-month lookback");
Check(backfilled.Count(c => c.Time.Date <= earliest) >= 130,
    "backfill retains 130 candles through oldest selectable date");
Console.WriteLine("Historical cache checks passed");

using (var workbook = new MemoryStream())
{
    DualCycleTrader.ExcelExporter.Write(workbook, new List<IReadOnlyList<string>>
    {
        new[] { "代號", "名稱" }, new[] { "2330", "台積電 & 測試" }
    });
    workbook.Position = 0;
    using var zip = new System.IO.Compression.ZipArchive(workbook,
        System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
    Check(zip.GetEntry("[Content_Types].xml") is not null &&
        zip.GetEntry("xl/workbook.xml") is not null, "Excel package parts");
    using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
    var xml = new System.Xml.XmlDocument();
    xml.Load(sheet);
    Check(xml.InnerText.Contains("台積電 & 測試"), "Excel Unicode and XML escaping");
}
Console.WriteLine("Excel export checks passed");

var taiwanOptions = new JsonSerializerOptions();
taiwanOptions.Converters.Add(new Web.Services.TaiwanDateTimeConverter());
var normalized = JsonSerializer.Deserialize<DateTime>("\"2026-10-03T05:00:00Z\"", taiwanOptions);
Check(normalized == new DateTime(2026, 10, 3, 13, 0, 0), "Taiwan market time normalization");
Console.WriteLine("Timezone normalization checks passed");

sealed class FakeMarketDataProvider : IMarketDataProvider
{
    public int LastDailyDays { get; private set; }
    public Task<List<Candle>> GetDailyAsync(string symbol, int days = 260)
    {
        LastDailyDays = days;
        return Task.FromResult(Enumerable.Range(0, days).Select(i =>
            new Candle(DateTime.Today.AddDays(i - days + 1), 100, 100, 100, 100, 1)).ToList());
    }

}
