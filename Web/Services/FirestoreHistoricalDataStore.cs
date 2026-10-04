using System.Text.Json;
using DualCycleTrader.Models;
using Microsoft.JSInterop;

namespace Web.Services;

public sealed class FirestoreHistoricalDataStore(IJSRuntime js) : IHistoricalDataStore
{
    private static readonly JsonSerializerOptions JsonOptions=new()
    {PropertyNameCaseInsensitive=true,Converters={new TaiwanDateTimeConverter()}};

    public async Task<List<Candle>> GetAsync(string symbol,string timeframe,DateTime from,DateTime to)
    {
        string json=await js.InvokeAsync<string>("stockApp.getCandles",symbol,timeframe,
            from.ToString(timeframe=="D"?"yyyy":"yyyyMM"),
            to.ToString(timeframe=="D"?"yyyy":"yyyyMM"));
        return (JsonSerializer.Deserialize<List<Candle>>(json,JsonOptions) ?? new())
            .Where(c=>c.Time>=from && c.Time<=to).OrderBy(c=>c.Time).ToList();
    }

    public async Task<List<List<Candle>>> GetManyAsync(IReadOnlyList<string> symbols,string timeframe,
        DateTime from,DateTime to)
    {
        if(symbols.Count==0) return new();
        string json=await js.InvokeAsync<string>("stockApp.getCandlesBatch",symbols,timeframe,
            from.ToString(timeframe=="D"?"yyyy":"yyyyMM"),
            to.ToString(timeframe=="D"?"yyyy":"yyyyMM"));
        var result=(JsonSerializer.Deserialize<List<List<Candle>>>(json,JsonOptions) ?? new())
            .Select(bars=>bars.Where(c=>c.Time>=from && c.Time<=to).OrderBy(c=>c.Time).ToList()).ToList();
        if(result.Count!=symbols.Count) throw new InvalidDataException("批次行情數量與股票數量不符。");
        return result;
    }

    public async Task<DateTime?> GetLatestDateAsync(string symbol,string timeframe)
    {
        string? value=await js.InvokeAsync<string?>("stockApp.getLatest",symbol,timeframe);
        return DateTime.TryParse(value,out var date)?date:null;
    }

    public async Task<(int added,int updated,int skipped)> UpsertRangeAsync(string symbol,string timeframe,
        IReadOnlyList<Candle> candles,bool preserveExisting=false)
    {
        var result=await js.InvokeAsync<UpsertResult>("stockApp.upsertCandles",symbol,timeframe,
            JsonSerializer.Serialize(candles,JsonOptions),preserveExisting);
        return (result.Added,result.Updated,result.Skipped);
    }

    private sealed record UpsertResult(int Added,int Updated,int Skipped);
}
