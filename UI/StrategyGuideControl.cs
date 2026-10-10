using DualCycleTrader.Models;
using DualCycleTrader.Strategy;

namespace DualCycleTrader.UI;

public enum StrategyGuideView { Flow, Conditions }

public sealed class StrategyGuideControl : UserControl
{
    private readonly StrategySettings _settings;
    private readonly StrategyGuideView _view;
    private readonly RichTextBox? _description;
    private readonly RichTextBox? _exitRules;
    private readonly Label _parameterSummary=new(){AutoSize=true,ForeColor=Color.DimGray};

    public StrategyGuideControl(StrategySettings settings, StrategyGuideView view)
    {
        _settings=settings;
        _view=view;
        Dock=DockStyle.Fill;
        BackColor=Color.WhiteSmoke;
        AutoScaleMode=AutoScaleMode.Dpi;

        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));

        var header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(14,9,14,6),BackColor=Color.White,ColumnCount=1,RowCount=2};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var disclaimer=new Label{
            Text="每日大盤盤型與確認後的交易模式分開顯示；自動選股依確認交易模式執行。技術條件以目前軟體設定值為準。",
            AutoSize=true,Dock=DockStyle.Fill,ForeColor=Color.DimGray,Font=new Font("Segoe UI",9)};
        header.Controls.Add(disclaimer,0,0);
        _parameterSummary.Dock=DockStyle.Fill;
        header.Controls.Add(_parameterSummary,0,1);
        header.SizeChanged+=(_,__)=>{
            int width=Math.Max(100,header.ClientSize.Width-header.Padding.Horizontal);
            disclaimer.MaximumSize=new Size(width,0);
            _parameterSummary.MaximumSize=new Size(width,0);
        };

        Control body;
        if(view==StrategyGuideView.Flow)
        {
            body=BuildFlow();
        }
        else
        {
            body=BuildConditions(out _description,out _exitRules);
        }
        root.Controls.Add(header,0,0);
        root.Controls.Add(body,0,1);
        Controls.Add(root);
        RefreshContent();
    }

    public void RefreshContent()
    {
        _parameterSummary.Text=$"目前參數：盤型連續確認 {Math.Max(1,_settings.MarketRegimeConfirmationDays)} 日｜多方雙選股 {(_settings.EnableBullishDualScanner?"A+B":"僅 A")}｜日 K RSI({_settings.DailyRsiPeriod})、MACD({_settings.DailyMacdFast},{_settings.DailyMacdSlow},{_settings.DailyMacdSignal})｜A 突破強量門檻 {_settings.BreakoutVolumeMultiple:0.##} 倍｜B 股價 > MA20 > MA50、回檔 {_settings.PullbackMinPercent:0.##}～{_settings.PullbackMaxPercent:0.##}%｜C 相對強度 +{_settings.RelativeStrengthMin:0.##}%｜顯示全部符合條件的候選股";
        if(_view==StrategyGuideView.Conditions && _description is not null)
        {
            var mode=(MarketMode)(_description.Tag ?? MarketMode.A_BullTrend);
            _description.Text=StrategyDescriptions.For(mode,_settings);
            if(_exitRules is not null) _exitRules.Text=StrategyDescriptions.ExitRules(_settings);
        }
    }

    private Control BuildFlow()
    {
        var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(24,14,24,14)};
        flow.Controls.Add(Card("STEP 1　大盤方向", "MA100 位置與斜率、MA50／MA20 方向、RSI、MACD\r\n↓\r\n得到：多方／修正／轉弱／空方",Color.FromArgb(225,235,248),900,135));
        flow.Controls.Add(Arrow());
        flow.Controls.Add(Card("STEP 2　判斷今日市場盤型", "ADX < 20 → 震盪　｜　ADX 20～25 → 過渡　｜　ADX > 25 且上升 → 趨勢\r\n+DI > -DI → 偏多　｜　-DI > +DI → 偏空；得到今日 A／B／C／D／E",Color.FromArgb(235,235,235),900,125));
        flow.Controls.Add(Arrow());

        flow.Controls.Add(Card("STEP 3　連續確認交易模式", $"同一盤型連續 {Math.Max(1,_settings.MarketRegimeConfirmationDays)} 個已收完交易日才切換；同日重算不增加天數。\r\nA 或 B 確認後屬多方結構；C、D、E 各有獨立交易模式。",Color.FromArgb(232,241,250),900,105));
        flow.Controls.Add(Arrow());

        var modes=new FlowLayoutPanel{Width=900,Height=110,FlowDirection=FlowDirection.LeftToRight,WrapContents=false};
        modes.Controls.Add(ModeBox("多方結構\r\nA 突破 + B 回檔",Color.FromArgb(183,225,190)));
        modes.Controls.Add(ModeBox("C 空方震盪\r\n抗跌型",Color.FromArgb(255,204,153)));
        modes.Controls.Add(ModeBox("D 空方趨勢\r\n停止新多單",Color.FromArgb(245,176,176)));
        modes.Controls.Add(ModeBox("E 過渡盤\r\n觀察型",Color.FromArgb(210,210,210)));
        modes.Resize+=(_,__)=>{
            int itemWidth=Math.Max(68,(modes.ClientSize.Width-36)/4);
            int itemHeight=Math.Max(72,modes.ClientSize.Height-8);
            foreach(Control control in modes.Controls){control.Width=itemWidth;control.Height=itemHeight;}
        };
        flow.Controls.Add(Card("STEP 4　依確認模式啟動選股器", "",Color.White,900,155,modes));
        foreach(string step in new[]{"STEP 5　掃描個股日 K；A+B 候選合併去重","STEP 6　顯示日 K 候選結果","既有持股出場：目前版本尚未實作"})
        {
            flow.Controls.Add(Arrow());
            flow.Controls.Add(Card(step,"",Color.White,900,54));
        }
        flow.Resize+=(_,__)=>{
            int width=Math.Max(420,flow.ClientSize.Width-flow.Padding.Horizontal-28);
            foreach(Control control in flow.Controls) control.Width=width;
        };
        return flow;
    }

    private Control BuildConditions(out RichTextBox description,out RichTextBox exitRules)
    {
        var split=new SplitContainer{Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,Padding=new Padding(12)};
        bool initialDistanceSet=false;
        split.SizeChanged+=(_,__)=>{
            if(initialDistanceSet || split.ClientSize.Width<260) return;
            int available=split.ClientSize.Width-split.SplitterWidth;
            split.SplitterDistance=Math.Min(160,available-split.Panel2MinSize);
            initialDistanceSet=true;
        };
        var list=new ListBox{Dock=DockStyle.Fill,Font=new Font("Segoe UI",11),ItemHeight=34};
        list.Items.AddRange(new object[]{"A 突破型","B 回檔型","C 抗跌型","D 停止新多單","E 觀察型"});
        split.Panel1.Controls.Add(list);

        var right=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2};
        right.RowStyles.Add(new RowStyle(SizeType.Percent,68));
        right.RowStyles.Add(new RowStyle(SizeType.Percent,32));
        var descriptionBox=ReadOnlyText();
        description=descriptionBox;
        exitRules=ReadOnlyText();
        exitRules.BackColor=Color.FromArgb(245,245,232);
        right.Controls.Add(description,0,0);
        right.Controls.Add(exitRules,0,1);
        split.Panel2.Controls.Add(right);

        list.SelectedIndexChanged+=(_,__)=>{
            if(list.SelectedIndex<0) return;
            MarketMode mode=list.SelectedIndex switch{
                0=>MarketMode.A_BullTrend,1=>MarketMode.B_BullRange,2=>MarketMode.C_BearRange,
                3=>MarketMode.D_BearTrend,_=>MarketMode.E_Transition};
            descriptionBox.Tag=mode;
            descriptionBox.Text=StrategyDescriptions.For(mode,_settings);
        };
        list.SelectedIndex=0;
        return split;
    }

    private static RichTextBox ReadOnlyText()=>new(){Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.FixedSingle,BackColor=Color.White,Font=new Font("Segoe UI",10.5f),ScrollBars=RichTextBoxScrollBars.Vertical,DetectUrls=false};
    private static Label Arrow()=>new(){Text="↓",Width=900,Height=28,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",16,FontStyle.Bold)};
    private static Label ModeBox(string text,Color color)=>new(){Text=text,BackColor=color,Width=170,Height=72,Margin=new Padding(4),TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",10,FontStyle.Bold),BorderStyle=BorderStyle.FixedSingle};

    private static Panel Card(string title,string text,Color color,int width,int height=90,Control? extra=null)
    {
        var p=new Panel{Width=width,Height=height,BackColor=color,BorderStyle=BorderStyle.FixedSingle,Padding=new Padding(12)};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=color,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,AutoEllipsis=true,Font=new Font("Segoe UI",11,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},0,0);
        if(extra is not null){extra.Dock=DockStyle.Fill;layout.Controls.Add(extra,0,1);}
        else layout.Controls.Add(new Label{Text=text,Dock=DockStyle.Fill,AutoEllipsis=true,Font=new Font("Segoe UI",10),TextAlign=ContentAlignment.MiddleCenter},0,1);
        p.Controls.Add(layout);
        return p;
    }
}
