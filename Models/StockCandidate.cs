namespace DualCycleTrader.Models;

public sealed class StockCandidate
{
    public string Symbol { get; init; } = "";
    public string Name { get; init; } = "";
    public MarketMode Mode { get; init; }
    public string StrategyType => Mode switch
    {
        MarketMode.A_BullTrend => "A 短線突破",
        MarketMode.B_BullRange => "B 短線回檔",
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
    public double CandidateScore { get; init; }
    public double DailyMacdDif { get; init; }
    public double DailyMacdDea { get; init; }
    public double DailyK { get; init; }
    public double DailyD { get; init; }
    public double DailyJ { get; init; }
    public string Reason { get; init; } = "";
}
