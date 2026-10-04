using DualCycleTrader.Indicators;
using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class StockScanner
{
    public sealed record EntryCheckResult(
        bool IsMatch,IReadOnlyList<string> UnmetConditions,
        double MacdDif,double MacdDea,double K,double D,double J);

    private static class ScoreWeight
    {
        public const double BreakoutProximity=20, BreakoutConfirmed=10, LongBreakout=10;
        public const double BreakoutVolume=15, BreakoutRelativeStrength=25;
        public const double ShortTrend=5, PriceAboveMa10=5, LongTrend=5, HealthyRsi=5;
        public const double OverheatedPenalty=15;
        public const double PriorStrength=20, HealthyPullback=20, Support=15;
        public const double VolumeContraction=15, RsiCooling=10, ShortRsiCooling=5;
        public const double MacdCooling=10, PullbackRelativeStrength=5;
        public const double RelativeStrengthFullScorePercent=10;
    }

    public static StockCandidate? Scan(string symbol, string name, IReadOnlyList<Candle> x,
        IReadOnlyList<Candle> market, MarketMode mode, StrategySettings s)
    {
        if (mode is MarketMode.A_BullTrend or MarketMode.B_BullRange)
            return DebugScan(symbol, name, x, market, mode, s).Candidate;
        if(x.Count<130 || market.Count<30 || mode==MarketMode.D_BearTrend) return null;
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

    public static StockScanDebug DebugScan(string symbol, string name, IReadOnlyList<Candle> x,
        IReadOnlyList<Candle> market, MarketMode mode, StrategySettings s)
    {
        if (mode is not (MarketMode.A_BullTrend or MarketMode.B_BullRange))
            throw new ArgumentOutOfRangeException(nameof(mode), "單股日 K Debug 僅支援 A/B。");
        if (x.Count < 130 || market.Count < 30)
            return new StockScanDebug { Symbol=symbol, Mode=mode,
                Date=x.Count>0?x[^1].Time:default,
                HardConditions=new Dictionary<string,bool> { ["至少 130 根個股日 K 與 30 根大盤日 K"]=false },
                Details=$"資料不足：個股 {x.Count} 根、大盤 {market.Count} 根。" };

        double close=(double)x[^1].Close;
        double ma10=Ta.Sma(x,10)!.Value, ma20=Ta.Sma(x,20)!.Value;
        double ma50=Ta.Sma(x,50)!.Value, ma100=Ta.Sma(x,100)!.Value;
        double priorMa10=Ta.Sma(x,10,1)!.Value, priorMa20=Ta.Sma(x,20,1)!.Value;
        double priorMa50=Ta.Sma(x,50,1)!.Value;
        double rsi14=Ta.Rsi(x,s.DailyRsiPeriod)??50;
        double rsi6=Ta.Rsi(x,6)??50;
        var macd=Ta.Macd(x,s.DailyMacdFast,s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
        var previousMacd=Ta.Macd(x.Take(x.Count-1).ToArray(),s.DailyMacdFast,
            s.DailyMacdSlow,s.DailyMacdSignal)??(0,0,0);
        double avgVol=Ta.AvgVolume(x,20,1)??0, volume=(double)x[^1].Volume;
        double volumeRatio=avgVol>0?volume/avgVol:0;
        double relativeStrength=RelativeStrength20(x,market);
        var hard=new Dictionary<string,bool>();
        double score, recentHigh, breakoutDistance=0, pullback=0;
        double support=0, supportDistance=0, pullbackVolumeRatio=0, priorGain=0;
        int pullbackDays=0;
        bool priorStrength=false;
        string details;

        if (mode==MarketMode.A_BullTrend)
        {
            int shortLookback=Math.Clamp(s.BreakoutLookbackMin,10,60);
            int longLookback=Math.Clamp(s.BreakoutLookbackMax,shortLookback,60);
            recentHigh=x.Skip(x.Count-1-shortLookback).Take(shortLookback)
                .Max(c=>(double)c.High);
            double longHigh=x.Skip(x.Count-1-longLookback).Take(longLookback)
                .Max(c=>(double)c.High);
            breakoutDistance=recentHigh>0?(recentHigh-close)/recentHigh*100:100;
            hard["股價高於 MA20"]=close>ma20;
            hard["MA20 未下降"]=ma20>=priorMa20;
            hard[$"接近或突破前 {shortLookback} 日高點"]=breakoutDistance<=s.NearBreakoutPercent;
            hard[$"日 K RSI({s.DailyRsiPeriod}) > {s.BreakoutRsiMin:0.#}"]=rsi14>s.BreakoutRsiMin;
            hard["日 K MACD DIF > DEA"]=macd.dif>macd.dea;
            bool breakout=close>=recentHigh;
            bool overheated=rsi14>=s.BreakoutRsiOverheated;
            score=ScoreWeight.BreakoutProximity+
                (breakout?ScoreWeight.BreakoutConfirmed:0)+
                (close>=longHigh?ScoreWeight.LongBreakout:0)+
                Math.Clamp(volumeRatio/Math.Max(s.BreakoutStrongVolumeRatio,0.01),0,1)*ScoreWeight.BreakoutVolume+
                Math.Clamp(relativeStrength/ScoreWeight.RelativeStrengthFullScorePercent,0,1)*
                    ScoreWeight.BreakoutRelativeStrength+
                (ma10>=priorMa10?ScoreWeight.ShortTrend:0)+
                (close>ma10?ScoreWeight.PriceAboveMa10:0)+
                (close>ma50 && ma50>=priorMa50?ScoreWeight.LongTrend:0)+
                (!overheated?ScoreWeight.HealthyRsi:-ScoreWeight.OverheatedPenalty);
            details=$"股價 {close:F2} / MA10 {ma10:F2} / MA20 {ma20:F2} / MA50 {ma50:F2} / MA100 {ma100:F2}；"+
                $"前{shortLookback}日高 {recentHigh:F2}，距突破 {breakoutDistance:F1}%（{(breakout?"已突破":"Near Breakout")}）；"+
                $"量比 {volumeRatio:F2}（{(volumeRatio>=s.BreakoutStrongVolumeRatio?"強量":"量能評分")}）；"+
                $"RSI14 {rsi14:F1}{(overheated?"，日K RSI偏熱":"")}；"+
                $"MACD DIF {macd.dif:F3} / DEA {macd.dea:F3}；相對強度 {relativeStrength:+0.0;-0.0;0.0}%；"+
                $"MA10 {(ma10>=priorMa10?"↑":"↓")} / MA20 {(ma20>=priorMa20?"↑":"↓")}；分數 {score:F1}";
        }
        else
        {
            int highLookback=Math.Clamp(s.PullbackHighLookbackDays,
                Math.Max(6,s.PullbackDaysMax+1),60);
            int peakIndex=x.Count-highLookback;
            for (int i=peakIndex+1;i<x.Count;i++)
                if (x[i].High>=x[peakIndex].High) peakIndex=i;
            recentHigh=(double)x[peakIndex].High;
            pullbackDays=x.Count-1-peakIndex;
            pullback=recentHigh>0?(recentHigh-close)/recentHigh*100:0;
            int priorLookback=Math.Clamp(s.PriorStrengthLookbackDays,2,60);
            int priorStart=peakIndex-priorLookback;
            if (priorStart>=0 && x[priorStart].Close>0)
            {
                priorGain=(double)(x[peakIndex].Close/x[priorStart].Close-1)*100;
                double peakMa20=x.Skip(peakIndex-19).Take(20).Average(c=>(double)c.Close);
                priorStrength=priorGain>=s.PriorStrengthMinGainPercent &&
                    (double)x[peakIndex].Close>peakMa20;
            }
            double priorResistance=x.Skip(Math.Max(0,peakIndex-priorLookback))
                .Take(Math.Min(priorLookback,peakIndex)).DefaultIfEmpty(x[peakIndex])
                .Max(c=>(double)c.High);
            var supports=new[] { ma10,ma20,priorResistance }.Where(v=>v>0).ToArray();
            support=supports.MinBy(v=>Math.Abs(close-v)/v);
            supportDistance=Math.Abs(close-support)/support*100;
            double advanceVol=pullbackDays>0?Ta.AvgVolume(x,20,pullbackDays)??0:0;
            double pullbackVol=pullbackDays>0?x.Skip(peakIndex+1)
                .Average(c=>(double)c.Volume):0;
            pullbackVolumeRatio=advanceVol>0?pullbackVol/advanceVol:double.PositiveInfinity;
            double peakRsi6=Ta.Rsi(x.Take(peakIndex+1).ToArray(),6)??50;
            bool rsiCooling=rsi6<peakRsi6;
            bool macdCooling=(macd.hist<0 && macd.hist>previousMacd.hist) ||
                (macd.hist>=0 && macd.hist<previousMacd.hist);
            hard["前段強勢"]=priorStrength;
            hard[$"回檔 {s.PullbackDaysMin}～{s.PullbackDaysMax} 個交易日"]=
                pullbackDays>=s.PullbackDaysMin && pullbackDays<=s.PullbackDaysMax;
            hard[$"回檔 {s.PullbackMinPercent:0.#}～{s.PullbackMaxPercent:0.#}%"]=
                pullback>=s.PullbackMinPercent && pullback<=s.PullbackMaxPercent;
            hard["MA20 未下降"]=ma20>=priorMa20;
            hard["MA20 支撐未明顯跌破"]=close>=ma20*(1-s.SupportTolerancePercent/100);
            hard[$"接近短線支撐 ±{s.SupportTolerancePercent:0.#}%"]=
                supportDistance<=s.SupportTolerancePercent;
            hard["整段回檔量縮"]=pullbackVolumeRatio<1;
            double healthyCenter=(s.PullbackMinPercent+s.PullbackMaxPercent)/2;
            double healthyHalf=Math.Max((s.PullbackMaxPercent-s.PullbackMinPercent)/2,0.01);
            score=(priorStrength?ScoreWeight.PriorStrength:0)+
                Math.Clamp(1-Math.Abs(pullback-healthyCenter)/healthyHalf,0,1)*ScoreWeight.HealthyPullback+
                Math.Clamp(1-supportDistance/Math.Max(s.SupportTolerancePercent,0.01),0,1)*ScoreWeight.Support+
                Math.Clamp(1-pullbackVolumeRatio,0,1)*ScoreWeight.VolumeContraction+
                (rsi14>=s.PullbackRsi14Min && rsi14<=s.PullbackRsi14Max?ScoreWeight.RsiCooling:0)+
                (rsiCooling?ScoreWeight.ShortRsiCooling:0)+
                (macdCooling?ScoreWeight.MacdCooling:0)+
                (relativeStrength>0?ScoreWeight.PullbackRelativeStrength:0)+
                (close>ma50 && ma50>=priorMa50?ScoreWeight.LongTrend:0);
            details=$"前段強勢 {(priorStrength?"PASS":"FAIL")}（前{priorLookback}日漲 {priorGain:F1}%）；"+
                $"前高 {recentHigh:F2}，回檔 {pullback:F1}% / {pullbackDays} 日；"+
                $"MA10 {ma10:F2} / MA20 {ma20:F2} / MA50 {ma50:F2} / MA100 {ma100:F2}；"+
                $"支撐 {support:F2}（距離 {supportDistance:F1}%）；整段回檔量比 {pullbackVolumeRatio:F2}；"+
                $"RSI14 {rsi14:F1} / 日K RSI6 {rsi6:F1}{(rsiCooling?" 降溫":"")}；"+
                $"MACD {(macdCooling?"動能修正":"未呈修正")} DIF {macd.dif:F3} / DEA {macd.dea:F3}；"+
                $"相對強度 {relativeStrength:+0.0;-0.0;0.0}%；分數 {score:F1}";
        }

        bool passed=hard.Values.All(value=>value);
        string conditionDetails=string.Join("、",hard.Select(item=>$"{(item.Value?"✓":"✗")}{item.Key}"));
        details=$"{conditionDetails}；{details}";
        StockCandidate? candidate=null;
        if (passed)
        {
            var kdj=Ta.Kdj(x,9)??(0,0,0);
            double previousClose=(double)x[^2].Close;
            candidate=new StockCandidate
            {
                Symbol=symbol,Name=name,Mode=mode,Close=x[^1].Close,
                ChangePercent=previousClose==0?0:(close-previousClose)/previousClose*100,
                Rsi14=rsi14,RelativeStrength20=relativeStrength,CandidateScore=score,
                DailyMacdDif=macd.dif,DailyMacdDea=macd.dea,
                DailyK=kdj.k,DailyD=kdj.d,DailyJ=kdj.j,Reason=details
            };
        }
        return new StockScanDebug
        {
            Symbol=symbol,Mode=mode,Date=x[^1].Time,Close=close,
            Ma10=ma10,Ma20=ma20,Ma50=ma50,Ma100=ma100,
            Rsi14=rsi14,DailyRsi6=rsi6,MacdDif=macd.dif,MacdDea=macd.dea,
            Volume=volume,AverageVolume20=avgVol,VolumeRatio=volumeRatio,
            RecentHigh=recentHigh,DistanceToBreakoutPercent=breakoutDistance,
            RelativeStrength20=relativeStrength,PullbackPercent=pullback,
            PullbackDays=pullbackDays,PriorStrength=priorStrength,
            PriorStrengthGainPercent=priorGain,Support=support,
            SupportDistancePercent=supportDistance,PullbackVolumeRatio=pullbackVolumeRatio,
            HardConditions=hard,Score=score,Details=details,Candidate=candidate
        };
    }

    public static bool Entry60m(IReadOnlyList<Candle> x, MarketMode mode, StrategySettings? settings=null)
        => CheckEntry60m(x,mode,settings).IsMatch;

    public static EntryCheckResult CheckEntry60m(IReadOnlyList<Candle> x, MarketMode mode, StrategySettings? settings=null)
    {
        settings ??= new StrategySettings();
        if(x.Count<70)
            return new(false,new[]{ $"60 分 K 資料不足：目前 {x.Count} 根，至少需要 70 根" },
                double.NaN,double.NaN,double.NaN,double.NaN,double.NaN);
        double close=(double)x[^1].Close;
        double rsi=Ta.Rsi(x,settings.IntradayRsiPeriod)??0;
        double prevRsi=Ta.Rsi(x.Take(x.Count-1).ToList(),settings.IntradayRsiPeriod)??0;
        var macd=Ta.Macd(x,settings.IntradayMacdFast,settings.IntradayMacdSlow,settings.IntradayMacdSignal)??(0,0,0);
        var prevMacd=Ta.Macd(x.Take(x.Count-1).ToList(),settings.IntradayMacdFast,settings.IntradayMacdSlow,settings.IntradayMacdSignal)??(0,0,0);
        var kdj=Ta.Kdj(x,9)??(0,0,0);
        var prevKdj=Ta.Kdj(x.Take(x.Count-1).ToList(),9)??(0,0,0);
        double avgVol=Ta.AvgVolume(x,20,1)??0;
        bool volUp=(double)x[^1].Volume>avgVol;
        bool kCross=prevKdj.k<=prevKdj.d && kdj.k>kdj.d;
        bool macdCross=prevMacd.dif<=prevMacd.dea && macd.dif>macd.dea;
        double ma20=Ta.Sma(x,20)??0;
        double prevLow=(double)x.Take(x.Count-1).TakeLast(20).Min(c=>c.Low);
        bool supportHeld=(double)x[^1].Low>=prevLow || Math.Abs(close-ma20)/ma20*100<=2.0;
        bool noNewLow=(double)x[^1].Low >= (double)x[^2].Low;

        var unmet=new List<string>();
        void Require(bool condition,string message){if(!condition) unmet.Add(message);}

        if(mode==MarketMode.A_BullTrend)
        {
            double prevHigh=x.Take(x.Count-1).TakeLast(20).Max(c=>(double)c.High);
            double distance=ma20==0?0:(close-ma20)/ma20*100;
            bool dontChase = distance>settings.OverextendedFrom60Ma20Percent && rsi>settings.OverboughtRsi6;
            Require(!dontChase,$"避免追價：距 MA20 {distance:F1}% 且 RSI6 {rsi:F1}");
            Require(close>prevHigh,$"尚未突破前 20 根高點 {prevHigh:F2}（目前 {close:F2}）");
            Require(volUp,"成交量尚未高於前 20 根均量");
            Require(rsi>50,$"RSI6 尚未站上 50（目前 {rsi:F1}）");
            Require(macdCross,$"MACD({settings.IntradayMacdFast},{settings.IntradayMacdSlow},{settings.IntradayMacdSignal}) 尚未出現 DIF 上穿 DEA");
            Require(kCross,"KDJ 尚未出現 K 上穿 D");
        }
        else if(mode==MarketMode.B_BullRange)
        {
            Require(supportHeld,"尚未確認守住前低或回到 MA20 附近");
            Require(noNewLow,"本根 60 分 K 仍創前一根新低");
            Require(rsi>50,$"RSI6 尚未站上 50（目前 {rsi:F1}）");
            Require(rsi>=prevRsi,$"RSI6 仍在下降（前值 {prevRsi:F1}，目前 {rsi:F1}）");
            Require(macdCross,$"MACD({settings.IntradayMacdFast},{settings.IntradayMacdSlow},{settings.IntradayMacdSignal}) 尚未金叉");
            Require(kCross,"KDJ 尚未金叉");
            Require(volUp,"成交量尚未高於前 20 根均量");
        }
        else if(mode==MarketMode.C_BearRange)
        {
            Require(supportHeld,"尚未確認守住前低或回到 MA20 附近");
            Require(noNewLow,"本根 60 分 K 仍創前一根新低");
            Require(rsi>50,$"RSI6 尚未站上 50（目前 {rsi:F1}）");
            Require(macdCross,$"MACD({settings.IntradayMacdFast},{settings.IntradayMacdSlow},{settings.IntradayMacdSignal}) 尚未金叉");
            Require(kCross,"KDJ 尚未金叉");
            Require(volUp,"成交量尚未高於前 20 根均量");
        }
        else if(mode==MarketMode.E_Transition)
        {
            Require(close>ma20,$"尚未站上 60 分 MA20（收盤 {close:F2}，MA20 {ma20:F2}）");
            Require(supportHeld,"尚未確認守住前低或 MA20 支撐");
            Require(rsi>50,$"RSI6 尚未站上 50（目前 {rsi:F1}）");
            Require(macdCross,$"MACD({settings.IntradayMacdFast},{settings.IntradayMacdSlow},{settings.IntradayMacdSignal}) 尚未金叉");
            Require(kCross,"KDJ 尚未金叉");
            Require(volUp,"成交量尚未高於前 20 根均量");
        }
        else unmet.Add("目前市場模式不啟動 60 分鐘多頭進場訊號");

        return new(unmet.Count==0,unmet,macd.dif,macd.dea,kdj.k,kdj.d,kdj.j);
    }

    private static double RelativeStrength20(IReadOnlyList<Candle> s, IReadOnlyList<Candle> m)
    {
        if(s.Count<21||m.Count<21) return 0;
        double sr=(double)(s[^1].Close/s[^21].Close-1)*100;
        double mr=(double)(m[^1].Close/m[^21].Close-1)*100;
        return sr-mr;
    }
}
