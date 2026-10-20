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
    protected string MacdPeriodsText = "12,26,9";
    protected string GuideMode = "B";
    protected string DebugSymbol = "", DebugMode = "B", DebugText = "";
    protected DateTime HistoricalDate = DateTime.Today.AddDays(-1);
    protected StrategySettings Settings = new();
    protected List<StockInfo> Universe = new();
    protected List<ResultRow> Rows = new();
    protected bool Analyzing;
    protected int Progress, ProgressMax = 1;
    protected bool Configured;
    protected bool Busy => cancellation is not null;
    private MarketApi? marketApi;
    private CancellationTokenSource? cancellation;

    protected List<ResultRow> VisibleRows => Rows.Where(r => Filter switch
    {
        "A" => r.Strategy.Contains('A'), "B" => r.Strategy.Contains('B'),
        "C" => r.Strategy.Contains('C'), _ => true
    }).ToList();
    protected string GuideDescription => StrategyDescriptions.For(MarketMode.B_BullRange, Settings);
    protected string ExitRules => StrategyDescriptions.ExitRules(Settings);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var config = await Http.GetFromJsonAsync<Config>("firebase-config.json", jsonOptions);
            if (config is null || string.IsNullOrWhiteSpace(config.ProjectId) ||
                string.IsNullOrWhiteSpace(config.MarketApiBaseUrl)) return;
            await JS.InvokeVoidAsync("import", "./js/stock-app.js?v=20261020-c");
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
        MacdPeriodsText = $"{Settings.DailyMacdFast},{Settings.DailyMacdSlow},{Settings.DailyMacdSignal}";
        value = await JS.InvokeAsync<string?>("stockApp.getJson", "universe");
        if (value is not null) Universe = JsonSerializer.Deserialize<List<StockInfo>>(value, jsonOptions) ?? new();
        Rows.Clear();
        Status = $"已載入 {Universe.Count} 檔股票清單；請重新分析以套用 B 條件。";
    }

    protected async Task SaveSettings()
    {
        try
        {
            var periods = MacdPeriodsText.Replace('，', ',').Split(',', StringSplitOptions.TrimEntries);
            if (periods.Length != 3 || !int.TryParse(periods[0], out int macdFast) ||
                !int.TryParse(periods[1], out int macdSlow) ||
                !int.TryParse(periods[2], out int macdSignal) ||
                macdFast < 2 || macdFast > 100 || macdSlow <= macdFast || macdSlow > 150 ||
                macdSignal < 2 || macdSignal > 100)
                throw new InvalidOperationException("MACD 請輸入「快,慢,訊號」，例如 12,26,9；慢線週期須大於快線。");
            Settings.DailyMacdFast = macdFast;
            Settings.DailyMacdSlow = macdSlow;
            Settings.DailyMacdSignal = macdSignal;
            MacdPeriodsText = $"{macdFast},{macdSlow},{macdSignal}";
            Settings.DailyRsiPeriod = Math.Clamp(Settings.DailyRsiPeriod, 2, 100);
            Settings.Rsi5UpperLimit = Math.Clamp(Settings.Rsi5UpperLimit, 1, 101);
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
            Error = null;
            Status = "設定已儲存；請按「重新分析」套用新的策略參數。";
        }
        catch (Exception ex) { Error = ex.Message; Status = "設定未儲存。"; }
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
            var mode = MarketMode.B_BullRange;
            var result = StockScanner.DebugScan(stock.Symbol, stock.Name, daily, market, mode, Settings);
            DebugText = $"{stock.Symbol} {stock.Name}　{result.Date:yyyy/MM/dd}　{(result.Passed ? "入選" : "淘汰")}\n" +
                $"Close {result.Close:F2}｜MA5 {result.Ma20:F2}｜MA20 {result.Ma50:F2}\n" +
                $"RSI({Settings.DailyRsiPeriod}) {result.Rsi14:F1}｜MACD DIF {result.MacdDif:F3} / DEA {result.MacdDea:F3}\n" +
                string.Join("\n", result.HardConditions.Select(item => $"{(item.Value ? "✓" : "✗")} {item.Key}")) +
                $"\n{result.Details}";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    private static DateTime TaiwanToday() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;

    private async Task<List<Candle>> EnsureCandles(string symbol, string timeframe,
        int historyDays, CancellationToken token, DateTime? expectedDate = null,
        List<Candle>? cached = null)
    {
        if (timeframe != "D") throw new ArgumentOutOfRangeException(nameof(timeframe));
        var today = TaiwanToday();
        var from = today.AddDays(-historyDays);
        var existing = cached ?? await Store.GetAsync(symbol, timeframe, from, today.AddDays(1));
        token.ThrowIfCancellationRequested();
        if (existing.Count == 0 || existing[^1].Time.Date < (expectedDate ?? today))
        {
            int days = existing.Count == 0 ? historyDays + 10 : 10;
            var fresh = await marketApi!.GetAsync(symbol, timeframe, days);
            token.ThrowIfCancellationRequested();
            await Store.UpsertRangeAsync(symbol, timeframe, fresh);
            existing = HistoricalCandles.NormalizeDaily(existing.Concat(fresh)
                .Where(c => c.Time >= from));
        }
        return existing;
    }

    private async Task<List<Candle>> BackfillHistoricalCandles(string symbol, DateTime date,
        DateTime from, DateTime to, int fetchDays, int requiredBars,
        List<Candle> cached, CancellationToken token)
    {
        if (HistoricalCandles.DailyAtClose(cached, date).Count >= requiredBars &&
            HistoricalCandles.HasCandleOn(cached, date))
            return cached;
        try
        {
            cached = await Store.RefreshAsync(symbol, "D", from, to);
            token.ThrowIfCancellationRequested();
            if (HistoricalCandles.DailyAtClose(cached, date).Count >= requiredBars &&
                HistoricalCandles.HasCandleOn(cached, date))
                return cached;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* The quote API remains the fallback when Firestore is unavailable. */ }
        var fresh = await marketApi!.GetAsync(symbol, "D", fetchDays);
        token.ThrowIfCancellationRequested();
        if (fresh.Count > 0) await Store.UpsertRangeAsync(symbol, "D", fresh);
        return HistoricalCandles.NormalizeDaily(cached.Concat(fresh)
            .Where(c => c.Time >= from && c.Time <= to));
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
            var setupSeconds = timer.Elapsed.TotalSeconds;
            ProgressMax = Universe.Count;
            int currentCount = 0, refreshedCount = 0, failedCount = 0;
            double latestCheckSeconds = 0, refreshSeconds = 0;
            const int updateBatchSize = 6;
            const int dailyReadBatchSize = 24;
            for (int start = 0; start < Universe.Count; start += dailyReadBatchSize)
            {
                token.ThrowIfCancellationRequested();
                var readBatch = Universe.Skip(start).Take(dailyReadBatchSize).ToArray();
                var phaseStarted = timer.Elapsed;
                var latestDates = await Store.GetLatestDatesAsync(
                    readBatch.Select(stock => stock.Symbol).ToArray(), market[^1].Time.Year);
                latestCheckSeconds += (timer.Elapsed - phaseStarted).TotalSeconds;
                var stale = readBatch.Select((stock, index) => (Stock: stock, Latest: latestDates[index]))
                    .Where(item => item.Latest?.Date < market[^1].Time.Date || item.Latest is null)
                    .ToArray();
                currentCount += readBatch.Length - stale.Length;
                Progress += readBatch.Length - stale.Length;
                if (Progress % 10 == 0) StateHasChanged();
                if (stale.Length == 0) continue;
                for (int offset = 0; offset < stale.Length; offset += updateBatchSize)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = stale.Skip(offset).Take(updateBatchSize).ToArray();
                    phaseStarted = timer.Elapsed;
                    var updated = await Task.WhenAll(batch.Select(async item =>
                    {
                        try
                        {
                            int days = item.Latest is null ? 510 :
                                Math.Clamp((market[^1].Time.Date - item.Latest.Value.Date).Days + 10, 10, 730);
                            var fresh = await marketApi!.GetAsync(item.Stock.Symbol, "D", days);
                            token.ThrowIfCancellationRequested();
                            await Store.UpsertRangeAsync(item.Stock.Symbol, "D", fresh);
                            return true;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { return false; }
                    }));
                    refreshSeconds += (timer.Elapsed - phaseStarted).TotalSeconds;
                    refreshedCount += updated.Count(success => success);
                    failedCount += updated.Count(success => !success);
                    Progress += batch.Length;
                    Status = $"檢查日 K {Progress}/{Universe.Count}：已是最新 {currentCount}、行情請求完成 {refreshedCount}、失敗 {failedCount}";
                    StateHasChanged();
                }
            }
            Status = $"資料更新完成：{Universe.Count} 檔（已是最新 {currentCount}、行情請求完成 {refreshedCount}、失敗 {failedCount}）；"+
                $"共 {timer.Elapsed.TotalSeconds:F1} 秒（大盤與清單 {setupSeconds:F1}、最新日期檢查 {latestCheckSeconds:F1}、行情更新 {refreshSeconds:F1}）。請按重新分析。";
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
        TimeSpan dailyReadTime = TimeSpan.Zero;
        TimeSpan scanTime = TimeSpan.Zero;
        try
        {
            var token = cancellation!.Token;
            if (Universe.Count == 0)
            {
                Status = "取得股票清單..."; StateHasChanged();
                Universe = await marketApi!.GetUniverseAsync();
                if (Universe.Count == 0) { Status = "無法取得股票清單。"; return; }
                await JS.InvokeVoidAsync("stockApp.putJson", "universe", JsonSerializer.Serialize(Universe));
            }
            await SyncDataRevision();
            DateTime today = TaiwanToday();
            DateTime analysisEnd = date?.Date ?? today;
            if (analysisEnd > today) { Status = "歷史日期不能晚於今天。"; return; }
            DateTime from = analysisEnd.AddDays(-550), to = analysisEnd.AddDays(1);
            int fetchDays = Math.Min(730, Math.Max(365, (today - analysisEnd).Days + 365));
            var readStarted = timer.Elapsed;
            var allMarket = await Store.GetAsync("^TWII", "D", from, to);
            if (date is not null && (HistoricalCandles.DailyAtClose(allMarket, analysisEnd).Count < 130 ||
                !HistoricalCandles.HasCandleOn(allMarket, analysisEnd)))
            {
                if (analysisEnd < today.AddDays(-730))
                {
                    Status = "所選日期的大盤資料不足；行情代理目前只能回補近 730 天，請匯入較早的日 K。";
                    return;
                }
                Status = $"回補 {analysisEnd:yyyy/MM/dd} 的大盤日 K..."; StateHasChanged();
                allMarket = await BackfillHistoricalCandles("^TWII", analysisEnd,
                    from, to, fetchDays, 130, allMarket, token);
            }
            dailyReadTime += timer.Elapsed - readStarted;
            var market = date is null ? allMarket : HistoricalCandles.DailyAtClose(allMarket, date.Value);
            if (market.Count < 130)
            {
                Status = $"{analysisEnd:yyyy/MM/dd} 大盤日 K 僅 {market.Count} 根，回補後仍不足 130 根。"+
                    "行情代理最多提供近 730 天；較早日期請匯入歷史日 K。";
                return;
            }
            if (date is not null && !HistoricalCandles.HasCandleOn(market, date.Value))
            { Status = "所選日期沒有大盤日 K，可能是休市日。"; return; }
            var snapshot = MarketClassifier.Analyze(market, Settings);
            var lastClosed = date ?? (DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).TimeOfDay >=
                new TimeSpan(13, 35, 0) ? today : today.AddDays(-1));
            var state = new TradingModeManager(Settings, "unused-web-state").EvaluateHistorical(market, lastClosed);
            var modes = ScannerCoordinator.ActiveScanners(state.ConfirmedTradingMode, Settings);
            MarketText = $"{(date is null ? "目前" : date.Value.ToString("yyyy/MM/dd"))}市場：{snapshot.Mode}　交易模式：{state.ConfirmedTradingMode}";
            MarketDetail = $"MA20 {(snapshot.Ma20Up ? "↑" : "↓")}　MA50 {(snapshot.Ma50Up ? "↑" : "↓")}　MA100 {(snapshot.Ma100Up ? "↑" : "↓")}　ADX {snapshot.Adx:F1}　RSI {snapshot.Rsi:F1}";
            if (modes.Count == 0)
            {
                Rows.Clear();
                await JS.InvokeVoidAsync("stockApp.putJson", "latest-results", "[]");
                Status = "目前交易模式未啟動選股策略，分析完成。";
                return;
            }
            var found = new List<(StockInfo Stock, StockCandidate Candidate, decimal Volume)>();
            int backfilledStocks = 0, failedBackfills = 0, unavailableStocks = 0,
                insufficientStocks = 0, earlierTradingDayStocks = 0;
            int requiredStockBars = Math.Max(25, Math.Max(Settings.DailyMacdSlow + Settings.DailyMacdSignal + 1,
                Settings.DailyRsiPeriod + 4));
            ProgressMax = Universe.Count; Progress = 0;
            const int readBatchSize = 64;
            for (int start = 0; start < Universe.Count; start += readBatchSize)
            {
                token.ThrowIfCancellationRequested();
                var batch = Universe.Skip(start).Take(readBatchSize).ToArray();
                readStarted = timer.Elapsed;
                var dailyBars = await Store.GetManyAsync(batch.Select(stock => stock.Symbol).ToArray(),
                    "D", from, to);
                dailyReadTime += timer.Elapsed - readStarted;
                if (date is not null)
                {
                    var missing = batch.Select((stock, index) => (Stock: stock, Index: index))
                        .Where(item => HistoricalCandles.DailyAtClose(dailyBars[item.Index], analysisEnd).Count < requiredStockBars ||
                            !HistoricalCandles.HasCandleOn(dailyBars[item.Index], analysisEnd)).ToArray();
                    if (analysisEnd < today.AddDays(-730)) unavailableStocks += missing.Length;
                    else
                    {
                        const int backfillBatchSize = 6;
                        for (int offset = 0; offset < missing.Length; offset += backfillBatchSize)
                        {
                            token.ThrowIfCancellationRequested();
                            var group = missing.Skip(offset).Take(backfillBatchSize).ToArray();
                            Status = $"回補個股歷史日 K：{start + offset + group.Length}/{Universe.Count}";
                            StateHasChanged();
                            var repaired = await Task.WhenAll(group.Select(async item =>
                            {
                                try
                                {
                                    var bars = await BackfillHistoricalCandles(item.Stock.Symbol, analysisEnd,
                                        from, to, fetchDays, requiredStockBars, dailyBars[item.Index], token);
                                    return (item.Index, Bars: bars, Failed: false);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch { return (item.Index, Bars: dailyBars[item.Index], Failed: true); }
                            }));
                            foreach (var item in repaired)
                            {
                                dailyBars[item.Index] = item.Bars;
                                if (item.Failed) failedBackfills++;
                                else backfilledStocks++;
                            }
                        }
                    }
                }
                var scanStarted = timer.Elapsed;
                for (int index = 0; index < batch.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var stock = batch[index];
                    var daily = dailyBars[index];
                    if (date is not null) daily = HistoricalCandles.DailyAtClose(daily, date.Value);
                    if (date is not null && daily.Count < requiredStockBars) insufficientStocks++;
                    if (date is not null && daily.Count >= requiredStockBars &&
                        daily[^1].Time.Date < date.Value.Date) earlierTradingDayStocks++;
                    if (daily.Count > 0 && (date is null || daily.Count >= requiredStockBars))
                        foreach (var candidate in ScannerCoordinator.Scan(stock.Symbol, stock.Name,
                            daily, market, modes, Settings))
                            found.Add((stock,candidate,daily[^1].Volume));
                    Progress++; Status = $"分析 {Progress}/{Universe.Count}：{stock.Name}";
                    if (Progress % 100 == 0) StateHasChanged();
                }
                scanTime += timer.Elapsed - scanStarted;
            }
            var candidates = found.OrderByDescending(item => Rank(item.Candidate,item.Candidate.Mode))
                .ToArray();
            var results = candidates.Select(item =>
            {
                var (stock, candidate, volume) = item;
                return new ResultRow(stock.Market, stock.Symbol.Split('.')[0], stock.Name,
                    string.Join("+", candidate.MatchedStrategies.Select(m => m.ToString()[0])),
                    candidate.Close, candidate.ChangePercent, candidate.Rsi14,
                    candidate.RelativeStrength20, candidate.DailyConditions,
                    Rank(candidate, candidate.Mode),
                    FormatMacd(candidate.DailyMacdDif, candidate.DailyMacdDea),
                    FormatKdj(candidate.DailyK, candidate.DailyD, candidate.DailyJ),volume);
            }).ToList();
            Rows = results;
            await JS.InvokeVoidAsync("stockApp.putJson", "latest-results", JsonSerializer.Serialize(Rows));
            Status = $"分析完成：{Rows.Count} 筆候選結果；歷史回補請求 {backfilledStocks} 檔、失敗 {failedBackfills} 檔、仍缺資料 {insufficientStocks} 檔、使用較早交易日 {earlierTradingDayStocks} 檔、超出回補範圍 {unavailableStocks} 檔；"+
                $"耗時 {timer.Elapsed.TotalSeconds:F1} 秒（日 K 載入 {dailyReadTime.TotalSeconds:F1} 秒、掃描 {scanTime.TotalSeconds:F1} 秒）。";
        }
        catch (OperationCanceledException) { Status = "已停止分析。"; }
        catch (Exception ex) { Error = ex.Message; Status = "分析失敗。"; }
        finally
        {
            try { await JS.InvokeVoidAsync("stockApp.publishRevision"); }
            catch (Exception ex) { Error ??= ex.Message; }
            Analyzing = false; End();
        }
    }

    protected async Task ImportFiles(InputFileChangeEventArgs args)
    {
        int added = 0, updated = 0, skipped = 0, errors = 0;
        var failures = new List<string>();
        ImportErrorDetails = "";
        foreach (var file in args.GetMultipleFiles(5000))
        {
            try
            {
                if (file.Name.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var settingsStream = file.OpenReadStream(1_000_000);
                    Settings = await JsonSerializer.DeserializeAsync<StrategySettings>(settingsStream, jsonOptions)
                        ?? throw new InvalidDataException("設定格式錯誤。");
                    Settings.MigrateLegacyScannerDefaults();
                    MacdPeriodsText = $"{Settings.DailyMacdFast},{Settings.DailyMacdSlow},{Settings.DailyMacdSignal}";
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
                if (split < 1 || name[(split + 1)..] != "D")
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
        Status = $"匯入完成：新增 {added}、更新 {updated}、略過 {skipped} 筆；錯誤檔案 {errors}。";
    }

    protected async Task ExportExcel()
    {
        var table = new List<IReadOnlyList<string>>();
        table.Add(new[] { "市場", "代號", "名稱", "策略", "分數", "收盤", "成交量（股）", "漲跌幅", "RSI", "相對強度20",
            "日K_MACD", "日K_KDJ", "日K原因" });
        table.AddRange(VisibleRows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Market,r.Symbol,r.Name,r.Strategy,r.Rank.ToString("F1"),r.Close.ToString(),r.Volume?.ToString("F0") ?? "",r.ChangePercent.ToString("F2"),
            r.Rsi.ToString("F1"),r.RelativeStrength.ToString("F1"),r.DailyMacd,r.DailyKdj,r.DailyReason
        }));
        using var memory = new MemoryStream();
        ExcelExporter.Write(memory, table);
        await JS.InvokeVoidAsync("stockApp.downloadBase64", $"日K選股_{TaiwanToday():yyyyMMdd}.xlsx",
            Convert.ToBase64String(memory.ToArray()),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    protected sealed record ResultRow(string Market, string Symbol, string Name, string Strategy,
        decimal Close, double ChangePercent, double Rsi, double RelativeStrength,
        string DailyReason, double Rank,
        string DailyMacd = "", string DailyKdj = "", decimal? Volume = null);
    private static string FormatMacd(double dif, double dea) =>
        double.IsNaN(dif) || double.IsNaN(dea) ? "—" :
        $"DIF {dif:F3} / DEA {dea:F3} / 柱 {dif - dea:F4}";
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
