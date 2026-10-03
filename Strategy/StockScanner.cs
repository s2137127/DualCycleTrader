using DualCycleTrader.Indicators;
using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class StockScanner
{
    public sealed record EntryCheckResult(
        bool IsMatch,IReadOnlyList<string> UnmetConditions,
        double MacdDif,double MacdDea,double K,double D,double J);

    public static StockCandidate? Scan(string symbol, string name, IReadOnlyList<Candle> x,
        IReadOnlyList<Candle> market, MarketMode mode, StrategySettings s)
    {
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
        if(mode==MarketMode.A_BullTrend)
        {
            bool breakoutOrNear = close >= high20 * 0.99;
            ok=close>ma20 && ma20>ma50 && ma50>ma100 && ma20up && ma50up && ma100up &&
               rsi>55 && macd.dif>macd.dea && breakoutOrNear && rs>0 &&
               (! (close>high20) || (avgVol>0 && vol>=avgVol*s.BreakoutVolumeMultiple));
            reason=$"20日高附近/突破＋相對大盤強{rs:F1}%";
        }
        else if(mode==MarketMode.B_BullRange)
        {
            double recentHigh=x.TakeLast(20).Max(c=>(double)c.High);
            double pullback=(recentHigh-close)/recentHigh*100;
            ok=close>ma50 && ma50>ma100 && ma50up && ma100up &&
               pullback>=s.PullbackMinPercent && pullback<=s.PullbackMaxPercent &&
               vol<avgVol && rsi>=40 && rsi<=55 && negativeHistShrinking;
            reason=$"回檔{pullback:F1}%＋量縮＋MACD負柱縮短";
        }
        else if(mode==MarketMode.C_BearRange)
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
