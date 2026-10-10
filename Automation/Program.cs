using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DualCycleTrader.Data;
using DualCycleTrader.Models;
using DualCycleTrader.Strategy;

const string uid = "4qjqaWnyjQW5HVYAuaezaxBJYP02";
const string origin = "https://s2137127.github.io";
bool dryRun = args.Contains("--dry-run");
int limit = 0;
int limitPosition = Array.IndexOf(args, "--limit");
if (limitPosition >= 0 && (limitPosition + 1 >= args.Length ||
    !int.TryParse(args[limitPosition + 1], out limit) || limit < 1))
    throw new ArgumentException("--limit needs a positive number");
string project = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID")?.Trim() ?? "";
string worker = Environment.GetEnvironmentVariable("MARKET_API_BASE_URL")?.TrimEnd('/') ?? "";
if (project.Length == 0 || !worker.StartsWith("https://", StringComparison.Ordinal))
    throw new InvalidOperationException("FIREBASE_PROJECT_ID and HTTPS MARKET_API_BASE_URL are required");
string root = $"https://firestore.googleapis.com/v1/projects/{project}/databases/(default)/documents/users/{uid}";
DateTime today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;
if (today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && !dryRun)
{
    Console.WriteLine($"{today:yyyy-MM-dd}: weekend; no analysis");
    return;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
http.DefaultRequestHeaders.Add("Origin", origin);
http.DefaultRequestHeaders.UserAgent.ParseAdd("DualCycleTrader/daily-analysis");
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
options.Converters.Add(new JsonStringEnumConverter());

async Task<JsonDocument?> GetDocument(string path)
{
    for (int attempt = 0; attempt < 4; attempt++)
    {
        using var response = await http.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.IsSuccessStatusCode)
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if ((int)response.StatusCode is not (429 or 500 or 502 or 503 or 504) || attempt == 3)
            throw new HttpRequestException($"{path}: HTTP {(int)response.StatusCode}");
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }
    throw new InvalidOperationException("Retry limit reached");
}

static decimal Number(JsonElement fields, string name)
{
    var value = fields.GetProperty(name);
    if (value.TryGetProperty("integerValue", out var integer))
        return decimal.Parse(integer.GetString()!, CultureInfo.InvariantCulture);
    return value.GetProperty("doubleValue").GetDecimal();
}

static DateTime CandleTime(string value)
{
    bool offset = value.EndsWith('Z') || (value.Length >= 6 &&
        (value[^6] == '+' || value[^6] == '-'));
    return offset ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)
        .ToOffset(TimeSpan.FromHours(8)).DateTime :
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
}

async Task<List<Candle>> GetBars(string symbol, DateTime from, DateTime to)
{
    var bars = new List<Candle>();
    for (int year = from.Year; year <= to.Year; year++)
    {
        string id = Uri.EscapeDataString($"{Uri.EscapeDataString(symbol)}_D_{year}");
        using var document = await GetDocument($"{root}/candles/{id}");
        if (document is null) continue;
        var fields = document.RootElement.GetProperty("fields");
        if (!fields.TryGetProperty("bars", out var storedBars)) continue;
        foreach (var item in storedBars.GetProperty("mapValue").GetProperty("fields").EnumerateObject())
        {
            var candle = item.Value.GetProperty("mapValue").GetProperty("fields");
            var time = CandleTime(candle.GetProperty("Time").GetProperty("stringValue").GetString()!);
            if (time < from || time > to) continue;
            bars.Add(new Candle(time, Number(candle,"Open"), Number(candle,"High"),
                Number(candle,"Low"), Number(candle,"Close"), Number(candle,"Volume")));
        }
    }
    return bars.OrderBy(c => c.Time).ToList();
}

async Task<string?> GetState(string name)
{
    using var document = await GetDocument($"{root}/state/{name}");
    if (document is null) return null;
    return document.RootElement.GetProperty("fields").GetProperty("json")
        .GetProperty("stringValue").GetString();
}

async Task PutState(string name, string json)
{
    using var request = new HttpRequestMessage(HttpMethod.Patch,
        $"{root}/state/{name}?updateMask.fieldPaths=json");
    request.Content = JsonContent.Create(new { fields = new { json = new { stringValue = json } } });
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
}

