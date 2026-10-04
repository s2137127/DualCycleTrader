using System.Text.Json;
using DualCycleTrader.Models;
using Microsoft.JSInterop;

namespace Web.Services;

public sealed class FirestoreHistoricalDataStore(IJSRuntime js) : IHistoricalDataStore
{
    private static readonly JsonSerializerOptions JsonOptions=new()
    {PropertyNameCaseInsensitive=true,Converters={new TaiwanDateTimeConverter()}};
    private const int CacheLimit = 1600;
    private readonly Dictionary<CacheKey,(List<Candle> Bars,LinkedListNode<CacheKey> Node)> cache=new();
    private readonly LinkedList<CacheKey> cacheOrder=new();
    private readonly record struct CacheKey(string Symbol,string Timeframe,DateTime From,DateTime To);

    public void ClearCache()
    {
        cache.Clear();
        cacheOrder.Clear();
    }

    private bool TryGetCached(CacheKey key,out List<Candle> bars)
    {
        if(cache.TryGetValue(key,out var entry))
        {
            cacheOrder.Remove(entry.Node);
            cacheOrder.AddLast(entry.Node);
            bars=entry.Bars;
            return true;
        }
        bars=null!;
        return false;
    }

    private void Remember(CacheKey key,List<Candle> bars)
    {
        if(cache.TryGetValue(key,out var old)) cacheOrder.Remove(old.Node);
        var node=cacheOrder.AddLast(key);
        cache[key]=(bars,node);
        if(cache.Count<=CacheLimit) return;
        var oldest=cacheOrder.First!;
        cache.Remove(oldest.Value);
        cacheOrder.RemoveFirst();
    }

    private void Invalidate(string symbol,string timeframe)
    {
        foreach(var key in cache.Keys.Where(k=>k.Symbol==symbol && k.Timeframe==timeframe).ToArray())
        {
            cacheOrder.Remove(cache[key].Node);
            cache.Remove(key);
        }
    }

    public async Task<List<Candle>> GetAsync(string symbol,string timeframe,DateTime from,DateTime to)
    {
        var key=new CacheKey(symbol,timeframe,from,to);
        if(TryGetCached(key,out var cached)) return cached;
        string json=await js.InvokeAsync<string>("stockApp.getCandles",symbol,timeframe,
            from.ToString(timeframe=="D"?"yyyy":"yyyyMM"),
            to.ToString(timeframe=="D"?"yyyy":"yyyyMM"));
        var bars=(JsonSerializer.Deserialize<List<Candle>>(json,JsonOptions) ?? new())
            .Where(c=>c.Time>=from && c.Time<=to).OrderBy(c=>c.Time).ToList();
        Remember(key,bars);
        return bars;
    }

    public async Task<List<List<Candle>>> GetManyAsync(IReadOnlyList<string> symbols,string timeframe,
        DateTime from,DateTime to)
    {
        if(symbols.Count==0) return new();
        var missing=symbols.Where(symbol=>!TryGetCached(new(symbol,timeframe,from,to),out _))
            .Distinct().ToArray();
        if(missing.Length>0)
        {
            string json=await js.InvokeAsync<string>("stockApp.getCandlesBatch",missing,timeframe,
                from.ToString(timeframe=="D"?"yyyy":"yyyyMM"),
                to.ToString(timeframe=="D"?"yyyy":"yyyyMM"));
            var fetched=JsonSerializer.Deserialize<List<List<Candle>>>(json,JsonOptions) ?? new();
            if(fetched.Count!=missing.Length) throw new InvalidDataException("批次行情數量與股票數量不符。");
            for(int index=0;index<missing.Length;index++)
                Remember(new(missing[index],timeframe,from,to),fetched[index]
                    .Where(c=>c.Time>=from && c.Time<=to).OrderBy(c=>c.Time).ToList());
        }
        return symbols.Select(symbol=>cache[new(symbol,timeframe,from,to)].Bars).ToList();
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
        if(result.Added>0 || result.Updated>0) Invalidate(symbol,timeframe);
        return (result.Added,result.Updated,result.Skipped);
    }

    private sealed record UpsertResult(int Added,int Updated,int Skipped);
}
