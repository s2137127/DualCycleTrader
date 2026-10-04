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
var entryChecks = ScannerCoordinator.CheckEntries(Candles(80), merged[0], settings);
Check(entryChecks.ContainsKey(MarketMode.A_BullTrend) && entryChecks.ContainsKey(MarketMode.B_BullRange),
    "hourly checks use each candidate strategy");

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
        new DateTime(2024, 1, 1).AddDays(index), close,
        close + 0.2m, close - 0.2m, close,
        index == values.Length - 1 ? lastVolume : 1000)).ToList();
}

var aPrices = Enumerable.Range(0, 100).Select(i => 130m - i * 0.3m)
    .Concat(Enumerable.Repeat(100m, 27))
    .Concat(Enumerable.Range(1, 10).Select(i => 100m + i))
    .Append(111m).ToArray();
var aDaily = DailySeries(aPrices);
var aMarket = DailySeries(Enumerable.Repeat(100m, aDaily.Count));
var a1 = StockScanner.DebugScan("A.TW", "A", aDaily, aMarket, MarketMode.A_BullTrend, settings);
Check(Ta.Sma(aDaily,100) < Ta.Sma(aDaily,100,1) && a1.Passed,
    "A1: falling MA100 cannot exclude short breakout");
var a2Daily = DailySeries(aPrices, 1300);
var a2 = StockScanner.DebugScan("A.TW", "A", a2Daily, aMarket, MarketMode.A_BullTrend, settings);
Check(a2.Passed && Math.Abs(a2.VolumeRatio - 1.3) < 0.01,
    "A2: breakout with 1.3x volume qualifies");
var strongMarket = DailySeries(Enumerable.Repeat(100m, aDaily.Count - 20)
    .Concat(Enumerable.Range(1, 20).Select(i => 100m + i)));
var aWeakRs = StockScanner.DebugScan("A.TW", "A", aDaily, strongMarket,
    MarketMode.A_BullTrend, settings);
Check(!aWeakRs.Passed && aWeakRs.HardConditions.Any(c =>
    c.Key.Contains("相對大盤強度") && !c.Value),
    "A: weak relative strength is filtered");
var aExtendedDaily = aDaily.ToArray();
aExtendedDaily[^1] = aExtendedDaily[^1] with
    { Close = 120m, High = 120.2m, Low = 119.8m };
var aExtended = StockScanner.DebugScan("A.TW", "A", aExtendedDaily, aMarket,
    MarketMode.A_BullTrend, settings);
Check(!aExtended.Passed && aExtended.HardConditions.Any(c =>
    c.Key.Contains("MA10 不超過") && !c.Value),
    "A: price far above MA10 is filtered");
var a3Prices = aPrices.ToArray();
a3Prices[^1] = 104m;
var a3 = StockScanner.DebugScan("A.TW", "A", DailySeries(a3Prices), aMarket,
    MarketMode.A_BullTrend, settings);
Check(!a3.Passed && a3.HardConditions.Any(c => c.Key.Contains("高點") && !c.Value),
    "A3: no recent high proximity excludes stock");

var bPrices = Enumerable.Range(0, 117).Select(i => 130m - i * (30m / 116))
    .Concat(Enumerable.Range(1, 10).Select(i => 100m + i))
    .Concat(new[] { 108m, 106m, 103.4m }).ToArray();
var bDaily = DailySeries(bPrices);
for (int index = bDaily.Count - 3; index < bDaily.Count; index++)
    bDaily[index] = bDaily[index] with { Volume = 600 };
var bMarket = DailySeries(Enumerable.Repeat(100m, bDaily.Count));
var b1 = StockScanner.DebugScan("B.TW", "B", bDaily, bMarket, MarketMode.B_BullRange, settings);
Check(b1.Passed && b1.Close < b1.Ma10 && b1.PullbackDays == 3 && b1.PriorStrength,
    "B1: strong 3-day pullback below MA10 qualifies");
