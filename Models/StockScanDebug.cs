namespace DualCycleTrader.Models;

public sealed class StockScanDebug
{
    public string Symbol { get; init; } = "";
    public MarketMode Mode { get; init; }
    public DateTime Date { get; init; }
    public double Close { get; init; }
    public double Ma10 { get; init; }
    public double Ma20 { get; init; }
    public double Ma50 { get; init; }
    public double Ma100 { get; init; }
    public double Rsi14 { get; init; }
    public double DailyRsi6 { get; init; }
    public double MacdDif { get; init; }
    public double MacdDea { get; init; }
    public double Volume { get; init; }
    public double AverageVolume20 { get; init; }
    public double VolumeRatio { get; init; }
    public double RecentHigh { get; init; }
    public double DistanceToBreakoutPercent { get; init; }
    public double RelativeStrength20 { get; init; }
    public double PullbackPercent { get; init; }
    public int PullbackDays { get; init; }
    public bool PriorStrength { get; init; }
    public double PriorStrengthGainPercent { get; init; }
    public double Support { get; init; }
    public double SupportDistancePercent { get; init; }
    public double PullbackVolumeRatio { get; init; }
    public IReadOnlyDictionary<string, bool> HardConditions { get; init; } =
        new Dictionary<string, bool>();
    public double Score { get; init; }
    public string Details { get; init; } = "";
    public StockCandidate? Candidate { get; init; }
    public bool Passed => Candidate is not null;
}