static double Rank(StockCandidate candidate) => candidate.Mode switch
{
    MarketMode.A_BullTrend or MarketMode.B_BullRange or MarketMode.E_Transition =>
        candidate.RelativeStrength20,
    MarketMode.C_BearRange => candidate.RelativeStrength20 + candidate.Rsi14 / 20,
    _ => candidate.RelativeStrength20
};

static string FormatMacd(double dif, double dea) =>
    double.IsNaN(dif) || double.IsNaN(dea) ? "—" : $"DIF {dif:F3} / DEA {dea:F3}";
static string FormatKdj(double k, double d, double j) =>
    double.IsNaN(k) || double.IsNaN(d) || double.IsNaN(j) ? "—" :
        $"K {k:F1} / D {d:F1} / J {j:F1}";

var fromDate = today.AddDays(-550);
var market = await GetBars("^TWII", fromDate, today.AddDays(1));
if (market.Count < 130) throw new InvalidOperationException("Market has fewer than 130 daily candles");
if (market[^1].Time.Date != today && !dryRun)
{
    Console.WriteLine($"{today:yyyy-MM-dd}: no market candle today; holiday or delayed source");
    return;
}
DateTime analysisDate = dryRun ? market[^1].Time.Date : today;
var settingsJson = await GetState("settings");
var settings = settingsJson is null ? new StrategySettings() :
    JsonSerializer.Deserialize<StrategySettings>(settingsJson, options) ?? new StrategySettings();
settings.MigrateLegacyScannerDefaults();
DateTime lastClosed = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).TimeOfDay >=
    new TimeSpan(13,35,0) ? analysisDate : analysisDate.AddDays(-1);
var mode = new TradingModeManager(settings,"unused-automation-state")
    .EvaluateHistorical(market,lastClosed).ConfirmedTradingMode;
var modes = ScannerCoordinator.ActiveScanners(mode,settings);
var universe = await http.GetFromJsonAsync<List<StockInfo>>($"{worker}/universe", options) ?? new();
if (limit > 0) universe = universe.Take(limit).ToList();
Console.WriteLine($"{analysisDate:yyyy-MM-dd}: {universe.Count} stocks, mode {mode}, scanners {string.Join(',',modes)}");
var found = new System.Collections.Concurrent.ConcurrentBag<ResultRow>();
var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
await Parallel.ForEachAsync(universe, new ParallelOptions { MaxDegreeOfParallelism = 8 },
    async (stock, _) =>
    {
        if (modes.Count == 0) return;
        try
        {
            var daily = await GetBars(stock.Symbol,fromDate,today.AddDays(1));
            if (daily.Count < 130 || daily[^1].Time.Date != analysisDate) return;
            foreach (var candidate in ScannerCoordinator.Scan(stock.Symbol,stock.Name,daily,market,modes,settings))
                found.Add(new ResultRow(stock.Market,stock.Symbol.Split('.')[0],stock.Name,
                    string.Join("+",candidate.MatchedStrategies.Select(m => m.ToString()[0])),
                    candidate.Close,candidate.ChangePercent,candidate.Rsi14,
                    candidate.RelativeStrength20,candidate.DailyConditions,Rank(candidate),
                    FormatMacd(candidate.DailyMacdDif,candidate.DailyMacdDea),
                    FormatKdj(candidate.DailyK,candidate.DailyD,candidate.DailyJ),daily[^1].Volume));
        }
        catch (Exception error) { errors.Add($"{stock.Symbol}: {error.Message}"); }
    });
if (!errors.IsEmpty)
{
    foreach (var error in errors.Take(20)) Console.Error.WriteLine(error);
    throw new InvalidOperationException($"Analysis failed for {errors.Count} stocks");
}
var rows = found.OrderByDescending(row => row.Rank).ToArray();
Console.WriteLine($"Candidates: {rows.Length}");
if (!dryRun)
{
    await PutState("universe",JsonSerializer.Serialize(universe));
    await PutState("latest-results",JsonSerializer.Serialize(rows));
}

sealed record ResultRow(string Market, string Symbol, string Name, string Strategy,
    decimal Close, double ChangePercent, double Rsi, double RelativeStrength,
    string DailyReason, double Rank, string DailyMacd, string DailyKdj, decimal? Volume);
