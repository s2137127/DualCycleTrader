using System.Text.Json;
using System.Net.Http.Json;
using DualCycleTrader.Data;
using DualCycleTrader.Models;

namespace Web.Services;

public sealed class MarketApi(HttpClient http)
{
    public async Task<List<Candle>> GetAsync(string symbol,string timeframe,int days)
    {
        string interval=timeframe=="D"?"1d":"60m";
        string url=$"chart?symbol={Uri.EscapeDataString(symbol)}&interval={interval}&days={days}";
        using var doc=JsonDocument.Parse(await http.GetStringAsync(url));
        var result=doc.RootElement.GetProperty("chart").GetProperty("result")[0];
        var timestamps=result.GetProperty("timestamp");
        var quote=result.GetProperty("indicators").GetProperty("quote")[0];
        var opens=quote.GetProperty("open"); var highs=quote.GetProperty("high");
        var lows=quote.GetProperty("low"); var closes=quote.GetProperty("close");
        var volumes=quote.GetProperty("volume");
        var candles=new List<Candle>();
        for(int i=0;i<timestamps.GetArrayLength();i++)
        {
            if(closes[i].ValueKind==JsonValueKind.Null) continue;
            DateTime time=DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64())
                .ToOffset(TimeSpan.FromHours(8)).DateTime;
            decimal Value(JsonElement values)=>values[i].ValueKind==JsonValueKind.Null?0:values[i].GetDecimal();
            candles.Add(new Candle(time,Value(opens),Value(highs),Value(lows),Value(closes),Value(volumes)));
        }
        return candles.OrderBy(c=>c.Time).ToList();
    }

    public async Task<List<StockInfo>> GetUniverseAsync()
        => await http.GetFromJsonAsync<List<StockInfo>>("universe") ?? new();
}
