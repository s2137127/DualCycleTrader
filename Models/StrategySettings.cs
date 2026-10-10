namespace DualCycleTrader.Models;

public sealed class StrategySettings
{
    public int DailyRsiPeriod { get; set; } = 14;
    public int DailyMacdFast { get; set; } = 12;
    public int DailyMacdSlow { get; set; } = 26;
    public int DailyMacdSignal { get; set; } = 9;
    public double Rsi5UpperLimit { get; set; } = 60.0;

    public int DailyRetentionBars { get; set; } = 500;
    public bool AutoCleanupCache { get; set; } = true;
    public int MarketRegimeConfirmationDays { get; set; } = 3;
    public bool EnableBullishDualScanner { get; set; } = true;
    public bool PersistMarketState { get; set; } = true;

    public double NearMaPercent { get; set; } = 3.0;
    // Keep the persisted legacy name so previously saved settings retain their value.
    public double BreakoutVolumeMultiple { get; set; } = 1.5;
    [System.Text.Json.Serialization.JsonIgnore]
    public double BreakoutStrongVolumeRatio
    { get => BreakoutVolumeMultiple; set => BreakoutVolumeMultiple = value; }
    public int BreakoutLookbackMin { get; set; } = 10;
    public int BreakoutLookbackMax { get; set; } = 20;
    public double NearBreakoutPercent { get; set; } = 2.0;
    public double BreakoutRsiMin { get; set; } = 55.0;
    public double BreakoutRsiOverheated { get; set; } = 75.0;
    public double BreakoutRelativeStrengthMin { get; set; } = 3.0;
    public double BreakoutMaxAboveMa10Percent { get; set; } = 8.0;
    public double PullbackMinPercent { get; set; } = 5.0;
    public double PullbackMaxPercent { get; set; } = 12.0;
    public int PullbackHighLookbackDays { get; set; } = 10;
    public int PullbackDaysMin { get; set; } = 2;
    public int PullbackDaysMax { get; set; } = 5;
    public int PriorStrengthLookbackDays { get; set; } = 10;
    public double PriorStrengthMinGainPercent { get; set; } = 3.0;
    public double PullbackRsi14Min { get; set; } = 40.0;
    public double PullbackRsi14Max { get; set; } = 60.0;
    public double SupportTolerancePercent { get; set; } = 2.0;
    public int ABScannerVersion { get; set; } = 0;

    public void MigrateLegacyScannerDefaults()
    {
        if (ABScannerVersion >= 3) return;
        if (ABScannerVersion > 0 && PullbackMinPercent == 3.0 && PullbackMaxPercent == 10.0)
        { PullbackMinPercent = 5.0; PullbackMaxPercent = 12.0; }
        ABScannerVersion = 3;
    }
    public double RelativeStrengthMin { get; set; } = 5.0;
    public double ProfitRetracePercent { get; set; } = 30.0;

    // Strategy thresholds referenced by scanner/classifier
}
