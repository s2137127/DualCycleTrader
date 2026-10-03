using DualCycleTrader.Models;

namespace Web.Services;

public interface IHistoricalDataStore
{
    Task<List<Candle>> GetAsync(string symbol,string timeframe,DateTime from,DateTime to);
    Task<DateTime?> GetLatestDateAsync(string symbol,string timeframe);
    Task<(int added,int updated,int skipped)> UpsertRangeAsync(string symbol,string timeframe,
        IReadOnlyList<Candle> candles,bool preserveExisting=false);
}
