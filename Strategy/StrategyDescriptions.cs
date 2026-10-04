using DualCycleTrader.Models;

namespace DualCycleTrader.Strategy;

public static class StrategyDescriptions
{
    public static string For(MarketMode mode, StrategySettings s) => mode switch
    {
        MarketMode.A_BullTrend => $"""
            A 短線突破（確認多方結構下的候選策略）

            目的
            尋找未來約 3～5 個交易日可能延續強勢的突破或接近突破股票。

            日 K 主要條件
            • 股價 > MA20，MA20 不下降；MA10 向上及股價站上 MA10 加分
            • 接近或突破前 {s.BreakoutLookbackMin} 日高點（距離不超過 {s.NearBreakoutPercent:0.##}%）；前 {s.BreakoutLookbackMax} 日新高加分
            • RSI({s.DailyRsiPeriod}) > {s.BreakoutRsiMin:0.##}；達 {s.BreakoutRsiOverheated:0.##} 標記偏熱並扣分
            • MACD({s.DailyMacdFast},{s.DailyMacdSlow},{s.DailyMacdSignal}) DIF > DEA
            • 成交量比、相對大盤強度、MA50 趨勢作為評分；強量標準 {s.BreakoutStrongVolumeRatio:0.##} 倍，不是入選門檻
            • MA100 僅供觀察，不作為 A 入選門檻

            60 分鐘 K 確認
            • 突破前 20 根高點且成交量放大
            • RSI({s.IntradayRsiPeriod}) > 50
            • MACD({s.IntradayMacdFast},{s.IntradayMacdSlow},{s.IntradayMacdSignal}) DIF 上穿 DEA
            • KDJ 金叉

            避免追價
            當股價高於 60 分 MA20 超過 {s.OverextendedFrom60Ma20Percent:0.##}%，且 RSI({s.IntradayRsiPeriod}) > {s.OverboughtRsi6:0.##} 時不追價。
            """,

        MarketMode.B_BullRange => $"""
            B 短線回檔（確認多方結構下的候選策略）

            目的
            尋找前段強勢、近期健康回檔並等待 60 分 K 再轉強的股票。

            日 K 主要條件
            • 高點前 {s.PriorStrengthLookbackDays} 日至少上漲 {s.PriorStrengthMinGainPercent:0.##}%，且高點收盤在當時 MA20 之上
            • 最近 {s.PullbackHighLookbackDays} 日高點後回檔 {s.PullbackDaysMin}～{s.PullbackDaysMax} 日，幅度 {s.PullbackMinPercent:0.##}～{s.PullbackMaxPercent:0.##}%
            • MA20 不下降，股價未明顯跌破 MA20，距 MA10／MA20／前高支撐不超過 {s.SupportTolerancePercent:0.##}%
            • 整段回檔平均量低於此前 20 日平均量
            • RSI({s.DailyRsiPeriod}) 在 {s.PullbackRsi14Min:0.##}～{s.PullbackRsi14Max:0.##}、日 K RSI6 降溫、MACD 修正、MA50 作為加分
            • MA100 不作為 B 入選門檻

            60 分鐘 K 確認
            • 守住前低，或接近 MA20（±2%）
            • 不再破前一根低點
            • RSI({s.IntradayRsiPeriod}) 站上 50 且未轉弱
            • MACD({s.IntradayMacdFast},{s.IntradayMacdSlow},{s.IntradayMacdSignal}) 金叉、KDJ 金叉
            • 成交量高於前 20 根均量

            備註
            原始策略提及布林通道；目前選股程式未使用 BB 作為判斷條件。
            """,

        MarketMode.C_BearRange => $"""
            C 抗跌型（確認空方震盪後使用）

            目的
            尋找比大盤強、能守住支撐的股票，而不是尋找跌幅最深者。

            日 K 主要條件
            • 20 日相對強度 = 個股 20 日報酬率 − 大盤 20 日報酬率
            • 相對強度至少 +{s.RelativeStrengthMin:0.##}%
            • 股價在 MA50 附近（±{s.NearMaPercent:0.##}%）或以上
            • MA100 不低於 20 根前的 MA100
            • RSI({s.DailyRsiPeriod}) > 45
            • MACD 負柱縮短
            • 下跌量縮、上漲量增，且守住前 20 日低點

            範例
            大盤報酬 -8%、個股報酬 -2%，相對強度為 +6%。

            60 分鐘 K 確認
            • 支撐確認且不破前低
            • RSI({s.IntradayRsiPeriod}) > 50
            • MACD({s.IntradayMacdFast},{s.IntradayMacdSlow},{s.IntradayMacdSignal}) 金叉、KDJ 金叉
            • 成交量高於前 20 根均量
            """,

        MarketMode.D_BearTrend => $"""
            D 空方趨勢 / 停止一般多頭新進場

            典型環境
            • 大盤收盤低於 MA100
            • MA20、MA50、MA100 向下
            • ADX > 25 且持續上升
            • -DI > +DI

            操作
            • 不啟動一般多頭選股器
            • RSI 超跌、KDJ 金叉、MACD 金叉只視為反彈訊號
            • 不視為正式多頭買點
            • 今日單次判為 D 不會立即切換交易模式；需連續確認
            • 不會自動賣出既有持股

            本模式不產生個股候選名單。
            """,

        MarketMode.E_Transition => $"""
            E 過渡盤 / 觀察型（確認後使用）

            典型環境
            • ADX 介於 20～25，或趨勢／方向條件尚未完整成立
            • DI 或均線方向可能尚未一致

            操作
            • 降低交易數量，只保留少數強勢股
            • 日 K 須維持多頭排列、RSI({s.DailyRsiPeriod}) > 55、MACD({s.DailyMacdFast},{s.DailyMacdSlow},{s.DailyMacdSignal}) 偏多
            • 相對強度優於大盤，並接近前 20 日高點

            60 分鐘 K 必須全部確認
            • 收盤站上 MA20 且守住支撐
            • RSI({s.IntradayRsiPeriod}) > 50
            • MACD({s.IntradayMacdFast},{s.IntradayMacdSlow},{s.IntradayMacdSignal}) 金叉、KDJ 金叉
            • 成交量高於前 20 根均量

            否則不產生進場訊號。
            """,

        _ => "無策略說明。"
    };

    public static string ExitRules(StrategySettings s) => $"""
        【統一出場規則】

        1. 防守停損：跌破買進依據的支撐或前低時出場。
        2. 趨勢停利：60 分鐘 K 收盤確認跌破 MA20，且動能同步轉弱時出場。
        3. 移動停利：設定中的最高價回落參數為 {s.ProfitRetracePercent:0.##}%。

        注意：目前版本只有選股與進場訊號，尚未實作持倉追蹤及上述出場演算法；此處為原始策略規則與現有參數說明。
        """;
}
