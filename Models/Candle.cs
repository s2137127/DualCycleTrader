namespace DualCycleTrader.Models;

public sealed record Candle(
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);
