using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using DualCycleTrader;
using System.Net.Http.Json;
using DualCycleTrader.Data;
using DualCycleTrader.Models;
using DualCycleTrader.Strategy;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Web.Services;

namespace Web.Pages;

public class HomeBase : ComponentBase
{
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected HttpClient Http { get; set; } = default!;
    [Inject] protected IHistoricalDataStore Store { get; set; } = default!;
    private readonly JsonSerializerOptions jsonOptions = new()
    { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(),
        new TaiwanDateTimeConverter() } };
    protected string? Error;
    protected string Status = "準備就緒", MarketText = "", MarketDetail = "";
    protected string ImportErrorDetails = "";
    protected string Simulation = "", Filter = "全部";
    protected string GuideMode = "A";
    protected string DebugSymbol = "", DebugMode = "A", DebugText = "";
    protected DateTime HistoricalDate = DateTime.Today.AddDays(-1);
    protected StrategySettings Settings = new();
    protected List<StockInfo> Universe = new();
    protected List<ResultRow> Rows = new();
    protected List<HistoricalSignal> SignalRows = new();
    protected int HistoryVisibleCount = 100;
    protected bool ShowingHistory;
    protected bool Analyzing;
    protected int HistoryMonths = 6;
    protected int Progress, ProgressMax = 1;
    protected bool Configured;
    protected bool Busy => cancellation is not null;
    private MarketApi? marketApi;
    private CancellationTokenSource? cancellation;

    protected List<ResultRow> VisibleRows => Rows.Where(r => Filter switch
    {
        "A" => r.Strategy.Contains('A'), "B" => r.Strategy.Contains('B'),
        "C" => r.Strategy.Contains('C'), "符合" => r.HourlySignal == "符合", _ => true
    }).ToList();
    protected string GuideDescription => StrategyDescriptions.For(GuideMode switch
    {
        "B" => MarketMode.B_BullRange, "C" => MarketMode.C_BearRange,
        "D" => MarketMode.D_BearTrend, "E" => MarketMode.E_Transition,
        _ => MarketMode.A_BullTrend
    }, Settings);
    protected string ExitRules => StrategyDescriptions.ExitRules(Settings);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var config = await Http.GetFromJsonAsync<Config>("firebase-config.json", jsonOptions);
            if (config is null || string.IsNullOrWhiteSpace(config.ProjectId) ||
                string.IsNullOrWhiteSpace(config.MarketApiBaseUrl)) return;
            await JS.InvokeVoidAsync("import", "./js/stock-app.js?v=20261004-3");
            await JS.InvokeVoidAsync("stockApp.initialize", new
            {
                apiKey = config.ApiKey, authDomain = config.AuthDomain,
                projectId = config.ProjectId, appId = config.AppId,
                useEmulators = config.UseEmulators
            });
            marketApi = new MarketApi(new HttpClient
            {
                BaseAddress = new Uri(config.MarketApiBaseUrl.TrimEnd('/') + "/")
            });
            Configured = true;
            await LoadState();
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    private async Task LoadState()
    {
        var value = await JS.InvokeAsync<string?>("stockApp.getJson", "settings");
        if (value is not null) Settings = JsonSerializer.Deserialize<StrategySettings>(value, jsonOptions) ?? new();
        Settings.MigrateLegacyScannerDefaults();
        value = await JS.InvokeAsync<string?>("stockApp.getJson", "universe");
        if (value is not null) Universe = JsonSerializer.Deserialize<List<StockInfo>>(value, jsonOptions) ?? new();
        value = await JS.InvokeAsync<string?>("stockApp.getJson", "latest-results");
        if (value is not null) Rows = JsonSerializer.Deserialize<List<ResultRow>>(value, jsonOptions) ?? new();
        Status = $"已載入 {Universe.Count} 檔股票清單、{Rows.Count} 筆上次分析結果。";
    }

    protected async Task SaveSettings()
    {
        try
        {
            Settings.DailyRsiPeriod = Math.Clamp(Settings.DailyRsiPeriod, 2, 100);
            Settings.IntradayRsiPeriod = Math.Clamp(Settings.IntradayRsiPeriod, 2, 100);
            Settings.DailyMacdFast = Math.Clamp(Settings.DailyMacdFast, 2, 100);
            Settings.DailyMacdSlow = Math.Clamp(Math.Max(Settings.DailyMacdSlow,
                Settings.DailyMacdFast + 1), 3, 150);
            Settings.DailyMacdSignal = Math.Clamp(Settings.DailyMacdSignal, 2, 100);
            Settings.IntradayMacdFast = Math.Clamp(Settings.IntradayMacdFast, 2, 100);
            Settings.IntradayMacdSlow = Math.Clamp(Math.Max(Settings.IntradayMacdSlow,
                Settings.IntradayMacdFast + 1), 3, 150);
            Settings.IntradayMacdSignal = Math.Clamp(Settings.IntradayMacdSignal, 2, 100);
            Settings.BreakoutLookbackMin = Math.Clamp(Settings.BreakoutLookbackMin, 10, 60);
            Settings.BreakoutLookbackMax = Math.Clamp(Settings.BreakoutLookbackMax,
                Settings.BreakoutLookbackMin, 60);
            Settings.NearBreakoutPercent = Math.Clamp(Settings.NearBreakoutPercent, 0.1, 10);
            Settings.BreakoutStrongVolumeRatio = Math.Clamp(Settings.BreakoutStrongVolumeRatio, 0.5, 5);
            Settings.BreakoutRsiMin = Math.Clamp(Settings.BreakoutRsiMin, 0, 99);
            Settings.BreakoutRelativeStrengthMin = Math.Clamp(Settings.BreakoutRelativeStrengthMin, -20, 30);
            Settings.BreakoutMaxAboveMa10Percent = Math.Clamp(Settings.BreakoutMaxAboveMa10Percent, 0, 30);
            Settings.BreakoutRsiOverheated = Math.Clamp(Settings.BreakoutRsiOverheated,
                Settings.BreakoutRsiMin + 1, 100);
            Settings.PullbackMinPercent = Math.Clamp(Settings.PullbackMinPercent, 0.1, 20);
            Settings.PullbackMaxPercent = Math.Clamp(Settings.PullbackMaxPercent,
                Settings.PullbackMinPercent, 30);
            Settings.PullbackDaysMin = Math.Clamp(Settings.PullbackDaysMin, 1, 10);
            Settings.PullbackDaysMax = Math.Clamp(Settings.PullbackDaysMax,
                Settings.PullbackDaysMin, 15);
            Settings.PullbackHighLookbackDays = Math.Clamp(Settings.PullbackHighLookbackDays,
                Settings.PullbackDaysMax + 1, 60);
            Settings.PriorStrengthLookbackDays = Math.Clamp(Settings.PriorStrengthLookbackDays, 2, 60);
            Settings.PriorStrengthMinGainPercent = Math.Clamp(Settings.PriorStrengthMinGainPercent, 0, 30);
            Settings.PullbackRsi14Min = Math.Clamp(Settings.PullbackRsi14Min, 0, 100);
            Settings.PullbackRsi14Max = Math.Clamp(Settings.PullbackRsi14Max,
                Settings.PullbackRsi14Min, 100);
            Settings.SupportTolerancePercent = Math.Clamp(Settings.SupportTolerancePercent, 0.1, 10);
            await JS.InvokeVoidAsync("stockApp.putJson", "settings", JsonSerializer.Serialize(Settings));
            Status = "設定已儲存。";
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    protected void Cancel() => cancellation?.Cancel();
    private void Begin() { cancellation = new(); Progress = 0; Error = null; }
    private void End() { cancellation?.Dispose(); cancellation = null; StateHasChanged(); }
    private async Task SyncDataRevision()
    {
        if (await JS.InvokeAsync<bool>("stockApp.syncRevision")) Store.ClearCache();
    }

    protected async Task DebugOneStock()
    {
        try
        {
            Error = null;
            var stock = Universe.FirstOrDefault(item =>
                item.Symbol.Equals(DebugSymbol.Trim(), StringComparison.OrdinalIgnoreCase) ||
                item.Symbol.Split('.')[0].Equals(DebugSymbol.Trim(), StringComparison.OrdinalIgnoreCase));
            if (stock is null) { DebugText = "股票代號不在目前清單中。"; return; }
            await SyncDataRevision();
            var today = TaiwanToday();
            var daily = await Store.GetAsync(stock.Symbol, "D", today.AddDays(-550), today.AddDays(1));
            var market = await Store.GetAsync("^TWII", "D", today.AddDays(-550), today.AddDays(1));
            var mode = DebugMode == "B" ? MarketMode.B_BullRange : MarketMode.A_BullTrend;
            var result = StockScanner.DebugScan(stock.Symbol, stock.Name, daily, market, mode, Settings);
            DebugText = $"{stock.Symbol} {stock.Name}　{result.Date:yyyy/MM/dd}　{(result.Passed ? "入選" : "淘汰")}\n" +
                $"Close {result.Close:F2}｜MA10 {result.Ma10:F2}｜MA20 {result.Ma20:F2}｜MA50 {result.Ma50:F2}｜MA100 {result.Ma100:F2}\n" +
                $"RSI14 {result.Rsi14:F1}｜日K RSI6 {result.DailyRsi6:F1}｜MACD DIF {result.MacdDif:F3} / DEA {result.MacdDea:F3}\n" +
                $"成交量 {result.Volume:F0}｜20 日均量 {result.AverageVolume20:F0}｜量比 {result.VolumeRatio:F2}｜相對強度 {result.RelativeStrength20:F1}%\n" +
                $"近期高點 {result.RecentHigh:F2}｜距突破 {result.DistanceToBreakoutPercent:F1}%｜回檔 {result.PullbackPercent:F1}% / {result.PullbackDays} 日\n" +
                $"前段強勢 {(result.PriorStrength ? "PASS" : "FAIL")}（{result.PriorStrengthGainPercent:F1}%）｜支撐 {result.Support:F2}（距離 {result.SupportDistancePercent:F1}%）｜回檔量比 {result.PullbackVolumeRatio:F2}\n" +
                string.Join("\n", result.HardConditions.Select(item => $"{(item.Value ? "✓" : "✗")} {item.Key}")) +
                $"\n分數 {result.Score:F1}\n{result.Details}";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    private static DateTime TaiwanToday() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;

    private async Task<List<Candle>> EnsureCandles(string symbol, string timeframe,
        int historyDays, CancellationToken token, DateTime? expectedDate = null,
        List<Candle>? cached = null)
    {
        var today = TaiwanToday();
        var from = today.AddDays(-historyDays);
        var existing = cached ?? await Store.GetAsync(symbol, timeframe, from, today.AddDays(1));
        token.ThrowIfCancellationRequested();
        if (existing.Count == 0 || existing[^1].Time.Date < (expectedDate ?? today))
        {
            int days = existing.Count == 0 ? historyDays + 10 : timeframe == "D" ? 10 : 5;
            var fresh = await marketApi!.GetAsync(symbol, timeframe, days);
            token.ThrowIfCancellationRequested();
            await Store.UpsertRangeAsync(symbol, timeframe, fresh);
            existing = existing.Concat(fresh).GroupBy(c => c.Time).Select(g => g.Last())
                .OrderBy(c => c.Time).Where(c => c.Time >= from).ToList();
        }
        return existing;
    }

    protected async Task UpdateData()
    {
        Begin();
        var timer = Stopwatch.StartNew();
        try
        {
            var token = cancellation!.Token;
            await SyncDataRevision();
            Status = "取得加權指數與股票清單..."; StateHasChanged();
            var market = await EnsureCandles("^TWII", "D", 500, token);
            if (market.Count < 130) throw new InvalidOperationException("加權指數日 K 不足 130 根。");
            Universe = await marketApi!.GetUniverseAsync();
            await JS.InvokeVoidAsync("stockApp.putJson", "universe", JsonSerializer.Serialize(Universe));
            ProgressMax = Universe.Count;
            var candidates = new List<StockInfo>();
            const int updateBatchSize = 6;
            const int dailyReadBatchSize = 24;
            for (int start = 0; start < Universe.Count; start += dailyReadBatchSize)
            {
                token.ThrowIfCancellationRequested();
                var readBatch = Universe.Skip(start).Take(dailyReadBatchSize).ToArray();
                var cachedDaily = await Store.GetManyAsync(readBatch.Select(stock => stock.Symbol).ToArray(),
                    "D", TaiwanToday().AddDays(-500), TaiwanToday().AddDays(1));
                for (int offset = 0; offset < readBatch.Length; offset += updateBatchSize)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = readBatch.Skip(offset).Take(updateBatchSize).ToArray();
                    var dailyBars = await Task.WhenAll(batch.Select(async (stock, index) =>
                    {
                        try { return await EnsureCandles(stock.Symbol, "D", 500, token,
                            market[^1].Time.Date, cachedDaily[offset + index]); }
                        catch (OperationCanceledException) { throw; }
                        catch { return null; }
                    }));
                    for (int index = 0; index < batch.Length; index++)
                    {
                        var stock = batch[index];
                        var daily = dailyBars[index];
                        if (daily is not null)
                            foreach (var mode in new[] { MarketMode.A_BullTrend, MarketMode.B_BullRange,
                                MarketMode.C_BearRange, MarketMode.E_Transition })
                                if (StockScanner.Scan(stock.Symbol, stock.Name, daily, market, mode, Settings) is not null)
                                { candidates.Add(stock); break; }
                        Progress++; Status = $"更新日 K {Progress}/{Universe.Count}：{stock.Name}";
                        if (Progress % 10 == 0) StateHasChanged();
                    }
                }
            }
            for (int start = 0; start < candidates.Count; start += updateBatchSize)
            {
                token.ThrowIfCancellationRequested();
                var batch = candidates.Skip(start).Take(updateBatchSize).ToArray();
                Status = $"更新 60 分 K {Math.Min(start + batch.Length, candidates.Count)}/{candidates.Count}";
                StateHasChanged();
                await Task.WhenAll(batch.Select(async stock =>
                {
                    try { await EnsureCandles(stock.Symbol, "60", 60, token, market[^1].Time.Date); }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }));
            }
            Status = $"資料更新完成：{Universe.Count} 檔，候選 {candidates.Count} 檔；耗時 {timer.Elapsed.TotalSeconds:F1} 秒。請按重新分析。";
        }
        catch (OperationCanceledException) { Status = "已停止更新。"; }
        catch (Exception ex) { Error = ex.Message; Status = "更新失敗。"; }
        finally
        {
            try { await JS.InvokeVoidAsync("stockApp.publishRevision"); }
            catch (Exception ex) { Error ??= ex.Message; }
            End();
        }
    }

    protected async Task Analyze(DateTime? date)
    {
        Begin();
        Analyzing = true;
        var timer = Stopwatch.StartNew();
        TimeSpan dailyReadTime = TimeSpan.Zero, hourlyReadTime = TimeSpan.Zero;
        try
        {
            ShowingHistory = false;
            var token = cancellation!.Token;
            if (Universe.Count == 0) { Status = "請先更新資料。"; return; }
            await SyncDataRevision();
            DateTime today = TaiwanToday();
            var readStarted = timer.Elapsed;
            var allMarket = await Store.GetAsync("^TWII", "D", today.AddDays(-550), today.AddDays(1));
            dailyReadTime += timer.Elapsed - readStarted;
            var market = date is null ? allMarket : HistoricalCandles.DailyAtClose(allMarket, date.Value);
            if (market.Count < 130) { Status = "大盤日 K 不足，請先更新資料。"; return; }
            if (date is not null && !HistoricalCandles.HasCandleOn(market, date.Value))
            { Status = "所選日期不是已儲存的大盤交易日。"; return; }
            var snapshot = MarketClassifier.Analyze(market, Settings);
            var lastClosed = date ?? (DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).TimeOfDay >=
                new TimeSpan(13, 35, 0) ? today : today.AddDays(-1));
            var state = new TradingModeManager(Settings, "unused-web-state").EvaluateHistorical(market, lastClosed);
            MarketMode? selected = Simulation switch
            {
                "A" => MarketMode.A_BullTrend, "B" => MarketMode.B_BullRange,
                "C" => MarketMode.C_BearRange, "D" => MarketMode.D_BearTrend,
                "E" => MarketMode.E_Transition, _ => null
            };
            var modes = ScannerCoordinator.ActiveScanners(state.ConfirmedTradingMode, Settings, selected);
            MarketText = $"{(date is null ? "目前" : date.Value.ToString("yyyy/MM/dd"))}市場：{snapshot.Mode}　交易模式：{state.ConfirmedTradingMode}";
            MarketDetail = $"MA20 {(snapshot.Ma20Up ? "↑" : "↓")}　MA50 {(snapshot.Ma50Up ? "↑" : "↓")}　MA100 {(snapshot.Ma100Up ? "↑" : "↓")}　ADX {snapshot.Adx:F1}　RSI {snapshot.Rsi:F1}";
            if (modes.Count == 0)
            {
                Rows.Clear();
                await JS.InvokeVoidAsync("stockApp.putJson", "latest-results", "[]");
                Status = "目前交易模式未啟動選股策略，分析完成。";
                return;
            }
            var found = new List<(StockInfo Stock, StockCandidate Candidate)>();
            ProgressMax = Universe.Count; Progress = 0;
            const int readBatchSize = 24;
            for (int start = 0; start < Universe.Count; start += readBatchSize)
            {
                token.ThrowIfCancellationRequested();
                var batch = Universe.Skip(start).Take(readBatchSize).ToArray();
                readStarted = timer.Elapsed;
                var dailyBars = await Store.GetManyAsync(batch.Select(stock => stock.Symbol).ToArray(),
                    "D", today.AddDays(-550), today.AddDays(1));
                dailyReadTime += timer.Elapsed - readStarted;
                for (int index = 0; index < batch.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var stock = batch[index];
                    var daily = dailyBars[index];
                    if (date is not null) daily = HistoricalCandles.DailyAtClose(daily, date.Value);
                    if (daily.Count >= 130 && (date is null || HistoricalCandles.HasCandleOn(daily, date.Value)))
                        foreach (var candidate in ScannerCoordinator.Scan(stock.Symbol, stock.Name,
                            daily, market, modes, Settings))
                            found.Add((stock,candidate));
                    Progress++; Status = $"分析 {Progress}/{Universe.Count}：{stock.Name}";
                    if (Progress % 100 == 0) StateHasChanged();
                }
            }
            var candidates = found.OrderByDescending(item => Rank(item.Candidate,item.Candidate.Mode))
                .ToArray();
            var results = new List<ResultRow>(candidates.Length);
            const int hourlyReadBatchSize = 6;
            for (int start = 0; start < candidates.Length; start += hourlyReadBatchSize)
            {
                token.ThrowIfCancellationRequested();
                var batch = candidates.Skip(start).Take(hourlyReadBatchSize).ToArray();
                readStarted = timer.Elapsed;
                var hourlyBars = await Store.GetManyAsync(batch.Select(item => item.Stock.Symbol).ToArray(),
                    "60", today.AddDays(-200), today.AddDays(1));
                hourlyReadTime += timer.Elapsed - readStarted;
                for (int index = 0; index < batch.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var (stock,candidate) = batch[index];
                    var hourly = hourlyBars[index];
                    if (date is not null) hourly = HistoricalCandles.HourlyAtClose(hourly, date.Value);
                    var checks = ScannerCoordinator.CheckEntries(hourly, candidate, Settings);
                    var entry = checks.Values.FirstOrDefault(c => c.IsMatch) ?? checks.Values.First();
                    bool enoughHistory = date is null ||
                        (hourly.Count >= 70 && hourly.Any(c => c.Time.Date == date.Value.Date));
                    string signal = !enoughHistory ? "歷史60分資料不足" : hourly.Count == 0 ? "尚未更新" :
                        checks.Values.Any(c => c.IsMatch) ? "符合" : "等待";
                    string reason = !enoughHistory ? "該交易日的 60 分 K 不足。" : hourly.Count == 0 ? "尚無 60 分 K 資料。" : string.Join("；",
                        checks.Select(c => $"{c.Key}：{(c.Value.IsMatch ? "所有條件符合" : string.Join("、", c.Value.UnmetConditions))}"));
                    results.Add(new(stock.Market, stock.Symbol.Split('.')[0], stock.Name,
                        string.Join("+", candidate.MatchedStrategies.Select(m => m.ToString()[0])),
                        candidate.Close, candidate.ChangePercent, candidate.Rsi14,
                        candidate.RelativeStrength20, candidate.DailyConditions, signal, reason,
                        Rank(candidate, candidate.Mode),
                        FormatMacd(candidate.DailyMacdDif, candidate.DailyMacdDea),
                        FormatKdj(candidate.DailyK, candidate.DailyD, candidate.DailyJ),
                        FormatMacd(entry.MacdDif, entry.MacdDea),
                        FormatKdj(entry.K, entry.D, entry.J)));
                }
            }
            Rows = results;
            await JS.InvokeVoidAsync("stockApp.putJson", "latest-results", JsonSerializer.Serialize(Rows));
            Status = $"分析完成：{Rows.Count} 筆候選結果；耗時 {timer.Elapsed.TotalSeconds:F1} 秒（日 K 載入 {dailyReadTime.TotalSeconds:F1} 秒、60 分 K 載入 {hourlyReadTime.TotalSeconds:F1} 秒）。";
        }
        catch (OperationCanceledException) { Status = "已停止分析。"; }
        catch (Exception ex) { Error = ex.Message; Status = "分析失敗。"; }
        finally { Analyzing = false; End(); }
    }

    private string SignalFingerprint => HistoricalSignalStore.Fingerprint(Settings);

    private static IEnumerable<string> Months(DateTime from, DateTime to)
    {
        for (var month = new DateTime(from.Year, from.Month, 1); month <= to;
            month = month.AddMonths(1)) yield return month.ToString("yyyyMM");
    }

    private async Task<HistoricalSignalArchive> LoadSignalMonth(string month, string fingerprint)
    {
        string? json = await JS.InvokeAsync<string?>("stockApp.getJson", $"signals-{fingerprint}-{month}");
        return json is null ? new HistoricalSignalArchive { Fingerprint = fingerprint } :
            JsonSerializer.Deserialize<HistoricalSignalArchive>(json, jsonOptions) ??
            new HistoricalSignalArchive { Fingerprint = fingerprint };
    }

    private async Task SaveSignalMonth(string month, HistoricalSignalArchive archive)
        => await JS.InvokeVoidAsync("stockApp.putJson", $"signals-{archive.Fingerprint}-{month}",
            JsonSerializer.Serialize(archive, jsonOptions));

    private async Task<HistoricalSignalArchive> LoadSignalDay(DateTime date, string fingerprint)
    {
        string? json = await JS.InvokeAsync<string?>("stockApp.getJson",
            $"signals-{fingerprint}-{date:yyyyMMdd}");
        return json is null ? new HistoricalSignalArchive { Fingerprint = fingerprint } :
            JsonSerializer.Deserialize<HistoricalSignalArchive>(json, jsonOptions) ??
            new HistoricalSignalArchive { Fingerprint = fingerprint };
    }

    private async Task SaveSignalDay(DateTime date, HistoricalSignalArchive archive)
        => await JS.InvokeVoidAsync("stockApp.putJson",
            $"signals-{archive.Fingerprint}-{date:yyyyMMdd}",
            JsonSerializer.Serialize(archive, jsonOptions));

    protected async Task ShowSignalHistory()
    {
        Begin();
        var timer = Stopwatch.StartNew();
        try
        {
            if (Universe.Count == 0) { Status = "請先更新資料。"; return; }
            var token = cancellation!.Token;
            await SyncDataRevision();
            DateTime today = TaiwanToday();
            DateTime earliest = HistoryMonths == 0 ? today.AddDays(-7) : today.AddMonths(-HistoryMonths);
            var market = await Store.GetAsync("^TWII", "D", today.AddDays(-550), today.AddDays(1));
            if (market.Count < 130) { Status = "大盤日 K 不足，請先更新資料。"; return; }
            var tradingDates = market.Where(c => c.Time.Date < today).GroupBy(c => c.Time.Date)
                .Select(g => g.Key).Skip(130).Where(d => d >= earliest).ToArray();
            if (tradingDates.Length == 0) { Status = "所選期間沒有足夠的交易日資料。"; return; }
            string fingerprint = SignalFingerprint;
            var archives = new Dictionary<string, HistoricalSignalArchive>();
            foreach (var month in Months(earliest, today))
                archives[month] = await LoadSignalMonth(month, fingerprint);
            var legacyAnalyzed = archives.Values.SelectMany(a => a.AnalyzedDates)
                .Concat(archives.Values.SelectMany(a => a.CompletedDates)).Select(d => d.Date).ToHashSet();
            var dayArchives = new Dictionary<DateTime, HistoricalSignalArchive>();
            var daysToLoad = tradingDates.Where(date => !legacyAnalyzed.Contains(date)).ToArray();
            for (int start = 0; start < daysToLoad.Length; start += 12)
            {
                var dates = daysToLoad.Skip(start).Take(12).ToArray();
                var loaded = await Task.WhenAll(dates.Select(date => LoadSignalDay(date, fingerprint)));
                for (int index = 0; index < dates.Length; index++)
                    dayArchives[dates[index]] = loaded[index];
            }
            var analyzed = legacyAnalyzed.Concat(dayArchives.Values.SelectMany(a => a.AnalyzedDates))
                .Concat(dayArchives.Values.SelectMany(a => a.CompletedDates))
                .Select(d => d.Date).ToHashSet();
            var missing = tradingDates.Where(d => !analyzed.Contains(d)).ToHashSet();
            int newDailyA = 0, newDailyB = 0, completedDays = 0;
            int hourlyReady = 0, hourlyExpected = 0;
            TimeSpan dailyReadTime = TimeSpan.Zero, dailyScanTime = TimeSpan.Zero;
            if (missing.Count > 0)
            {
                ProgressMax = Universe.Count; Progress = 0;
                Status = "正在讀取及篩選歷史日 K…";
                StateHasChanged();
                await Task.Delay(1, token);
                var dailyProgress = new Progress<int>(count =>
                {
                    Progress = count;
                    Status = $"篩選歷史日 K {count}/{Universe.Count}";
                    StateHasChanged();
                });
                var streamed = await HistoricalSignalScanner.FindDailyCandidatesByDateAsync(market, Universe,
                    async symbols =>
                    {
                        var bars = await Store.GetManyAsync(symbols, "D",
                            earliest.AddDays(-250), today.AddDays(1));
                        Store.ClearCache();
                        return bars;
                    }, Settings, missing.Min(), today,
                    missing, token, dailyProgress);
                newDailyA = streamed.AStockDays;
                newDailyB = streamed.BStockDays;
                dailyReadTime = streamed.ReadTime;
                dailyScanTime = streamed.ScanTime;
                var byDate = streamed.ByDate;
                var daily = streamed.CandidateDaily;
                var candidates = byDate.Values.SelectMany(s => s).DistinctBy(s => s.Symbol).ToArray();
                var hourly = new Dictionary<string, List<Candle>>();
                ProgressMax = candidates.Length; Progress = 0;
                const int historyHourlyBatchSize = 6;
                for (int start = 0; start < candidates.Length; start += historyHourlyBatchSize)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = candidates.Skip(start).Take(historyHourlyBatchSize).ToArray();
                    var barsByStock = await Task.WhenAll(batch.Select(async stock =>
                    {
                        int days = Math.Min(730, Math.Max(60, (int)(today - missing.Min()).TotalDays + 31));
                        var bars = await Store.GetAsync(stock.Symbol, "60", today.AddDays(-days), today.AddDays(1));
                        if (bars.Count == 0 || bars[0].Time.Date > missing.Min().AddDays(-30))
                        {
                            try
                            {
                                var fresh = await marketApi!.GetAsync(stock.Symbol, "60", days);
                                await Store.UpsertRangeAsync(stock.Symbol, "60", fresh);
                                bars = bars.Concat(fresh).GroupBy(c => c.Time).Select(g => g.Last())
                                    .OrderBy(c => c.Time).ToList();
                            }
                            catch { /* An incomplete date remains marked incomplete below. */ }
                        }
                        return bars;
                    }));
                    for (int index = 0; index < batch.Length; index++)
                        hourly[batch[index].Symbol] = barsByStock[index];
                    Progress += batch.Length;
                    Status = $"補齊歷史 60 分 K {Progress}/{candidates.Length}";
                    StateHasChanged();
                }
                var reportSignals = new List<HistoricalSignal>();
                ProgressMax = candidates.Length; Progress = 0;
                for (int start = 0; start < candidates.Length; start += 8)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = candidates.Skip(start).Take(8).ToArray();
                    var report = HistoricalSignalScanner.Scan(market, batch,
                        symbol => daily.GetValueOrDefault(symbol) ?? new(),
                        symbol => hourly.GetValueOrDefault(symbol) ?? new(), Settings,
                        missing.Min(), today, token, null, missing);
                    reportSignals.AddRange(report.Signals);
                    Progress += batch.Length;
                    Status = $"檢查歷史觸發訊號 {Progress}/{candidates.Length}";
                    StateHasChanged();
                    await Task.Delay(1, token);
                }
                var completed = new HashSet<DateTime>();
                foreach (var date in missing)
                {
                    bool ready = true;
                    foreach (var stock in byDate.GetValueOrDefault(date, Array.Empty<StockInfo>()))
                    {
                        if (!daily[stock.Symbol].Any(c => c.Time.Date == date)) continue;
                        hourlyExpected++;
                        if (hourly.TryGetValue(stock.Symbol, out var bars) &&
                            bars.Count(c => c.Time.Date <= date) >= 70 &&
                            bars.Count(c => c.Time.Date == date) >= 5)
                            hourlyReady++;
                        else ready = false;
                    }
                    if (ready) completed.Add(date);
                }
                completedDays = completed.Count;
                daily.Clear();
                hourly.Clear();
                Store.ClearCache();
                var merger = new HistoricalSignalStore("unused-web-signals");
                var groupedSignals = reportSignals.GroupBy(s => s.TriggerTime.Date)
                    .ToDictionary(group => group.Key, group => group.ToArray());
                ProgressMax = missing.Count; Progress = 0;
                foreach (var date in missing.OrderBy(d => d))
                {
                    token.ThrowIfCancellationRequested();
                    var archive = merger.Merge(dayArchives[date], new[] { date },
                        completed.Contains(date) ? new[] { date } : Array.Empty<DateTime>(),
                        groupedSignals.GetValueOrDefault(date) ?? Array.Empty<HistoricalSignal>());
                    await SaveSignalDay(date, archive);
                    dayArchives[date] = archive;
                    Progress++;
                    Status = $"儲存觸發紀錄 {Progress}/{missing.Count} 個交易日";
                    StateHasChanged();
                    await Task.Delay(1, token);
                }
            }
            var dayStored = dayArchives.Where(pair => pair.Value.AnalyzedDates.Any(d => d.Date == pair.Key))
                .Select(pair => pair.Key).ToHashSet();
            SignalRows = archives.Values.SelectMany(a => a.Signals)
                .Where(s => !dayStored.Contains(s.TriggerTime.Date))
                .Concat(dayArchives.Values.SelectMany(a => a.Signals))
                .Where(s => s.TriggerTime.Date >= earliest && s.TriggerTime.Date < today)
                .OrderByDescending(s => s.TriggerTime).ToList();
            HistoryVisibleCount = 100;
            ShowingHistory = true;
            int historyA = SignalRows.Count(s => s.Strategies.Contains(MarketMode.A_BullTrend));
            int historyB = SignalRows.Count(s => s.Strategies.Contains(MarketMode.B_BullRange));
            string diagnostic = missing.Count == 0 ? "已讀取既有紀錄" :
                $"本次日 K 候選 A {newDailyA}／B {newDailyB} 筆（股票×日期），日 K 讀取 {dailyReadTime.TotalSeconds:F1} 秒／篩選 {dailyScanTime.TotalSeconds:F1} 秒，60 分資料足夠 {hourlyReady}/{hourlyExpected} 筆候選、完整交易日 {completedDays}/{missing.Count}";
            Status = $"觸發紀錄 A {historyA}／B {historyB} 筆；{diagnostic}；耗時 {timer.Elapsed.TotalSeconds:F1} 秒。";
        }
        catch (OperationCanceledException) { Status = "已停止歷史觸發掃描。"; }
        catch (Exception ex) { Error = ex.Message; Status = "觸發紀錄讀取失敗。"; }
        finally
        {
            try { await JS.InvokeVoidAsync("stockApp.publishRevision"); }
            catch (Exception ex) { Error ??= ex.Message; }
            End();
        }
    }

    protected void ShowMoreHistory() => HistoryVisibleCount = Math.Min(HistoryVisibleCount + 100, SignalRows.Count);

    protected async Task ImportFiles(InputFileChangeEventArgs args)
    {
        int added = 0, updated = 0, skipped = 0, errors = 0, archives = 0;
        var failures = new List<string>();
        ImportErrorDetails = "";
        foreach (var file in args.GetMultipleFiles(5000))
        {
            try
            {
                if (file.Name.Equals("historical-signals.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var archiveStream = file.OpenReadStream(20_000_000);
                    var imported = await JsonSerializer.DeserializeAsync<HistoricalSignalArchive>(archiveStream, jsonOptions)
                        ?? throw new InvalidDataException("觸發紀錄格式錯誤。");
                    var dates = imported.AnalyzedDates.Concat(imported.CompletedDates)
                        .Concat(imported.Signals.Select(s => s.TriggerTime)).ToArray();
                    foreach (var month in dates.Select(d => d.ToString("yyyyMM")).Distinct())
                    {
                        var current = await LoadSignalMonth(month, imported.Fingerprint);
                        var known = current.Signals.Select(s => (s.TriggerTime, s.Symbol)).ToHashSet();
                        var incoming = imported.Signals.Where(s => s.TriggerTime.ToString("yyyyMM") == month).ToArray();
                        var merged = current with
                        {
                            AnalyzedDates = current.AnalyzedDates.Concat(imported.AnalyzedDates
                                .Where(d => d.ToString("yyyyMM") == month)).Distinct().OrderBy(d => d).ToList(),
                            CompletedDates = current.CompletedDates.Concat(imported.CompletedDates
                                .Where(d => d.ToString("yyyyMM") == month)).Distinct().OrderBy(d => d).ToList(),
                            Signals = current.Signals.Concat(incoming.Where(s => !known.Contains((s.TriggerTime, s.Symbol))))
                                .OrderByDescending(s => s.TriggerTime).ToList()
                        };
                        added += incoming.Count(s => !known.Contains((s.TriggerTime, s.Symbol)));
                        skipped += incoming.Length - incoming.Count(s => !known.Contains((s.TriggerTime, s.Symbol)));
                        await SaveSignalMonth(month, merged);
                        archives++;
                    }
                    continue;
                }
                if (file.Name.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var settingsStream = file.OpenReadStream(1_000_000);
                    Settings = await JsonSerializer.DeserializeAsync<StrategySettings>(settingsStream, jsonOptions)
                        ?? throw new InvalidDataException("設定格式錯誤。");
                    Settings.MigrateLegacyScannerDefaults();
                    await SaveSettings();
                    continue;
                }
                if (file.Name.Equals("stock-universe.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var universeStream = file.OpenReadStream(5_000_000);
                    Universe = await JsonSerializer.DeserializeAsync<List<StockInfo>>(universeStream, jsonOptions)
                        ?? throw new InvalidDataException("股票清單格式錯誤。");
                    await JS.InvokeVoidAsync("stockApp.putJson", "universe", JsonSerializer.Serialize(Universe));
                    continue;
                }
                if (file.Name.Equals("market-state.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var stateStream = file.OpenReadStream(1_000_000);
                    using var reader = new StreamReader(stateStream);
                    await JS.InvokeVoidAsync("stockApp.putJson", "legacy-market-state", await reader.ReadToEndAsync());
                    continue;
                }
                string name = Path.GetFileNameWithoutExtension(file.Name);
                int split = name.LastIndexOf('_');
                if (split < 1 || (name[(split + 1)..] != "D" && name[(split + 1)..] != "60"))
                { errors++; if (failures.Count < 10) failures.Add($"{file.Name}: 不支援的檔名"); continue; }
                string symbol = name[..split].Replace('_', '.');
                if (symbol == ".TWII") symbol = "^TWII";
                using var stream = file.OpenReadStream(10_000_000);
                var candles = await JsonSerializer.DeserializeAsync<List<Candle>>(stream, jsonOptions) ?? new();
                var result = await Store.UpsertRangeAsync(symbol, name[(split + 1)..], candles, true);
                added += result.added; updated += result.updated; skipped += result.skipped;
            }
            catch (Exception ex)
            {
                errors++;
                if (failures.Count < 10) failures.Add($"{file.Name}: {ex.Message}");
            }
        }
        await JS.InvokeVoidAsync("stockApp.publishRevision");
        ImportErrorDetails = string.Join("\n", failures);
        Status = $"匯入完成：新增 {added}、更新 {updated}、略過 {skipped} 筆；處理觸發紀錄月份 {archives} 個；錯誤檔案 {errors}。";
    }

    protected async Task ExportExcel()
    {
        var table = new List<IReadOnlyList<string>>();
        if (ShowingHistory)
        {
            table.Add(new[] { "觸發時間", "觸發價格", "市場", "代號", "名稱", "策略", "日K判定日", "當時交易模式", "日K原因" });
            table.AddRange(SignalRows.Select(s => (IReadOnlyList<string>)new[]
            {
                s.TriggerTime.ToString("yyyy/MM/dd HH:mm"),s.TriggerPrice.ToString(),s.Market,
                s.Symbol.Split('.')[0],s.Name,string.Join("+",s.Strategies.Select(m => m.ToString()[0])),
                s.DailyDecisionDate.ToString("yyyy/MM/dd"),s.TradingMode.ToString(),s.DailyReason
            }));
        }
        else
        {
            table.Add(new[] { "市場", "代號", "名稱", "策略", "分數", "收盤", "漲跌幅", "RSI", "相對強度20",
                "日K_MACD", "日K_KDJ", "日K原因", "60分訊號", "60分K_MACD", "60分K_KDJ", "等待原因" });
            table.AddRange(VisibleRows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Market,r.Symbol,r.Name,r.Strategy,r.Rank.ToString("F1"),r.Close.ToString(),r.ChangePercent.ToString("F2"),
                r.Rsi.ToString("F1"),r.RelativeStrength.ToString("F1"),r.DailyMacd,r.DailyKdj,
                r.DailyReason,r.HourlySignal,r.HourlyMacd,r.HourlyKdj,r.WaitingReason
            }));
        }
        using var memory = new MemoryStream();
        ExcelExporter.Write(memory, table);
        await JS.InvokeVoidAsync("stockApp.downloadBase64", $"雙週期選股_{TaiwanToday():yyyyMMdd}.xlsx",
            Convert.ToBase64String(memory.ToArray()),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    protected sealed record ResultRow(string Market, string Symbol, string Name, string Strategy,
        decimal Close, double ChangePercent, double Rsi, double RelativeStrength,
        string DailyReason, string HourlySignal, string WaitingReason, double Rank,
        string DailyMacd = "", string DailyKdj = "", string HourlyMacd = "", string HourlyKdj = "");
    private static string FormatMacd(double dif, double dea) =>
        double.IsNaN(dif) || double.IsNaN(dea) ? "—" : $"DIF {dif:F3} / DEA {dea:F3}";
    private static string FormatKdj(double k, double d, double j) =>
        double.IsNaN(k) || double.IsNaN(d) || double.IsNaN(j) ? "—" :
        $"K {k:F1} / D {d:F1} / J {j:F1}";
    private static double Rank(StockCandidate candidate, MarketMode mode) => mode switch
    {
        MarketMode.A_BullTrend => candidate.RelativeStrength20,
        MarketMode.B_BullRange => candidate.RelativeStrength20,
        MarketMode.C_BearRange => candidate.RelativeStrength20 + candidate.Rsi14 / 20,
        _ => candidate.RelativeStrength20
    };
    private sealed record Config(string ApiKey, string AuthDomain, string ProjectId, string AppId,
        string MarketApiBaseUrl, bool UseEmulators = false);
}
