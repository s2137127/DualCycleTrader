using System.Text.Json;
namespace DualCycleTrader.Data;

public sealed class TaiwanStockUniverseProvider
{
    private readonly HttpClient _http=new(){Timeout=TimeSpan.FromSeconds(30)};
    private readonly string _cachePath=AppDataPaths.StockUniverse;
    public TaiwanStockUniverseProvider()=>_http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

    public async Task<List<StockInfo>> GetAllAsync()
    {
        var cached=GetCachedAll();
        if(cached.Count>0 && File.GetLastWriteTime(_cachePath).Date==DateTime.Today)
            return cached;

        var r=new List<StockInfo>();
        r.AddRange(await GetTwseAsync());
        try { r.AddRange(await GetTpexAsync()); } catch { }
        var result=r.Where(x=>IsFourDigitStock(x.Symbol))
                .GroupBy(x=>x.Symbol).Select(g=>g.First())
                .OrderBy(x=>x.Symbol).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        File.WriteAllText(_cachePath,JsonSerializer.Serialize(result));
        return result;
    }

    public List<StockInfo> GetCachedAll()
    {
        try
        {
            return File.Exists(_cachePath)
                ? JsonSerializer.Deserialize<List<StockInfo>>(File.ReadAllText(_cachePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    private async Task<List<StockInfo>> GetTwseAsync()
    {
        string json=await _http.GetStringAsync("https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL");
        using var doc=JsonDocument.Parse(json);
        var r=new List<StockInfo>();
        foreach(var e in doc.RootElement.EnumerateArray())
        {
            string code=Get(e,"Code"), name=Get(e,"Name");
            if(code.Length>0) r.Add(new($"{code}.TW",name,"上市"));
        }
        return r;
    }

    private async Task<List<StockInfo>> GetTpexAsync()
    {
        string json=await _http.GetStringAsync("https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes");
        using var doc=JsonDocument.Parse(json);
        var r=new List<StockInfo>();
        foreach(var e in doc.RootElement.EnumerateArray())
        {
            string code=First(e,"SecuritiesCompanyCode","Code","股票代號","證券代號");
            string name=First(e,"CompanyName","Name","股票名稱","證券名稱");
            if(code.Length>0) r.Add(new($"{code}.TWO",name,"上櫃"));
        }
        return r;
    }

    private static string Get(JsonElement e,string n)=>e.TryGetProperty(n,out var p)?p.ToString().Trim():"";
    private static string First(JsonElement e,params string[] ns)
    {
        foreach(var n in ns) if(e.TryGetProperty(n,out var p)) return p.ToString().Trim();
        return "";
    }
    private static bool IsFourDigitStock(string s)
    {
        string code=s.Split('.')[0];
        return code.Length==4 && code.All(char.IsDigit);
    }
}
