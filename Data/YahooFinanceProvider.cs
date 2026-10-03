using System.Text.Json;
using DualCycleTrader.Models;

namespace DualCycleTrader.Data;

// Free prototype provider. Yahoo's chart endpoint is unofficial and may change.
// TWSE: 2330.TW, TPEx: 6488.TWO, TAIEX: ^TWII
public sealed class YahooFinanceProvider : IMarketDataProvider
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public Task<List<Candle>> GetDailyAsync(string symbol, int days = 260)
        => GetAsync(symbol, "1d", $"{Math.Max(days, 5)}d");

    public Task<List<Candle>> Get60MinuteAsync(string symbol, int days = 30)
        => GetAsync(symbol, "60m", $"{Math.Min(Math.Max(days, 5), 730)}d");

    private async Task<List<Candle>> GetAsync(string symbol, string interval, string range)
    {
        string s = Uri.EscapeDataString(symbol);
        string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{s}?interval={interval}&range={range}&includePrePost=false";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
        var ts = result.GetProperty("timestamp");
        var q = result.GetProperty("indicators").GetProperty("quote")[0];

        var opens=q.GetProperty("open"); var highs=q.GetProperty("high");
        var lows=q.GetProperty("low"); var closes=q.GetProperty("close");
        var vols=q.GetProperty("volume");
        var list = new List<Candle>();

        for(int i=0;i<ts.GetArrayLength();i++)
        {
            if(closes[i].ValueKind==JsonValueKind.Null) continue;
            DateTime t = DateTimeOffset.FromUnixTimeSeconds(ts[i].GetInt64()).LocalDateTime;
            decimal Val(JsonElement a) => a[i].ValueKind==JsonValueKind.Null ? 0 : a[i].GetDecimal();
            list.Add(new Candle(t, Val(opens), Val(highs), Val(lows), Val(closes), Val(vols)));
        }
        return list.OrderBy(c=>c.Time).ToList();
    }
}
