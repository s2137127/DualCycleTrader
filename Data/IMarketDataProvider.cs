using DualCycleTrader.Models;

namespace DualCycleTrader.Data;

public interface IMarketDataProvider
{
    Task<List<Candle>> GetDailyAsync(string symbol, int days = 260);
    Task<List<Candle>> Get60MinuteAsync(string symbol, int days = 30);
}
