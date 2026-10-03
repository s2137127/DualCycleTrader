using DualCycleTrader.Indicators;
using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public sealed record MarketSnapshot(
    MarketMode Mode, double Ma20, double Ma50, double Ma100,
    bool Ma20Up, bool Ma50Up, bool Ma100Up,
    double Adx, double PlusDi, double MinusDi, double Rsi,
    double Dif, double Dea, string DirectionState);

public static class MarketClassifier
{
    public static MarketSnapshot Analyze(IReadOnlyList<Candle> x, StrategySettings settings)
    {
        if (x.Count < 130) throw new InvalidOperationException("大盤日K資料不足。");
        double ma20=Ta.Sma(x,20)!.Value, ma50=Ta.Sma(x,50)!.Value, ma100=Ta.Sma(x,100)!.Value;
        bool up20=ma20 > Ta.Sma(x,20,5)!.Value;
        bool up50=ma50 > Ta.Sma(x,50,5)!.Value;
        bool up100=ma100 > Ta.Sma(x,100,20)!.Value;
        var adx=Ta.Adx(x,14) ?? throw new InvalidOperationException("ADX資料不足");
        var prevAdx=Ta.Adx(x.Take(x.Count-1).ToList(),14);
        bool adxRising=prevAdx is not null && adx.adx > prevAdx.Value.adx;
        double rsi=Ta.Rsi(x,settings.DailyRsiPeriod) ?? 50;
        var macd=Ta.Macd(x,settings.DailyMacdFast,settings.DailyMacdSlow,settings.DailyMacdSignal) ?? (0,0,0);
        double p=(double)x[^1].Close;

        bool bull = p > ma100 && up100 && up50 && (up20 || p >= ma20);
        bool correction = p > ma100 && up100 && (!up20 || p < ma20) && (rsi < 50 || macd.dif <= macd.dea);
        bool bear = p < ma100 && !up100 && !up50 && !up20;
        string direction = bull ? "多方" : correction ? "修正" : bear ? "空方" : "轉弱";

        bool trend = adx.adx > 25 && adxRising;
        bool range = adx.adx < 20;
        bool transition = adx.adx >= 20 && adx.adx <= 25;

        MarketMode mode;
        if (transition) mode = MarketMode.E_Transition;
        else if (trend && direction == "多方" && adx.plusDi > adx.minusDi) mode = MarketMode.A_BullTrend;
        else if (trend && direction == "空方" && adx.minusDi > adx.plusDi) mode = MarketMode.D_BearTrend;
        else if (range && (direction == "多方" || direction == "修正")) mode = MarketMode.B_BullRange;
        else if (range && (direction == "轉弱" || direction == "空方")) mode = MarketMode.C_BearRange;
        else mode = MarketMode.E_Transition;

        return new(mode,ma20,ma50,ma100,up20,up50,up100,adx.adx,adx.plusDi,adx.minusDi,rsi,macd.dif,macd.dea,direction);
    }
}