var b2 = StockScanner.DebugScan("B.TW", "B",
    DailySeries(Enumerable.Repeat(100m, 127).Concat(new[] { 98m, 95m, 92m })),
    bMarket, MarketMode.B_BullRange, settings);
Check(!b2.Passed && !b2.PriorStrength, "B2: weak prior trend excludes pullback");
var b3Daily = bDaily.ToArray();
b3Daily[^1] = b3Daily[^1] with { Close = 93.5m, Low = 93.3m, High = 93.7m };
var b3 = StockScanner.DebugScan("B.TW", "B", b3Daily, bMarket,
    MarketMode.B_BullRange, settings);
Check(!b3.Passed && b3.PullbackPercent > settings.PullbackMaxPercent,
    "B3: 15 percent pullback exceeds healthy range");
Check(Ta.Sma(bDaily,100) < Ta.Sma(bDaily,100,1) && b1.Passed,
    "B4: falling MA100 cannot exclude healthy pullback");
var flatHourly = DailySeries(Enumerable.Repeat(100m, 80));
var b5Entry = StockScanner.CheckEntry60m(flatHourly, MarketMode.B_BullRange, settings);
Check(b1.Candidate is not null && !b5Entry.IsMatch &&
    b5Entry.UnmetConditions.Any(c => c.Contains("MACD")),
    "B5: daily candidate remains while hourly MACD waits");
var oldDefaults = JsonSerializer.Deserialize<StrategySettings>(
    "{\"PullbackMinPercent\":5,\"PullbackMaxPercent\":12,\"BreakoutVolumeMultiple\":1.3}")!;
oldDefaults.MigrateLegacyScannerDefaults();
Check(oldDefaults.PullbackMinPercent == 3 && oldDefaults.PullbackMaxPercent == 10 &&
    oldDefaults.BreakoutStrongVolumeRatio == 1.3,
    "legacy A/B settings migrate without losing custom volume ratio");
Console.WriteLine("Short-cycle A/B scanner checks passed");

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
var hourlyDate = new DateTime(2026, 9, 29);
var hourly = new[]
{
    new Candle(hourlyDate.AddHours(12), 1, 1, 1, 1, 1),
    new Candle(hourlyDate.AddHours(13).AddMinutes(30), 2, 2, 2, 2, 1),
    new Candle(hourlyDate.AddHours(14), 3, 3, 3, 3, 1),
    new Candle(hourlyDate.AddDays(1), 4, 4, 4, 4, 1)
};
Check(HistoricalCandles.HourlyAtClose(hourly, hourlyDate).Count == 2,
    "hourly candles after close excluded");
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
await provider.Get60MinuteForHistoryAsync("TEST.TW", earliest);
Check(remote.LastHourlyDays >= (DateTime.Today - earliest).Days + 30,
    "hourly history requests six months plus warmup");
Console.WriteLine("Historical cache checks passed");

var signalMarket = Candles(180);
var signalDay = signalMarket[170].Time.Date;
var historicalHourly = Enumerable.Range(0, 70).Select(i =>
    new Candle(signalDay.AddDays(-20).AddHours(i), 100, 100, 100, 100, 1)).ToList();
historicalHourly.Add(new Candle(signalDay.AddHours(9), 123, 123, 123, 123, 1));
historicalHourly.Add(new Candle(signalDay.AddHours(13), 124, 124, 124, 124, 1));
var universe = new[] { new StockInfo("TEST.TW", "Test", "TWSE") };
int seenDailyCount = 0;
var signalReport = HistoricalSignalScanner.ScanCore(signalMarket, universe,
    _ => signalMarket, _ => historicalHourly, settings,
    signalDay.AddDays(-1), signalDay.AddDays(1),
    (symbol, name, daily, marketAtDecision, modes, s) =>
    {
        Check(daily[^1].Time.Date == signalDay.AddDays(-1) &&
            marketAtDecision[^1].Time.Date == signalDay.AddDays(-1),
            "signal uses prior completed daily close");
        seenDailyCount++;
        return new StockCandidate { Symbol = symbol, Name = name, Mode = modes[0],
            MatchedStrategies = new[] { modes[0] }, Reason = "daily passed" };
    },
    (bars, mode, s) => bars[^1].Time.TimeOfDay == new TimeSpan(9, 0, 0));
