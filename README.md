# 網頁版與桌面版

網頁版的安裝、Firebase、匯入及 GitHub Pages 步驟請看 [WEB_README.md](WEB_README.md)。原桌面版說明如下。

# 雙週期選股系統 v0.1

## 需求
- Windows 10/11
- .NET 8 SDK
- 網路連線

## 執行
```powershell
dotnet restore
dotnet run
```

## 發布單一資料夾
```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

輸出在：
`bin\Release\net8.0-windows\win-x64\publish\`

## 行情
目前第一版使用 Yahoo Finance chart endpoint：
- 加權指數：`^TWII`
- 上市：例如 `2330.TW`
- 上櫃：例如 `6488.TWO`

這是免費、免 API Key 的 prototype provider，但不是正式保證服務的 API。
因此 DataProvider 已與策略分離，未來可換 FinMind / Fugle / 券商行情而不用重寫策略。

## v0.1 注意
1. 目前掃描「UI 中列出的股票」，不是自動下載全市場清單。
2. 文件中的模糊語意已先做第一版量化，例如「附近」預設 ±3%。
3. A/B/C/D、日K與60分K邏輯已分開。
4. 尚未包含自動下單。
5. 尚未包含完整回測。

## v0.2 漏斗篇更新
- 新增 E 過渡盤：ADX 20–25 時不硬判 A/B/C/D。
- 趨勢盤要求 ADX >25 且相較前一日上升。
- 大盤方向先分多方／修正／轉弱／空方，再決定盤型。
- A 日K加入相對大盤強度、20日高附近/突破；60分K要求 MACD 真正金叉、KDJ金叉，並避免 RSI6>80 且遠離 MA20 時追價。
- B 日K加入 MACD 負柱縮短；60分K要求守支撐、不破低、RSI6 回50上、MACD/KDJ金叉、量回升。
- C 日K加入 MACD 負柱縮短、量價型態與守前低；60分K增加支撐確認與不破前低。
- E 只保留強勢日K，60分K條件全部確認才產生訊號。


## v0.3
- 改為自動取得上市＋上櫃股票清單，不再手動輸入。
- 先掃全市場日 K，再抓取全部符合候選股的 60 分 K，並顯示所有候選結果。
- UI 增加掃描進度與停止按鈕。
- 股票清單使用交易所公開 OpenAPI；行情仍沿用免費 Yahoo Finance prototype provider。
- 免費來源可能限流，因此採循序掃描；第一次完整掃描可能需要一些時間。


## v0.4
- RSI 改為 Wilder smoothing；不再只取最後 N 根重新平均。
- 日 K RSI 與 60 分 K RSI 週期可在 UI 自訂，預設仍為策略原始值 14 / 6。
- 設定與行情資料會保存到執行檔旁的 `Data` 資料夾；舊版 `%LocalAppData%` 資料會在首次啟動時自動複製。
- 加入本機行情快取。第一次建立歷史資料，後續掃描只刷新近期資料並合併。
- 日 K 預設保留 500 根；60 分 K 預設保留 120 天（免費來源首次可取得的 60 分資料仍受來源限制）。
- 可自動清理舊行情，也可從 UI 手動清除全部快取。
- UI 顯示目前快取檔案數與容量。
- 行情採逐檔增量更新；已有今日資料的股票直接使用快取，只下載缺少或尚未包含今日行情的部分。
