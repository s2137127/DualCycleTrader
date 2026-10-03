using DualCycleTrader.Models;

namespace DualCycleTrader.Indicators;

public sealed record AdxDebugRow(DateTime Date, decimal High, decimal Low, decimal Close,
    double? Tr, double? PlusDm, double? MinusDm,
    double? SmoothedTr, double? SmoothedPlusDm, double? SmoothedMinusDm,
    double? PlusDi, double? MinusDi, double? Dx, double? Adx);

public static class Ta
{
    public static double? Sma(IReadOnlyList<Candle> x, int period, int offset = 0)
    {
        int end = x.Count - offset;
        int start = end - period;
        if (start < 0 || end <= 0) return null;
        return x.Skip(start).Take(period).Average(c => (double)c.Close);
    }

    public static double? AvgVolume(IReadOnlyList<Candle> x, int period, int offset = 0)
    {
        int end = x.Count - offset;
        int start = end - period;
        if (start < 0 || end <= 0) return null;
        return x.Skip(start).Take(period).Average(c => (double)c.Volume);
    }

    public static double? Rsi(IReadOnlyList<Candle> x, int period)
    {
        if (period <= 0 || x.Count < period + 1) return null;

        double avgGain = 0;
        double avgLoss = 0;

        // First RSI seed: simple average of the first N price changes.
        for (int i = 1; i <= period; i++)
        {
            double change = (double)(x[i].Close - x[i - 1].Close);
            if (change > 0) avgGain += change;
            else if (change < 0) avgLoss += -change;
        }

        avgGain /= period;
        avgLoss /= period;

        // Wilder smoothing for every following candle.
        for (int i = period + 1; i < x.Count; i++)
        {
            double change = (double)(x[i].Close - x[i - 1].Close);
            double gain = Math.Max(change, 0);
            double loss = Math.Max(-change, 0);

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0) return avgGain == 0 ? 50 : 100;
        if (avgGain == 0) return 0;

        double rs = avgGain / avgLoss;
        return 100 - 100 / (1 + rs);
    }

    public static (double dif, double dea, double hist)? Macd(IReadOnlyList<Candle> x, int fast, int slow, int signal)
    {
        if (x.Count < slow + signal) return null;
        var closes = x.Select(c => (double)c.Close).ToArray();
        var ef = Ema(closes, fast);
        var es = Ema(closes, slow);
        var dif = ef.Zip(es, (a,b) => a-b).ToArray();
        var dea = Ema(dif, signal);
        return (dif[^1], dea[^1], dif[^1] - dea[^1]);
    }

    public static (double k, double d, double j)? Kdj(IReadOnlyList<Candle> x, int n = 9)
    {
        if (x.Count < n + 2) return null;
        double k = 50, d = 50;
        foreach (int idx in Enumerable.Range(n - 1, x.Count - n + 1))
        {
            var w = x.Skip(idx - n + 1).Take(n).ToArray();
            double lo = (double)w.Min(c => c.Low);
            double hi = (double)w.Max(c => c.High);
            double rsv = hi == lo ? 50 : ((double)x[idx].Close - lo) / (hi - lo) * 100;
            k = 2.0/3*k + 1.0/3*rsv;
            d = 2.0/3*d + 1.0/3*k;
        }
        return (k, d, 3*k - 2*d);
    }

    public static (double adx, double plusDi, double minusDi)? Adx(IReadOnlyList<Candle> x, int period = 14)
    {
        var last = AdxSeries(x, period).LastOrDefault();
        return last is null || last.Adx is null
            ? null : (last.Adx.Value, last.PlusDi!.Value, last.MinusDi!.Value);
    }

    // Values are chronological. The first DI is at candle index period;
    // the first ADX is at index 2 * period - 1.
    public static IReadOnlyList<AdxDebugRow> DebugAdx(IReadOnlyList<Candle> candles, int period = 14)
        => AdxSeries(candles, period).TakeLast(20).ToArray();

    private static List<AdxDebugRow> AdxSeries(IReadOnlyList<Candle> x, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        if (x.Count == 0) return new();
        for (int i = 1; i < x.Count; i++)
            if (x[i].Time <= x[i - 1].Time)
                throw new ArgumentException("Candles must have unique timestamps in oldest-to-newest order.", nameof(x));

        var rows = new List<AdxDebugRow>(x.Count);
        double smoothedTr = 0, smoothedPlus = 0, smoothedMinus = 0;
        double dxSeed = 0, adx = 0;
        for (int i = 0; i < x.Count; i++)
        {
            var c = x[i];
            if (i == 0)
            {
                rows.Add(new(c.Time, c.High, c.Low, c.Close, null, null, null,
                    null, null, null, null, null, null, null));
                continue;
            }

            var previous = x[i - 1];
            double high = (double)c.High, low = (double)c.Low;
            double up = high - (double)previous.High;
            double down = (double)previous.Low - low;
            double tr = Math.Max(high - low, Math.Max(
                Math.Abs(high - (double)previous.Close), Math.Abs(low - (double)previous.Close)));
            double plus = up > down && up > 0 ? up : 0;
            double minus = down > up && down > 0 ? down : 0;
            if (i <= period)
            {
                smoothedTr += tr;
                smoothedPlus += plus;
                smoothedMinus += minus;
            }
            else
            {
                smoothedTr = smoothedTr - smoothedTr / period + tr;
                smoothedPlus = smoothedPlus - smoothedPlus / period + plus;
                smoothedMinus = smoothedMinus - smoothedMinus / period + minus;
            }

            if (i < period)
            {
                rows.Add(new(c.Time, c.High, c.Low, c.Close, tr, plus, minus,
                    null, null, null, null, null, null, null));
                continue;
            }

            double plusDi = smoothedTr == 0 ? 0 : 100 * smoothedPlus / smoothedTr;
            double minusDi = smoothedTr == 0 ? 0 : 100 * smoothedMinus / smoothedTr;
            double denominator = plusDi + minusDi;
            double dx = denominator == 0 ? 0 : 100 * Math.Abs(plusDi - minusDi) / denominator;
            double? currentAdx = null;
            if (i < 2 * period - 1) dxSeed += dx;
            else if (i == 2 * period - 1)
            {
                adx = (dxSeed + dx) / period;
                currentAdx = adx;
            }
            else
            {
                adx = (adx * (period - 1) + dx) / period;
                currentAdx = adx;
            }
            rows.Add(new(c.Time, c.High, c.Low, c.Close, tr, plus, minus,
                smoothedTr, smoothedPlus, smoothedMinus, plusDi, minusDi, dx, currentAdx));
        }
        return rows;
    }

    private static double[] Ema(double[] values, int period)
    {
        var r = new double[values.Length];
        double a = 2.0/(period+1);
        r[0]=values[0];
        for(int i=1;i<values.Length;i++) r[i]=a*values[i]+(1-a)*r[i-1];
        return r;
    }
}
