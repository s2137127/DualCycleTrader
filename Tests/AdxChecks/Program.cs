using DualCycleTrader.Indicators;
using DualCycleTrader.Models;
using DualCycleTrader.Strategy;

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
Check(debug.Passed == debug.HardConditions.Values.All(value => value),
    "debug agrees with scanner");
var doji = candles.ToList();
doji[^1] = doji[^1] with { Open = doji[^1].Close };
Check(!StockScanner.DebugScan("X.TW", "X", doji, candles,
    MarketMode.B_BullRange, settings).HardConditions["當日收紅 K（收盤 > 開盤）"],
    "doji fails red-candle condition");
Console.WriteLine("B scanner checks passed");
