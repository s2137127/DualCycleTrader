using DualCycleTrader.Data;
using DualCycleTrader.Models;
using DualCycleTrader.Strategy;
using DualCycleTrader.UI;

namespace DualCycleTrader;

public sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore = new();
    private readonly StrategySettings _settings;
    private readonly CachedMarketDataProvider _data;
    private readonly TaiwanStockUniverseProvider _universe = new();
    private readonly TradingModeManager _tradingModes;
    private readonly HistoricalSignalStore _signalStore = new();

    private readonly Label _mode=new(){AutoSize=true,Font=new Font("Segoe UI",16,FontStyle.Bold)};
    private readonly ComboBox _simulationMode=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=230};
    private readonly ComboBox _resultFilter=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=130};
    private readonly Label _modeContext=new(){AutoSize=true,ForeColor=Color.DarkSlateGray};
    private readonly Label _market=new(){AutoSize=true};
    private readonly Button _updateData=new(){Text="更新資料",Height=38,Width=110};
    private readonly Button _reanalyze=new(){Text="重新分析",Height=38,Width=110};
    private readonly Button _backtest=new(){Text="歷史日期分析",Height=38,Width=120};
    private readonly Button _signalHistory=new(){Text="觸發紀錄",Height=38,Width=95};
    private readonly Button _exportExcel=new(){Text="匯出 Excel",Height=38,Width=105};
    private readonly ComboBox _signalRange=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=90};
    private readonly DateTimePicker _backtestDate=new(){Format=DateTimePickerFormat.Short,Width=110};
    private readonly Button _cancel=new(){Text="停止",Height=38,Width=80,Enabled=false};
    private readonly Button _saveSettings=new(){Text="儲存設定",Height=32,Width=90};
    private readonly Button _clearCache=new(){Text="清除快取",Height=32,Width=90};
    private readonly NumericUpDown _dailyRsi=new(){Minimum=2,Maximum=100,Width=65};
    private readonly NumericUpDown _intradayRsi=new(){Minimum=2,Maximum=100,Width=65};
    private readonly NumericUpDown _dailyMacdFast=new(){Minimum=2,Maximum=100,Width=48};
    private readonly NumericUpDown _dailyMacdSlow=new(){Minimum=3,Maximum=150,Width=48};
    private readonly NumericUpDown _dailyMacdSignal=new(){Minimum=2,Maximum=100,Width=48};
    private readonly NumericUpDown _intradayMacdFast=new(){Minimum=2,Maximum=100,Width=48};
    private readonly NumericUpDown _intradayMacdSlow=new(){Minimum=3,Maximum=150,Width=48};
    private readonly NumericUpDown _intradayMacdSignal=new(){Minimum=2,Maximum=100,Width=48};
    private readonly CheckBox _autoCleanup=new(){Text="自動清理舊行情",AutoSize=true};
    private readonly Label _cacheInfo=new(){AutoSize=true};
    private readonly ProgressBar _progress=new(){Width=360,Height=24};
    private readonly DataGridView _grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AutoGenerateColumns=true,AllowUserToAddRows=false};
    private readonly Label _status=new(){AutoSize=true};
    private CancellationTokenSource? _cts;
    private List<StockInfo> _latestUniverse=new();
    private readonly StrategyGuideControl _flowGuide;
    private readonly StrategyGuideControl _conditionsGuide;
    private MarketMode? _actualMarketMode;
    private DateTime? _historicalDate;
    private bool _showingSignalHistory;
    private List<object> _gridRows=new();
    private string? _sortColumn;
    private bool _sortAscending=true;

    private sealed record ModeOption(string Text,MarketMode? Mode)
    {
        public override string ToString()=>Text;
    }

    private sealed record HistoryRangeOption(string Text,int Months=0,int Days=0)
    {
        public DateTime StartDate(DateTime today)=>Months>0?today.AddMonths(-Months):today.AddDays(-Days);
        public override string ToString()=>Text;
    }

    public MainForm()
    {
        _settings=_settingsStore.Load();
        _data=new CachedMarketDataProvider(new YahooFinanceProvider(),_settings);
        _tradingModes=new TradingModeManager(_settings);
        _flowGuide=new StrategyGuideControl(_settings,StrategyGuideView.Flow);
        _conditionsGuide=new StrategyGuideControl(_settings,StrategyGuideView.Conditions);

        Text="雙週期選股系統 v0.4";
        Width=1500; Height=820; MinimumSize=new Size(1100,650);
        StartPosition=FormStartPosition.CenterScreen;
        AutoScaleMode=AutoScaleMode.Dpi;

        _dailyRsi.Value=_settings.DailyRsiPeriod;
        _intradayRsi.Value=_settings.IntradayRsiPeriod;
        _dailyMacdFast.Value=_settings.DailyMacdFast;
        _dailyMacdSlow.Value=_settings.DailyMacdSlow;
        _dailyMacdSignal.Value=_settings.DailyMacdSignal;
        _intradayMacdFast.Value=_settings.IntradayMacdFast;
        _intradayMacdSlow.Value=_settings.IntradayMacdSlow;
        _intradayMacdSignal.Value=_settings.IntradayMacdSignal;
        _autoCleanup.Checked=_settings.AutoCleanupCache;
        _simulationMode.Items.AddRange(new object[]{
            new ModeOption("自動（使用確認交易模式）",null),
            new ModeOption("A－多方趨勢／突破型",MarketMode.A_BullTrend),
            new ModeOption("B－多方震盪／回檔型",MarketMode.B_BullRange),
            new ModeOption("C－空方震盪／抗跌型",MarketMode.C_BearRange),
            new ModeOption("D－空方趨勢／空手",MarketMode.D_BearTrend),
            new ModeOption("E－過渡盤／觀察型",MarketMode.E_Transition)});
        _simulationMode.SelectedIndex=0;
        _backtestDate.MinDate=DateTime.Today.AddMonths(-6);
        _backtestDate.MaxDate=DateTime.Today.AddDays(-1);
        _backtestDate.Value=_backtestDate.MaxDate;
        _signalRange.Items.AddRange(new object[]{
            new HistoryRangeOption("半年",Months:6),new HistoryRangeOption("一季",Months:3),
            new HistoryRangeOption("一個月",Months:1),new HistoryRangeOption("一週",Days:7)});
        _signalRange.SelectedIndex=0;
        _resultFilter.Items.AddRange(new object[]{"全部候選","A 突破","B 回檔","C 抗跌","60分已觸發"});
        _resultFilter.SelectedIndex=0;

        var top=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(12),ColumnCount=1,RowCount=7};
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<7;i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var modeRow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};
        modeRow.Controls.Add(_mode);
        modeRow.Controls.Add(new Label{Text="模擬模式：",AutoSize=true,Padding=new Padding(18,8,0,0)});
        modeRow.Controls.Add(_simulationMode);
        modeRow.Controls.Add(new Label{Text="結果篩選：",AutoSize=true,Padding=new Padding(12,8,0,0)});
        modeRow.Controls.Add(_resultFilter);
        var settingsRow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=new Padding(0,4,0,4)};
        settingsRow.Controls.Add(new Label{Text="日K RSI:",AutoSize=true,Padding=new Padding(0,7,0,0)});
        settingsRow.Controls.Add(_dailyRsi);
        settingsRow.Controls.Add(new Label{Text="60分K RSI:",AutoSize=true,Padding=new Padding(10,7,0,0)});
        settingsRow.Controls.Add(_intradayRsi);
        settingsRow.Controls.Add(new Label{Text="日K MACD 快/慢/訊號:",AutoSize=true,Padding=new Padding(10,7,0,0)});
        settingsRow.Controls.Add(_dailyMacdFast);
        settingsRow.Controls.Add(_dailyMacdSlow);
        settingsRow.Controls.Add(_dailyMacdSignal);
        settingsRow.Controls.Add(new Label{Text="60分 MACD 快/慢/訊號:",AutoSize=true,Padding=new Padding(10,7,0,0)});
        settingsRow.Controls.Add(_intradayMacdFast);
        settingsRow.Controls.Add(_intradayMacdSlow);
        settingsRow.Controls.Add(_intradayMacdSignal);
        settingsRow.Controls.Add(_autoCleanup);
        settingsRow.Controls.Add(_saveSettings);
        settingsRow.Controls.Add(_clearCache);

        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=new Padding(0,4,0,4)};
        buttons.Controls.Add(_updateData); buttons.Controls.Add(_reanalyze);
        buttons.Controls.Add(new Label{Text="回測收 K 日（近半年）：",AutoSize=true,Padding=new Padding(10,10,0,0)});
        buttons.Controls.Add(_backtestDate); buttons.Controls.Add(_backtest);
        buttons.Controls.Add(new Label{Text="觸發期間：",AutoSize=true,Padding=new Padding(10,10,0,0)});
        buttons.Controls.Add(_signalRange); buttons.Controls.Add(_signalHistory);
        buttons.Controls.Add(_exportExcel); buttons.Controls.Add(_cancel); buttons.Controls.Add(_progress);

        top.Controls.Add(modeRow);
        top.Controls.Add(_market);
        top.Controls.Add(_modeContext);
        top.Controls.Add(settingsRow);
        top.Controls.Add(_cacheInfo);
        top.Controls.Add(buttons);
        top.Controls.Add(_status);

        var strategyTabs=new TabControl{Dock=DockStyle.Fill,Font=new Font("Segoe UI",10)};
        var flowTab=new TabPage("市場與選股流程"){Padding=new Padding(3)};
        var conditionsTab=new TabPage("各策略條件"){Padding=new Padding(3)};
        flowTab.Controls.Add(_flowGuide);
        conditionsTab.Controls.Add(_conditionsGuide);
        strategyTabs.TabPages.AddRange(new[]{flowTab,conditionsTab});
        strategyTabs.SelectedIndexChanged+=(_,__)=>{
            if(strategyTabs.SelectedTab==flowTab) _flowGuide.RefreshContent();
            if(strategyTabs.SelectedTab==conditionsTab) _conditionsGuide.RefreshContent();
        };

        var splitView=new SplitContainer{
            Dock=DockStyle.Fill,Orientation=Orientation.Vertical,SplitterWidth=6};
        var selectionPanel=new Panel{Dock=DockStyle.Fill};
        selectionPanel.Controls.Add(_grid);
        selectionPanel.Controls.Add(top);
        splitView.Panel1.Controls.Add(selectionPanel);
        splitView.Panel2.Controls.Add(strategyTabs);
        Controls.Add(splitView);
        Shown+=(_,__)=>{
            int available=splitView.ClientSize.Width-splitView.SplitterWidth;
            if(available>0)
                splitView.SplitterDistance=Math.Clamp((int)(available*0.55),
                    splitView.Panel1MinSize,available-splitView.Panel2MinSize);
        };

        _updateData.Click+=async(_,__)=>await UpdateDataAsync();
        _reanalyze.Click+=async(_,__)=>await ReanalyzeAsync();
        _backtest.Click+=async(_,__)=>await ReanalyzeAsync(_backtestDate.Value.Date);
        _signalHistory.Click+=async(_,__)=>await ShowSignalHistoryAsync();
        _exportExcel.Click+=(_,__)=>ExportVisibleGrid();
        _cancel.Click+=(_,__)=>_cts?.Cancel();
        _saveSettings.Click+=(_,__)=>SaveSettings();
        _clearCache.Click+=(_,__)=>ClearCache();
        _simulationMode.SelectedIndexChanged+=async(_,__)=>{
            if(!_showingSignalHistory && _actualMarketMode is not null && _cts is null)
                await ReanalyzeAsync(_historicalDate);
        };
        _resultFilter.SelectedIndexChanged+=(_,__)=>BindGridRows();
        _grid.ShowCellToolTips=true;
        _grid.CellToolTipTextNeeded+=GridCellToolTipTextNeeded;
        _grid.CellDoubleClick+=(_,e)=>{
            if(e.RowIndex<0) return;
            var item=_grid.Rows[e.RowIndex].DataBoundItem;
            if(item is null) return;
            var props=System.ComponentModel.TypeDescriptor.GetProperties(item);
            MessageBox.Show($"日K：{props["日K原因"]?.GetValue(item)}\r\n\r\n60分：{props["等待原因"]?.GetValue(item)}",
                $"{props["代號"]?.GetValue(item)} {props["名稱"]?.GetValue(item)} 條件明細");
        };
        _grid.CellFormatting+=GridCellFormatting;
        _grid.ColumnHeaderMouseClick+=GridColumnHeaderMouseClick;
        UpdateCacheInfo();
    }

    private void SaveSettings()
    {
        _settings.DailyRsiPeriod=(int)_dailyRsi.Value;
        _settings.IntradayRsiPeriod=(int)_intradayRsi.Value;
        SaveMacdSettings();
        _settings.AutoCleanupCache=_autoCleanup.Checked;
        _settingsStore.Save(_settings);
        _flowGuide.RefreshContent();
        _conditionsGuide.RefreshContent();
        MessageBox.Show("設定已儲存。新的 RSI 週期會從下一次分析開始使用。","設定");
    }

    private void ClearCache()
    {
        if(MessageBox.Show("確定要清除所有本機行情快取？下次掃描會重新下載歷史資料。",
            "清除快取",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
        _data.ClearCache();
        UpdateCacheInfo();
    }

    private void UpdateCacheInfo()
    {
        var s=_data.GetCacheStats();
        _cacheInfo.Text=$"本機行情快取：{s.files} 個檔案 / {FormatBytes(s.bytes)}";
    }

    private async Task UpdateDataAsync()
    {
        SaveSettingsSilently();
        BeginWork();
        try
        {
            DateTime earliestHistoryDate=DateTime.Today.AddMonths(-6);
            _status.Text=$"取得大盤資料（支援回測至 {earliestHistoryDate:yyyy/MM/dd}）...";
            var market=await _data.GetDailyForHistoryAsync("^TWII",earliestHistoryDate);
            var snap=MarketClassifier.Analyze(market,_settings);
            _status.Text="取得上市＋上櫃股票清單...";
            _latestUniverse=await _universe.GetAllAsync();
            if(_latestUniverse.Count==0) throw new InvalidOperationException("無法取得股票清單。");
            _progress.Minimum=0; _progress.Maximum=_latestUniverse.Count; _progress.Value=0;

            var candidatesBySymbol=new Dictionary<string,StockInfo>();
            MarketMode[] downloadableModes={MarketMode.A_BullTrend,MarketMode.B_BullRange,
                MarketMode.C_BearRange,MarketMode.E_Transition};
            for(int i=0;i<_latestUniverse.Count;i++)
            {
                _cts!.Token.ThrowIfCancellationRequested();
                var s=_latestUniverse[i]; _status.Text=$"更新日K {i+1}/{_latestUniverse.Count}：{s.Name}";
                try
                {
                    var daily=await _data.GetDailyForHistoryAsync(s.Symbol,earliestHistoryDate);
                    foreach(var mode in downloadableModes)
                        if(StockScanner.Scan(s.Symbol,s.Name,daily,market,mode,_settings) is not null)
                            candidatesBySymbol[s.Symbol]=s;
                }
                catch { }
                _progress.Value=i+1;
            }

            // Preload the union of A/B/C/E candidates so simulation never downloads data.
            foreach(var info in candidatesBySymbol.Values)
            {
                _cts!.Token.ThrowIfCancellationRequested();
                _status.Text=$"更新60分K：{info.Name}";
                try { await _data.Get60MinuteAsync(info.Symbol,30); } catch { }
            }
            _data.MarkDataUpdated();
            _status.Text=$"資料更新完成：共 {_latestUniverse.Count} 檔。請按「重新分析」產生結果。";
            UpdateCacheInfo();
        }
        catch(OperationCanceledException){_status.Text="已停止更新資料。";}
        catch(Exception ex){MessageBox.Show(ex.Message,"執行失敗");_status.Text="執行失敗";}
        finally{EndWork();}
    }

    private async Task ReanalyzeAsync(DateTime? historicalDate = null)
    {
        await Task.Yield();
        SaveSettingsSilently();
        BeginWork();
        try
        {
            _showingSignalHistory=false;
            _historicalDate=historicalDate;
            if(_latestUniverse.Count==0)
                _latestUniverse=_universe.GetCachedAll();
            if(_latestUniverse.Count==0)
            {
                MessageBox.Show("目前沒有可用的快取資料，請先按「更新資料」下載行情。",
                    "尚無資料",MessageBoxButtons.OK,MessageBoxIcon.Information);
                _status.Text="尚無快取資料，請先更新資料。";
                return;
            }

            var allMarket=_data.GetCachedDaily("^TWII");
            var market=historicalDate is null ? allMarket
                : HistoricalCandles.DailyAtClose(allMarket,historicalDate.Value);
            if(market.Count==0)
            {
                MessageBox.Show("目前沒有可用的快取資料，請先按「更新資料」下載行情。",
                    "尚無資料",MessageBoxButtons.OK,MessageBoxIcon.Information);
                _status.Text="尚無快取資料，請先更新資料。";
                return;
            }

            if(historicalDate is not null && (market.Count<130 || !HistoricalCandles.HasCandleOn(market,historicalDate.Value)))
            {
                MessageBox.Show("所選日期不是快取中的大盤交易日，或之前不足 130 根日 K。請選擇有完整快取的交易日。",
                    "歷史資料不足",MessageBoxButtons.OK,MessageBoxIcon.Information);
                _status.Text="無法分析：所選日期沒有足夠的大盤日 K。";
                return;
            }
            var lastUpdate=_data.GetLastDataUpdateTime();
            if(historicalDate is null && (lastUpdate is null || lastUpdate.Value.Date!=DateTime.Today))
            {
                string lastText=lastUpdate is null ? "無更新紀錄" : lastUpdate.Value.ToString("yyyy/MM/dd HH:mm");
                MessageBox.Show($"目前資料不是今天更新的（最後更新：{lastText}）。請先按「更新資料」。",
                    "資料需要更新",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                _status.Text="快取資料不是今天更新的，請先更新資料。";
                return;
            }
            var snap=MarketClassifier.Analyze(market,_settings);
            _actualMarketMode=snap.Mode;
            var state=historicalDate is null ? _tradingModes.Evaluate(market)
                : _tradingModes.EvaluateHistorical(market,historicalDate.Value);
            var simulation=(_simulationMode.SelectedItem as ModeOption)?.Mode;
            var scannerModes=ScannerCoordinator.ActiveScanners(state.ConfirmedTradingMode,_settings,simulation);
            string dateLabel=historicalDate is null ? "今日" : historicalDate.Value.ToString("yyyy/MM/dd 收 K");
            _mode.Text=$"{dateLabel}市場：{ModeDisplay(snap.Mode)}\r\n{(historicalDate is null ? "目前" : "當時")}交易模式：{TradingModeText(state.ConfirmedTradingMode)}";
            _market.Text=$"MA20 {(snap.Ma20Up?"↑":"↓")}   MA50 {(snap.Ma50Up?"↑":"↓")}   MA100 {(snap.Ma100Up?"↑":"↓")}   ADX {snap.Adx:F1}   +DI {snap.PlusDi:F1}   -DI {snap.MinusDi:F1}   RSI({_settings.DailyRsiPeriod}) {snap.Rsi:F1}";
            UpdateModeContext(snap.Mode,state,simulation);
            if(historicalDate is not null)
                _modeContext.Text += "\r\n歷史分析只用截至所選日期的快取 K 棒，不變更目前交易模式。";

            if(scannerModes.Count==0)
            {
                _gridRows.Clear(); _grid.DataSource=null;
                _status.Text=state.DisableNewLongEntries && simulation is null
                    ? "確認空方趨勢：停止一般多頭新進場；既有持股不受此選股流程處理。"
                    : "交易模式初始化中或模擬 D：目前不啟動新進場選股器。";
                return;
            }

            _progress.Minimum=0; _progress.Maximum=_latestUniverse.Count; _progress.Value=0;
            _status.Text=$"使用快取執行 {string.Join("+",scannerModes.Select(ScannerModeText))} 分析...";
            var token=_cts!.Token;
            var scan=await Task.Run(()=>
            {
                var result=new List<(StockInfo Info,StockCandidate C)>();
                int missingDaily=0;
                foreach(var s in _latestUniverse)
                {
                    token.ThrowIfCancellationRequested();
                    var cachedDaily=_data.GetCachedDaily(s.Symbol);
                    var daily=historicalDate is null ? cachedDaily
                        : HistoricalCandles.DailyAtClose(cachedDaily,historicalDate.Value);
                    if(historicalDate is not null &&
                        (!HistoricalCandles.HasCandleOn(daily,historicalDate.Value) || daily.Count<130))
                    {
                        missingDaily++;
                        continue;
                    }
                    if(daily.Count>0)
                    {
                        foreach(var c in ScannerCoordinator.Scan(s.Symbol,s.Name,daily,market,scannerModes,_settings))
                            result.Add((s,c));
                    }
                }
                return (result,missingDaily);
            },token);
            var found=scan.result;
            _progress.Value=_progress.Maximum;

            var candidates=found.OrderByDescending(x=>Rank(x.C,x.C.Mode)).ToList();
            var analyzed=await Task.Run(()=>
            {
                var result=new List<object>();
                int missingHourly=0;
                foreach(var x in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    var cached60=_data.GetCached60Minute(x.Info.Symbol);
                    var h60=historicalDate is null ? cached60
                        : HistoricalCandles.HourlyAtClose(cached60,historicalDate.Value);
                    bool hasHistoricalHourly=historicalDate is null ||
                        (h60.Count>=70 && h60.Any(c=>c.Time.Date==historicalDate.Value.Date));
                    if(!hasHistoricalHourly) missingHourly++;
                    if(!hasHistoricalHourly) h60=new List<Models.Candle>();
                    var entries=ScannerCoordinator.CheckEntries(h60,x.C,_settings);
                    var entry=entries.Values.FirstOrDefault(e=>e.IsMatch) ?? entries.Values.First();
                    string signal=!hasHistoricalHourly ? "歷史60分資料不足"
                        : h60.Count==0 ? "尚未更新" : entries.Values.Any(e=>e.IsMatch)?"符合":"等待";
                    string waitingReason=!hasHistoricalHourly ? "快取沒有該交易日的 60 分 K，無法回測當日進場訊號。"
                        : h60.Count==0 ? "尚無 60 分 K 快取，請先更新資料。"
                        : string.Join("；",entries.Select(e=>$"{ScannerModeText(e.Key)}："+
                            (e.Value.IsMatch ? "所有條件符合" : string.Join("、",e.Value.UnmetConditions))));
                    string FormatMacd(double dif,double dea)
                        => double.IsNaN(dif)||double.IsNaN(dea) ? "—" : $"DIF {dif:F3} / DEA {dea:F3}";
                    string FormatKdj(double k,double d,double j)
                        => double.IsNaN(k)||double.IsNaN(d)||double.IsNaN(j) ? "—" : $"K {k:F1} / D {d:F1} / J {j:F1}";
                    result.Add(new{
                        市場=x.Info.Market,代號=x.Info.Symbol.Split('.')[0],名稱=x.Info.Name,
                        策略=string.Join("+",x.C.MatchedStrategies.Select(ModeCode)),日K狀態="符合",
                        日K原因=x.C.DailyConditions,
                        收盤=x.C.Close,漲跌幅=Math.Round(x.C.ChangePercent,2),
                        RSI=Math.Round(x.C.Rsi14,1),相對強度20=Math.Round(x.C.RelativeStrength20,1),
                        日K_MACD=FormatMacd(x.C.DailyMacdDif,x.C.DailyMacdDea),
                        日K_KDJ=FormatKdj(x.C.DailyK,x.C.DailyD,x.C.DailyJ),
                        六十分鐘訊號=signal,等待原因=waitingReason,
                        六十分K_MACD=FormatMacd(entry.MacdDif,entry.MacdDea),
                        六十分K_KDJ=FormatKdj(entry.K,entry.D,entry.J)
                    });
                }
                return (result,missingHourly);
            },token);
            var rows=analyzed.result;
            _gridRows=rows;
            BindGridRows();
            _status.Text=$"{(historicalDate is null ? "今日" : historicalDate.Value.ToString("yyyy/MM/dd")+" 收 K 回測")}：{string.Join("+",scannerModes.Select(ScannerModeText))}，日K符合 {found.Count} 檔。"+
                (historicalDate is null ? "" : $" 日K資料不足／非當日交易 {scan.missingDaily} 檔；當日60分資料不足 {analyzed.missingHourly} 檔。");
            if(historicalDate is not null && (scan.missingDaily>0 || analyzed.missingHourly>0))
                MessageBox.Show($"所選日期 {historicalDate.Value:yyyy/MM/dd} 的快取資料不完整：\r\n"+
                    $"日 K 不足或當日未交易：{scan.missingDaily} 檔\r\n"+
                    $"當日 60 分 K 不足：{analyzed.missingHourly} 檔\r\n\r\n"+
                    "已顯示可計算的候選股；60 分資料不足者不會顯示為已觸發。",
                    "歷史資料不足",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(OperationCanceledException){_status.Text="已停止分析。";}
        catch(Exception ex){MessageBox.Show(ex.Message,"分析失敗");_status.Text="分析失敗";}
        finally{EndWork();}
    }

    private async Task ShowSignalHistoryAsync()
    {
        SaveSettingsSilently();
        var range=(_signalRange.SelectedItem as HistoryRangeOption)!;
        BeginWork();
        try
        {
            if(_latestUniverse.Count==0) _latestUniverse=_universe.GetCachedAll();
            var market=_data.GetCachedDaily("^TWII");
            if(_latestUniverse.Count==0 || market.Count<130)
            {
                MessageBox.Show("缺少股票清單或大盤日 K 快取，請先按「更新資料」。",
                    "資料不足",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            DateTime earliest=range.StartDate(DateTime.Today);
            var marketDates=market.Where(c=>c.Time.Date<DateTime.Today)
                .OrderBy(c=>c.Time).GroupBy(c=>c.Time.Date).Select(g=>g.Key).Skip(130)
                .Where(date=>date>=earliest).ToArray();
            if(marketDates.Length==0)
            {
                MessageBox.Show("所選期間缺少足夠的大盤日 K，請先按「更新資料」。",
                    "資料不足",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            var archive=_signalStore.Load(_settings,_latestUniverse.Select(s=>s.Symbol));
            var analyzedDates=archive.AnalyzedDates.Concat(archive.CompletedDates)
                .Select(d=>d.Date).ToHashSet();
            var missingDates=marketDates.Where(date=>!analyzedDates.Contains(date)).ToHashSet();
            _progress.Minimum=0; _progress.Maximum=_latestUniverse.Count; _progress.Value=0;
            var progress=new Progress<int>(value=>_progress.Value=Math.Min(value,_progress.Maximum));
            var token=_cts!.Token;
            int candidateStockCount=0, failedDownloads=0, newlyCompleted=0;
            HistoricalSignalResult? report=null;
            if(missingDates.Count>0)
            {
                DateTime firstMissing=missingDates.Min();
                _status.Text=$"只分析尚未完成的 {missingDates.Count} 個交易日，從 {firstMissing:yyyy/MM/dd} 開始...";
                var candidatesByDate=await Task.Run(()=>HistoricalSignalScanner.FindDailyCandidatesByDate(
                    market,_latestUniverse,_data.GetCachedDaily,_settings,firstMissing,DateTime.Today,
                    missingDates,token,progress),token);
                var candidateStocks=candidatesByDate.Values.SelectMany(stocks=>stocks)
                    .DistinctBy(stock=>stock.Symbol).ToArray();
                candidateStockCount=candidateStocks.Length;
                _progress.Maximum=Math.Max(1,candidateStocks.Length); _progress.Value=0;
                for(int i=0;i<candidateStocks.Length;i++)
                {
                    token.ThrowIfCancellationRequested();
                    var stock=candidateStocks[i];
                    _status.Text=$"補齊缺漏日期的 60 分 K {i+1}/{candidateStocks.Length}：{stock.Name}";
                    var cached60=_data.GetCached60Minute(stock.Symbol);
                    bool hasGap=candidatesByDate.Any(pair=>pair.Value.Any(s=>s.Symbol==stock.Symbol) &&
                        cached60.Count(c=>c.Time.Date==pair.Key)<5);
                    try { await _data.Get60MinuteForHistoryAsync(stock.Symbol,firstMissing,hasGap); }
                    catch { failedDownloads++; }
                    _progress.Value=i+1;
                }
                _progress.Maximum=_latestUniverse.Count; _progress.Value=0;
                _status.Text="計算尚未完成日期的 60 分 K 觸發...";
                report=await Task.Run(()=>HistoricalSignalScanner.Scan(market,_latestUniverse,
                    _data.GetCachedDaily,_data.GetCached60Minute,_settings,firstMissing,DateTime.Today,
                    token,progress,missingDates),token);
                var newlyCompleteDates=await Task.Run(()=>FindCompleteSignalDates(
                    missingDates,candidatesByDate,token),token);
                newlyCompleted=newlyCompleteDates.Count;
                archive=_signalStore.Merge(archive,missingDates,newlyCompleteDates,report.Signals);
                _signalStore.Save(archive);
            }
            var selectedSignals=archive.Signals.Where(s=>s.TriggerTime.Date>=earliest &&
                s.TriggerTime.Date<DateTime.Today).OrderByDescending(s=>s.TriggerTime).ToArray();
            int analyzedInRange=marketDates.Count(date=>archive.AnalyzedDates.Contains(date) ||
                archive.CompletedDates.Contains(date));
            int completedInRange=archive.CompletedDates.Count(d=>marketDates.Contains(d.Date));
            _showingSignalHistory=true;
            _mode.Text=$"近{range.Text} 60 分 K 觸發紀錄\r\n{earliest:yyyy/MM/dd}～{DateTime.Today.AddDays(-1):yyyy/MM/dd}";
            _market.Text="日 K 與交易模式採前一個已收盤交易日；觸發價格為完成訊號的 60 分 K 收盤價。";
            _modeContext.Text=$"已分析 {analyzedInRange}/{marketDates.Length} 個交易日，其中資料完整 {completedInRange} 日、"+
                $"資料未齊 {analyzedInRange-completedInRange} 日。本次新增分析 {missingDates.Count} 日；"+
                $"補抓失敗 {failedDownloads} 檔。股票範圍為目前清單。";
            _modeContext.ForeColor=Color.DarkOrange;
            _gridRows=selectedSignals.Select(s=>(object)new{
                觸發時間=s.TriggerTime,觸發價格=s.TriggerPrice,
                市場=s.Market,代號=s.Symbol.Split('.')[0],名稱=s.Name,
                策略=string.Join("+",s.Strategies.Select(ModeCode)),
                日K判定日=s.DailyDecisionDate,
                當時交易模式=TradingModeText(s.TradingMode),
                日K狀態="符合",日K原因=s.DailyReason,
                六十分鐘訊號="符合",等待原因="所有對應策略的 60 分 K 進場條件均符合。"
            }).ToList();
            _sortColumn=null;
            if(_resultFilter.SelectedIndex!=0) _resultFilter.SelectedIndex=0;
            BindGridRows();
            _status.Text=$"{range.Text}已保存觸發 {selectedSignals.Length} 筆；"+
                (missingDates.Count==0 ? "所選交易日均已分析，這次直接讀取紀錄。" :
                    $"本次新增分析 {missingDates.Count} 日，尚未分析 {marketDates.Length-analyzedInRange} 日；"+
                    $"涉及候選股 {candidateStockCount} 檔。"+
                    (report is null ? "" : $" 本次日 K 候選日 {report.DailyCandidates} 筆。"));
        }
        catch(OperationCanceledException){_status.Text="已停止歷史觸發掃描。";}
        catch(Exception ex){MessageBox.Show(ex.Message,"歷史掃描失敗");_status.Text="歷史掃描失敗";}
        finally{EndWork();}
    }

    private List<DateTime> FindCompleteSignalDates(IReadOnlySet<DateTime> dates,
        IReadOnlyDictionary<DateTime,IReadOnlyList<StockInfo>> candidatesByDate,
        CancellationToken token)
    {
        var complete=new List<DateTime>();
        var dailyBySymbol=new Dictionary<string,List<Candle>>();
        var hourlyBySymbol=new Dictionary<string,List<Candle>>();
        foreach(var date in dates.OrderBy(d=>d))
        {
            token.ThrowIfCancellationRequested();
            bool ready=true;
            if(candidatesByDate.TryGetValue(date,out var stocks))
                foreach(var stock in stocks)
                {
                    token.ThrowIfCancellationRequested();
                    if(!dailyBySymbol.TryGetValue(stock.Symbol,out var daily))
                        dailyBySymbol[stock.Symbol]=daily=_data.GetCachedDaily(stock.Symbol);
                    // A halted stock has no bar on this date and cannot trigger.
                    if(!daily.Any(c=>c.Time.Date==date)) continue;
                    if(!hourlyBySymbol.TryGetValue(stock.Symbol,out var hourly))
                        hourlyBySymbol[stock.Symbol]=hourly=_data.GetCached60Minute(stock.Symbol);
                    int available=hourly.Count(c=>c.Time.Date<=date);
                    int onDate=hourly.Count(c=>c.Time.Date==date);
                    if(available<70 || onDate<5)
                    {
                        ready=false;
                        break;
                    }
                }
            if(ready) complete.Add(date);
        }
        return complete;
    }

    private void BeginWork()
    {
        _cts=new CancellationTokenSource();
        _updateData.Enabled=false; _reanalyze.Enabled=false; _backtest.Enabled=false;
        _signalHistory.Enabled=false; _signalRange.Enabled=false;
        _backtestDate.Enabled=false; _simulationMode.Enabled=false; _cancel.Enabled=true;
    }

    private void EndWork()
    {
        _updateData.Enabled=true; _reanalyze.Enabled=true; _backtest.Enabled=true;
        _signalHistory.Enabled=true; _signalRange.Enabled=true;
        _backtestDate.Enabled=true; _simulationMode.Enabled=true; _cancel.Enabled=false;
        _cts?.Dispose(); _cts=null; UpdateCacheInfo();
    }

    private void GridCellToolTipTextNeeded(object? sender,DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if(e.RowIndex<0 || e.ColumnIndex<0) return;
        string column=_grid.Columns[e.ColumnIndex].Name;
        if(column!="六十分鐘訊號" && column!="日K狀態") return;
        var item=_grid.Rows[e.RowIndex].DataBoundItem;
        if(item is null) return;
        e.ToolTipText=System.ComponentModel.TypeDescriptor.GetProperties(item)
            [column=="日K狀態" ? "日K原因" : "等待原因"]?.GetValue(item)?.ToString() ?? "";
    }

    private void BindGridRows()
    {
        int horizontalOffset=_grid.HorizontalScrollingOffset;
        _grid.DataSource=null;
        string filter=_resultFilter.SelectedItem?.ToString() ?? "全部候選";
        _grid.DataSource=_gridRows.Where(row=>{
            var props=System.ComponentModel.TypeDescriptor.GetProperties(row);
            string strategy=props["策略"]?.GetValue(row)?.ToString() ?? "";
            string signal=props["六十分鐘訊號"]?.GetValue(row)?.ToString() ?? "";
            return filter switch { "A 突破"=>strategy.Contains('A'),"B 回檔"=>strategy.Contains('B'),
                "C 抗跌"=>strategy.Contains('C'),"60分已觸發"=>signal=="符合",_=>true };
        }).ToList();
        string[] compoundColumns={"等待原因","日K原因","日K_MACD","日K_KDJ","六十分K_MACD","六十分K_KDJ"};
        foreach(DataGridViewColumn column in _grid.Columns)
            column.SortMode=compoundColumns.Contains(column.Name)
                ? DataGridViewColumnSortMode.NotSortable
                : DataGridViewColumnSortMode.Programmatic;

        if(_grid.Columns["等待原因"] is DataGridViewColumn reasonColumn)
        {
            reasonColumn.MinimumWidth=220;
            reasonColumn.AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
        }
        if(_sortColumn is not null && _grid.Columns[_sortColumn] is DataGridViewColumn sortedColumn)
            sortedColumn.HeaderCell.SortGlyphDirection=_sortAscending?SortOrder.Ascending:SortOrder.Descending;
        if(horizontalOffset>0 && _grid.Columns.Count>0)
            _grid.HorizontalScrollingOffset=horizontalOffset;
    }

    private void ExportVisibleGrid()
    {
        var columns=_grid.Columns.Cast<DataGridViewColumn>()
            .Where(column=>column.Visible)
            .OrderBy(column=>column.DisplayIndex).ToArray();
        var rows=_grid.Rows.Cast<DataGridViewRow>()
            .Where(row=>row.Visible && !row.IsNewRow).ToArray();
        if(columns.Length==0 || rows.Length==0)
        {
            MessageBox.Show("目前表格沒有可匯出的資料。","匯出 Excel",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }

        using var dialog=new SaveFileDialog{
            Title="匯出目前表格",Filter="Excel 活頁簿 (*.xlsx)|*.xlsx",
            DefaultExt="xlsx",AddExtension=true,
            FileName=$"雙週期選股_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"};
        if(dialog.ShowDialog(this)!=DialogResult.OK) return;

        try
        {
            var cells=new List<IReadOnlyList<string>>{
                columns.Select(column=>column.HeaderText).ToArray()};
            foreach(var row in rows)
                cells.Add(columns.Select(column=>
                    row.Cells[column.Index].FormattedValue?.ToString() ?? "").ToArray());
            ExcelExporter.Write(dialog.FileName,cells);
            _status.Text=$"已匯出 {rows.Length} 筆資料：{dialog.FileName}";
        }
        catch(Exception ex)
        {
            MessageBox.Show($"匯出失敗：{ex.Message}","匯出 Excel",
                MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }

    private void GridColumnHeaderMouseClick(object? sender,DataGridViewCellMouseEventArgs e)
    {
        if(e.ColumnIndex<0) return;
        var column=_grid.Columns[e.ColumnIndex];
        if(column.SortMode==DataGridViewColumnSortMode.NotSortable) return;
        var firstRow=_gridRows.FirstOrDefault();
        if(firstRow is null) return;

        if(_sortColumn==column.Name) _sortAscending=!_sortAscending;
        else {_sortColumn=column.Name;_sortAscending=true;}

        var property=System.ComponentModel.TypeDescriptor.GetProperties(firstRow)[column.Name];
        if(property is null) return;
        int Compare(object? left,object? right)
        {
            object? a=left is null?null:property.GetValue(left);
            object? b=right is null?null:property.GetValue(right);
            int result=a switch{
                null when b is null=>0,
                null=>-1,
                IComparable comparable=>comparable.CompareTo(b),
                _=>string.Compare(a.ToString(),b?.ToString(),StringComparison.CurrentCulture)
            };
            return _sortAscending?result:-result;
        }
        _gridRows.Sort(Compare);
        BindGridRows();
    }

    private void GridCellFormatting(object? sender,DataGridViewCellFormattingEventArgs e)
    {
        if(e.RowIndex<0 || e.ColumnIndex<0) return;
        string columnName=_grid.Columns[e.ColumnIndex].Name;
        if(columnName!="收盤" && columnName!="漲跌幅") return;

        var item=_grid.Rows[e.RowIndex].DataBoundItem;
        if(item is null) return;
        var value=System.ComponentModel.TypeDescriptor.GetProperties(item)["漲跌幅"]?.GetValue(item);
        if(value is not double changePercent) return;
        var style=e.CellStyle;
        if(style is null) return;

        style.BackColor=_grid.DefaultCellStyle.BackColor;
        style.ForeColor=_grid.DefaultCellStyle.ForeColor;
        style.SelectionBackColor=_grid.DefaultCellStyle.SelectionBackColor;
        style.SelectionForeColor=_grid.DefaultCellStyle.SelectionForeColor;

        if(columnName=="漲跌幅")
        {
            e.Value=$"{changePercent:+0.00;-0.00;0.00}%";
            e.FormattingApplied=true;
        }

        // 台股漲跌停價受價格跳動單位影響，換算後不一定恰好為 ±10%。
        if(changePercent>=9.5)
        {
            style.BackColor=Color.Red;
            style.ForeColor=Color.White;
            style.SelectionBackColor=Color.DarkRed;
        }
        else if(changePercent<=-9.5)
        {
            style.BackColor=Color.Green;
            style.ForeColor=Color.White;
            style.SelectionBackColor=Color.DarkGreen;
        }
    }

    private void SaveSettingsSilently()
    {
        _settings.DailyRsiPeriod=(int)_dailyRsi.Value;
        _settings.IntradayRsiPeriod=(int)_intradayRsi.Value;
        SaveMacdSettings();
        _settings.AutoCleanupCache=_autoCleanup.Checked;
        _settingsStore.Save(_settings);
    }

    private static string FormatBytes(long b)
    {
        if(b<1024) return $"{b} B";
        if(b<1024*1024) return $"{b/1024d:F1} KB";
        return $"{b/1024d/1024d:F1} MB";
    }

    private void SaveMacdSettings()
    {
        if(_dailyMacdFast.Value>=_dailyMacdSlow.Value)
            _dailyMacdSlow.Value=_dailyMacdFast.Value+1;
        if(_intradayMacdFast.Value>=_intradayMacdSlow.Value)
            _intradayMacdSlow.Value=_intradayMacdFast.Value+1;
        _settings.DailyMacdFast=(int)_dailyMacdFast.Value;
        _settings.DailyMacdSlow=(int)_dailyMacdSlow.Value;
        _settings.DailyMacdSignal=(int)_dailyMacdSignal.Value;
        _settings.IntradayMacdFast=(int)_intradayMacdFast.Value;
        _settings.IntradayMacdSlow=(int)_intradayMacdSlow.Value;
        _settings.IntradayMacdSignal=(int)_intradayMacdSignal.Value;
    }

    private static double Rank(StockCandidate c,MarketMode m)=>m switch{
        MarketMode.A_BullTrend=>c.RelativeStrength20+c.Rsi14/10,
        MarketMode.B_BullRange=>c.RelativeStrength20-Math.Abs(c.Rsi14-47.5)/10,
        MarketMode.C_BearRange=>c.RelativeStrength20+c.Rsi14/20,
        _=>c.RelativeStrength20
    };

    private void UpdateModeContext(MarketMode actual,MarketState state,MarketMode? simulation)
    {
        int required=Math.Max(1,_settings.MarketRegimeConfirmationDays);
        string confirmation=$"模式確認：{ModeDisplay(state.CandidateMarketRegime)} {Math.Min(state.CandidateRegimeDays,required)}/{required} 日";
        _modeContext.Text=simulation is null ? confirmation
            : $"{confirmation}\r\n目前僅顯示 {ScannerModeText(simulation.Value)} 模擬候選股；真實交易模式不變。";
        _modeContext.ForeColor=simulation is null?Color.DarkSlateGray:Color.DarkOrange;
    }

    private static string TradingModeText(TradingMode mode)=>mode switch{
        TradingMode.BullishAB=>"🟡 多方結構（A+B）",TradingMode.BearRangeC=>"🟠 空方震盪（C）",
        TradingMode.BearTrendD=>"🔴 空方趨勢（停止新多單）",TradingMode.TransitionE=>"⚠️ 過渡觀察（E）",
        _=>"初始化中"
    };

    private static string ModeDisplay(MarketMode m)=>m switch{
        MarketMode.A_BullTrend=>"🟢 A 多方趨勢",MarketMode.B_BullRange=>"🟡 B 多方震盪",
        MarketMode.C_BearRange=>"🟠 C 空方震盪",MarketMode.D_BearTrend=>"🔴 D 空方趨勢",
        MarketMode.E_Transition=>"⚠️ E 過渡盤",_=>"未知"
    };

    private static string ScannerModeText(MarketMode m)=>m switch{
        MarketMode.A_BullTrend=>"A 突破型",MarketMode.B_BullRange=>"B 回檔型",
        MarketMode.C_BearRange=>"C 抗跌型",MarketMode.D_BearTrend=>"D 空手",
        MarketMode.E_Transition=>"E 觀察型",_=>"未知"
    };

    private static string ModeCode(MarketMode m)=>m switch{
        MarketMode.A_BullTrend=>"A",MarketMode.B_BullRange=>"B",MarketMode.C_BearRange=>"C",
        MarketMode.D_BearTrend=>"D",MarketMode.E_Transition=>"E",_=>"?"
    };

    private static string ModeText(MarketMode m)=>m switch{
        MarketMode.A_BullTrend=>"A 多頭趨勢",MarketMode.B_BullRange=>"B 多頭震盪",
        MarketMode.C_BearRange=>"C 空頭震盪",MarketMode.D_BearTrend=>"D 空頭趨勢",
        _=>m.ToString().Contains("Transition")?"E 過渡盤":"未知"
    };
}
