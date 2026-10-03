namespace DualCycleTrader.Models;

public sealed class StockCandidate
{
    public string Symbol { get; init; } = "";
    public string Name { get; init; } = "";
    public MarketMode Mode { get; init; }
    public string StrategyType => Mode switch
    {
        MarketMode.A_BullTrend => "A 突破型",
        MarketMode.B_BullRange => "B 回檔型",
        MarketMode.C_BearRange => "C 抗跌型",
        MarketMode.E_Transition => "E 觀察型",
        _ => "無"
    };
    public IReadOnlyList<MarketMode> MatchedStrategies { get; init; } = Array.Empty<MarketMode>();
    public string DailyConditions => Reason;
    public decimal Close { get; init; }
    public double ChangePercent { get; init; }
    public double Rsi14 { get; init; }
    public double RelativeStrength20 { get; init; }
    public double DailyMacdDif { get; init; }
    public double DailyMacdDea { get; init; }
    public double DailyK { get; init; }
    public double DailyD { get; init; }
    public double DailyJ { get; init; }
    public string Reason { get; init; } = "";
    public bool Entry60m { get; init; }
}
