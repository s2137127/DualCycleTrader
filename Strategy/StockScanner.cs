using DualCycleTrader.Indicators;
using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class StockScanner
{
    public static StockCandidate? Scan(string symbol, string name, IReadOnlyList<Candle> x,
        IReadOnlyList<Candle> market, MarketMode mode, StrategySettings s)
    {
        if(x.Count<130 || market.Count<30 || mode==MarketMode.D_BearTrend) return null;
        if (mode is MarketMode.A_BullTrend or MarketMode.B_BullRange)
        {
            int needed=mode==MarketMode.A_BullTrend ? s.BullishMaLongPeriod+20 : s.BullishMaMediumPeriod+5;
            if(x.Count < needed) return null;
            return ScanBullish(symbol, name, x, market, mode, s);
        }
        double close=(double)x[^1].Close;
        double ma20=Ta.Sma(x,20)!.Value, ma50=Ta.Sma(x,50)!.Value, ma100=Ta.Sma(x,100)!.Value;
        bool ma20up=ma20>Ta.Sma(x,20,5)!.Value, ma50up=ma50>Ta.Sma(x,50,5)!.Value;
        bool ma100up=ma100>Ta.Sma(x,100,20)!.Value;
        double rsi=Ta.Rsi(x,s.DailyRsiPeriod)??50;
        var macd=Ta.Macd(x,s.DailyMacdFast,s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
        var prevMacd=Ta.Macd(x.Take(x.Count-1).ToList(),s.DailyMacdFast,s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
        var kdj=Ta.Kdj(x,9)??(0,0,0);
        double avgVol=Ta.AvgVolume(x,20,1)??0;
        double vol=(double)x[^1].Volume;
        double high20=x.Take(x.Count-1).TakeLast(20).Max(c=>(double)c.High);
        double rs=RelativeStrength20(x,market);
        bool negativeHistShrinking = macd.hist < 0 && prevMacd.hist < 0 && macd.hist > prevMacd.hist;
        bool downDay = x[^1].Close < x[^2].Close;
        bool volumePattern = downDay ? vol < avgVol : vol > avgVol;

        bool ok=false; string reason="";
        if(mode==MarketMode.C_BearRange)
        {
            bool nearOrAbove50=close>=ma50 || Math.Abs(close-ma50)/ma50*100<=s.NearMaPercent;
            bool holdPriorLow=(double)x[^1].Low >= (double)x.Take(x.Count-1).TakeLast(20).Min(c=>c.Low);
            ok=rs>=s.RelativeStrengthMin && nearOrAbove50 && ma100>=Ta.Sma(x,100,20)!.Value &&
               rsi>45 && negativeHistShrinking && volumePattern && holdPriorLow;
            reason=$"相對強度+{rs:F1}%＋守MA50/前低";
        }
        else if(mode==MarketMode.E_Transition)
        {
            ok=close>ma20 && ma20>ma50 && ma50>ma100 && ma50up && ma100up &&
               rsi>55 && macd.dif>macd.dea && rs>0 && close>=high20*0.99;
            reason="過渡盤：僅保留極強日K候選";
        }

        if(!ok) return null;
        double previousClose=(double)x[^2].Close;
        double changePercent=previousClose==0 ? 0 : (close-previousClose)/previousClose*100;
        return new StockCandidate{Symbol=symbol,Name=name,Mode=mode,Close=x[^1].Close,
            ChangePercent=changePercent,Rsi14=rsi,RelativeStrength20=rs,
            DailyMacdDif=macd.dif,DailyMacdDea=macd.dea,
            DailyK=kdj.k,DailyD=kdj.d,DailyJ=kdj.j,Reason=reason};
    }

    private static StockCandidate? ScanBullish(string symbol, string name,
        IReadOnlyList<Candle> x, IReadOnlyList<Candle> market,
        MarketMode mode, StrategySettings s)
    {
        var shortCycle=ShortCycleConditions(x,s.Rsi5UpperLimit);
        if (!shortCycle.RedCandle || !shortCycle.Rsi5BelowLimit || !shortCycle.Rsi5Rising)
            return null;
        double close=(double)x[^1].Close;
        int shortPeriod=s.BullishMaShortPeriod, mediumPeriod=s.BullishMaMediumPeriod;
        double ma20=Ta.Sma(x,shortPeriod)!.Value, ma50=Ta.Sma(x,mediumPeriod)!.Value;
        double volume=(double)x[^1].Volume;
        double averageVolume;
        double relativeStrength;
        double rsi;
        (double dif,double dea,double hist) macd;
        string reason;

        if (mode==MarketMode.A_BullTrend)
        {
            int longPeriod=s.BullishMaLongPeriod;
            double ma100=Ta.Sma(x,longPeriod)!.Value;
            if (!(close>ma20 && ma20>ma50 && ma50>ma100 &&
                  ma20>Ta.Sma(x,shortPeriod,5) && ma50>Ta.Sma(x,mediumPeriod,5) &&
                  ma100>Ta.Sma(x,longPeriod,20))) return null;
            double high20=x.Take(x.Count-1).TakeLast(20).Max(c=>(double)c.High);
            if (close<high20*0.99) return null;
            relativeStrength=RelativeStrength20(x,market);
            if (relativeStrength<=0) return null;
            if (close>high20)
            {
                averageVolume=Ta.AvgVolume(x,20,1)??0;
                if (!(averageVolume>0 && volume>=averageVolume*s.BreakoutVolumeMultiple))
                    return null;
            }
            rsi=Ta.Rsi(x,s.DailyRsiPeriod)??50;
            if (rsi<=55) return null;
            macd=Ta.Macd(x,s.DailyMacdFast,s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
            if (macd.dif<=macd.dea) return null;
            reason=$"紅 K＋RSI(5) 低於 {s.Rsi5UpperLimit:0.##} 且上升＋20日高附近/突破＋相對大盤強{relativeStrength:F1}%";
        }
        else
        {
            if (!(close>ma20 && ma20>ma50 &&
                  ma20>Ta.Sma(x,shortPeriod,5) && ma50>Ta.Sma(x,mediumPeriod,5))) return null;
            double recentHigh=x.TakeLast(20).Max(c=>(double)c.High);
            double pullback=(recentHigh-close)/recentHigh*100;
            if (pullback<s.PullbackMinPercent || pullback>s.PullbackMaxPercent) return null;
            averageVolume=Ta.AvgVolume(x,20,1)??0;
            if (volume>=averageVolume) return null;
            rsi=Ta.Rsi(x,s.DailyRsiPeriod)??50;
            if (rsi<40 || rsi>55) return null;
            macd=Ta.Macd(x,s.DailyMacdFast,s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
            var previousMacd=Ta.Macd(x.Take(x.Count-1).ToList(),s.DailyMacdFast,
                s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
            if (!(macd.hist<0 && previousMacd.hist<0 && macd.hist>previousMacd.hist))
                return null;
            relativeStrength=RelativeStrength20(x,market);
            reason=$"紅 K＋RSI(5) 低於 {s.Rsi5UpperLimit:0.##} 且上升＋回檔{pullback:F1}%＋量縮＋MACD負柱縮短";
        }

        var kdj=Ta.Kdj(x,9)??(0,0,0);
        double previousClose=(double)x[^2].Close;
        return new StockCandidate{Symbol=symbol,Name=name,Mode=mode,Close=x[^1].Close,
            ChangePercent=previousClose==0?0:(close-previousClose)/previousClose*100,
            Rsi14=rsi,RelativeStrength20=relativeStrength,
            DailyMacdDif=macd.dif,DailyMacdDea=macd.dea,
            DailyK=kdj.k,DailyD=kdj.d,DailyJ=kdj.j,Reason=reason};
    }

    public static StockScanDebug DebugScan(string symbol, string name, IReadOnlyList<Candle> x,
        IReadOnlyList<Candle> market, MarketMode mode, StrategySettings s)
    {
        if (mode is not (MarketMode.A_BullTrend or MarketMode.B_BullRange))
            throw new ArgumentOutOfRangeException(nameof(mode), "單股日 K Debug 僅支援 A/B。");
        int requiredBars=Math.Max(130,mode==MarketMode.A_BullTrend ?
            s.BullishMaLongPeriod+20 : s.BullishMaMediumPeriod+5);
        if (x.Count < requiredBars || market.Count < 30)
            return new StockScanDebug { Symbol=symbol, Mode=mode,
                Date=x.Count>0?x[^1].Time:default,
                HardConditions=new Dictionary<string,bool> { [$"至少 {requiredBars} 根個股日 K 與 30 根大盤日 K"]=false },
                Details=$"資料不足：個股 {x.Count} 根、大盤 {market.Count} 根。" };

        double close=(double)x[^1].Close;
        int shortPeriod=s.BullishMaShortPeriod, mediumPeriod=s.BullishMaMediumPeriod,
            longPeriod=s.BullishMaLongPeriod;
        double ma10=Ta.Sma(x,10)!.Value, ma20=Ta.Sma(x,shortPeriod)!.Value;
        double ma50=Ta.Sma(x,mediumPeriod)!.Value, ma100=Ta.Sma(x,longPeriod)??0;
        double rsi=Ta.Rsi(x,s.DailyRsiPeriod)??50;
        var macd=Ta.Macd(x,s.DailyMacdFast,s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
        var priorMacd=Ta.Macd(x.Take(x.Count-1).ToArray(),s.DailyMacdFast,
            s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
        var shortCycle=ShortCycleConditions(x,s.Rsi5UpperLimit);
        double averageVolume=Ta.AvgVolume(x,20,1)??0;
        double volume=(double)x[^1].Volume;
        double relativeStrength=RelativeStrength20(x,market);
        var hard=new Dictionary<string,bool>();
        hard["當日收紅 K（收盤 > 開盤）"]=shortCycle.RedCandle;
        hard[$"RSI(5) < {s.Rsi5UpperLimit:0.##}"]=shortCycle.Rsi5BelowLimit;
        hard["今日 RSI(5) > 昨日 RSI(5)"]=shortCycle.Rsi5Rising;
        double recentHigh, breakoutDistance=0, pullback=0;
        if (mode==MarketMode.A_BullTrend)
        {
            recentHigh=x.Take(x.Count-1).TakeLast(20).Max(c=>(double)c.High);
            breakoutDistance=recentHigh>0?(recentHigh-close)/recentHigh*100:100;
            hard[$"股價 > MA{shortPeriod} > MA{mediumPeriod} > MA{longPeriod}"]=close>ma20 && ma20>ma50 && ma50>ma100;
            hard[$"MA{shortPeriod}、MA{mediumPeriod}、MA{longPeriod} 向上"]=ma20>Ta.Sma(x,shortPeriod,5) &&
                ma50>Ta.Sma(x,mediumPeriod,5) && ma100>Ta.Sma(x,longPeriod,20);
            hard["RSI > 55"]=rsi>55;
            hard["MACD DIF > DEA"]=macd.dif>macd.dea;
            hard["接近前 20 日高點 1% 內"]=close>=recentHigh*0.99;
            hard["20 日相對強度優於大盤"]=relativeStrength>0;
            hard[$"突破時量比至少 {s.BreakoutVolumeMultiple:0.##} 倍"]=
                close<=recentHigh || (averageVolume>0 && volume>=averageVolume*s.BreakoutVolumeMultiple);
        }
        else
        {
            recentHigh=x.TakeLast(20).Max(c=>(double)c.High);
            pullback=recentHigh>0?(recentHigh-close)/recentHigh*100:0;
            hard[$"股價 > MA{shortPeriod} > MA{mediumPeriod}"]=close>ma20 && ma20>ma50;
            hard[$"MA{shortPeriod}、MA{mediumPeriod} 向上"]=ma20>Ta.Sma(x,shortPeriod,5) && ma50>Ta.Sma(x,mediumPeriod,5);
            hard[$"前 20 日高點回檔 {s.PullbackMinPercent:0.##}～{s.PullbackMaxPercent:0.##}%"]=
                pullback>=s.PullbackMinPercent && pullback<=s.PullbackMaxPercent;
            hard["成交量低於前 20 日均量"]=volume<averageVolume;
            hard["RSI 介於 40～55"]=rsi>=40 && rsi<=55;
            hard["MACD 負柱縮短"]=macd.hist<0 && priorMacd.hist<0 && macd.hist>priorMacd.hist;
        }
        StockCandidate? candidate=Scan(symbol,name,x,market,mode,s);
        string details=string.Join("、",hard.Select(item=>$"{(item.Value?"✓":"✗")}{item.Key}"));
        return new StockScanDebug
        {
            Symbol=symbol,Mode=mode,Date=x[^1].Time,Close=close,
            Ma10=ma10,Ma20=ma20,Ma50=ma50,Ma100=ma100,
            Rsi14=rsi,DailyRsi6=Ta.Rsi(x,6)??50,
            MacdDif=macd.dif,MacdDea=macd.dea,
            Volume=volume,AverageVolume20=averageVolume,
            VolumeRatio=averageVolume>0?volume/averageVolume:0,
            RecentHigh=recentHigh,DistanceToBreakoutPercent=breakoutDistance,
            RelativeStrength20=relativeStrength,PullbackPercent=pullback,
            HardConditions=hard,Details=details,Candidate=candidate
        };
    }

    private static double RelativeStrength20(IReadOnlyList<Candle> s, IReadOnlyList<Candle> m)
    {
        if(s.Count<21||m.Count<21) return 0;
        double sr=(double)(s[^1].Close/s[^21].Close-1)*100;
        double mr=(double)(m[^1].Close/m[^21].Close-1)*100;
        return sr-mr;
    }

    private static (bool RedCandle, bool Rsi5BelowLimit, bool Rsi5Rising)
        ShortCycleConditions(IReadOnlyList<Candle> candles,double rsi5UpperLimit)
    {
        var todayRsi5=Ta.Rsi(candles,5);
        var yesterdayRsi5=Ta.Rsi(candles.Take(candles.Count-1).ToArray(),5);
        return (candles[^1].Close>candles[^1].Open,
            todayRsi5 is not null && todayRsi5 < rsi5UpperLimit,
            todayRsi5 is not null && yesterdayRsi5 is not null && todayRsi5>yesterdayRsi5);
    }
}