Check(seenDailyCount == 1 && signalReport.Signals.Count == 1, "one completed-hour trigger");
Check(signalReport.Signals[0].TriggerTime == signalDay.AddHours(10) &&
    signalReport.Signals[0].TriggerPrice == 123, "trigger time and close price");
Check(HistoricalSignalScanner.BarCloseTime(signalDay.AddHours(13)) ==
    signalDay.AddHours(13).AddMinutes(30), "final session bar closes at 13:30");
var excluded = HistoricalSignalScanner.ScanCore(signalMarket, universe,
    _ => signalMarket, _ => historicalHourly, settings,
    signalDay.AddDays(-1), signalDay.AddDays(1),
    (symbol, name, daily, index, modes, s) => new StockCandidate
    { Symbol = symbol, Mode = modes[0], MatchedStrategies = new[] { modes[0] } },
    (bars, mode, s) => true, targetDates: new HashSet<DateTime> { signalDay.AddDays(-1) });
Check(excluded.Signals.Count == 0, "incremental scan skips previously analyzed dates");
Console.WriteLine("Historical trigger checks passed");

var signalPath = Path.Combine(Path.GetTempPath(), $"dct-signal-store-{Guid.NewGuid():N}.json");
var signalStore = new HistoricalSignalStore(signalPath);
var signalArchive = signalStore.Load(settings, universe.Select(s => s.Symbol));
signalArchive = signalStore.Merge(signalArchive, new[] { signalDay }, new[] { signalDay },
    signalReport.Signals);
signalStore.Save(signalArchive);
var reloaded = signalStore.Load(settings, universe.Select(s => s.Symbol));
Check(reloaded.CompletedDates.Contains(signalDay) && reloaded.Signals.Count == 1,
    "trigger records survive restart");
Check(reloaded.AnalyzedDates.Contains(signalDay), "analyzed date survives restart");
var nextDay = signalDay.AddDays(1);
var incremental = signalStore.Merge(reloaded, new[] { nextDay }, new[] { nextDay },
    Array.Empty<HistoricalSignal>());
Check(incremental.Signals.Count == 1 && incremental.CompletedDates.Count == 2,
    "new day preserves earlier trigger records");
var replaced = signalStore.Merge(incremental, new[] { signalDay }, new[] { signalDay },
    signalReport.Signals);
Check(replaced.Signals.Count == 1 && replaced.CompletedDates.Count == 2,
    "rechecking a day replaces its records without duplicates");
var partialDay = nextDay.AddDays(1);
var partial = signalStore.Merge(replaced, new[] { partialDay }, Array.Empty<DateTime>(),
    Array.Empty<HistoricalSignal>());
Check(partial.AnalyzedDates.Contains(partialDay) && !partial.CompletedDates.Contains(partialDay),
    "incomplete date is recorded as analyzed without claiming full coverage");
var changedSettings = new StrategySettings { DailyRsiPeriod = 20 };
Check(signalStore.Load(changedSettings, universe.Select(s => s.Symbol)).Signals.Count == 0,
    "strategy change invalidates stored triggers");
Check(signalStore.Load(settings, new[] { "OTHER.TW" }).CompletedDates.Count == 1,
    "universe changes preserve prior completed dates");
Console.WriteLine("Incremental signal store checks passed");

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
    public int LastHourlyDays { get; private set; }
    public Task<List<Candle>> GetDailyAsync(string symbol, int days = 260)
    {
        LastDailyDays = days;
        return Task.FromResult(Enumerable.Range(0, days).Select(i =>
            new Candle(DateTime.Today.AddDays(i - days + 1), 100, 100, 100, 100, 1)).ToList());
    }
    public Task<List<Candle>> Get60MinuteAsync(string symbol, int days = 30)
    {
        LastHourlyDays = days;
        return Task.FromResult(new List<Candle>());
    }
}
