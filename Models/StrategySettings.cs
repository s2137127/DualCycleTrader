namespace DualCycleTrader.Models;

public sealed class StrategySettings
{
    public int DailyRsiPeriod { get; set; } = 14;
    public int IntradayRsiPeriod { get; set; } = 6;
    public int DailyMacdFast { get; set; } = 12;
    public int DailyMacdSlow { get; set; } = 26;
    public int DailyMacdSignal { get; set; } = 9;
    public int IntradayMacdFast { get; set; } = 5;
    public int IntradayMacdSlow { get; set; } = 13;
    public int IntradayMacdSignal { get; set; } = 5;

    public int DailyRetentionBars { get; set; } = 500;
    public int IntradayRetentionDays { get; set; } = 120;
    public bool AutoCleanupCache { get; set; } = true;
    public int MarketRegimeConfirmationDays { get; set; } = 3;
    public bool EnableBullishDualScanner { get; set; } = true;
    public bool PersistMarketState { get; set; } = true;

    public double NearMaPercent { get; set; } = 3.0;
    public double BreakoutVolumeMultiple { get; set; } = 1.5;
    public double PullbackMinPercent { get; set; } = 5.0;
    public double PullbackMaxPercent { get; set; } = 12.0;
    public double RelativeStrengthMin { get; set; } = 5.0;
    public double ProfitRetracePercent { get; set; } = 30.0;

    // Strategy thresholds referenced by scanner/classifier
    public double OverboughtRsi6 { get; set; } = 80.0;
    public double OverextendedFrom60Ma20Percent { get; set; } = 8.0;
}
