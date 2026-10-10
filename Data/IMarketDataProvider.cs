using DualCycleTrader.Models;

namespace DualCycleTrader.Data;

public interface IMarketDataProvider
{
    Task<List<Candle>> GetDailyAsync(string symbol, int days = 260);
}
