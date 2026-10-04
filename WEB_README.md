# 網頁版使用說明

目前 A／B 選股與 60 分 K 進場條件請見 [策略文件](STRATEGY.md)。

原 WinForms 專案仍在根目錄。改造前原始碼備份在 `Backups/WinForms-before-web-migration-2026-10-03.zip`；另一份獨立備份是 Downloads 的 `DualCycleTrader_WinForms_backup_2026-10-03.zip`。`Core` 直接連結原本的 Models、Indicators、Strategy 原始碼，沒有修改公式或閾值。`Web` 是可部署到 GitHub Pages 的 Blazor WebAssembly 網頁；`MarketProxy` 是按使用者要求才向 Yahoo、臺灣證交所及櫃買中心取資料的 Cloudflare Worker。電腦不必長時間開機，也沒有排程監控。

## 1. 本機啟動

需要 .NET 8 SDK，以及可載入 Firebase JavaScript SDK 的網路。先完成下述 Firebase 和 Worker 設定，再將 `Web/wwwroot/firebase-config.example.json` 複製成 `Web/wwwroot/firebase-config.json` 並填值。這個正式設定檔已列入 `.gitignore`。

```powershell
dotnet restore Web/Web.csproj
dotnet run --project Web/Web.csproj --launch-profile http
```

瀏覽 `http://localhost:5289/`。如果只想確認頁面能啟動，可暫時保留空設定；頁面會提示先設定 Firebase。桌面版仍可用 `dotnet run --project DualCycleTrader.csproj` 開啟。

## 2. 建立 Firebase 專案

1. 在 [Firebase Console](https://console.firebase.google.com/) 建立專案，新增「Web 應用程式」。
2. 在 Build → Firestore Database 建立 **Standard** 資料庫，優先選免費方案可用的區域。不要長期使用「測試模式」規則。
3. 本網站採訪客共用資料模式，不需要設定 Firebase Authentication。共用資料的 UID 同時寫在 `Web/wwwroot/js/stock-app.js` 與 `firestore.rules`；若另建專案，請將兩處改成相同的資料路徑識別值。
4. 部署規則與索引設定。`firestore.indexes.json` 會關閉 K 棒 map 的自動索引，避免每根 K 棒產生大量索引項目。

先安裝 Node.js，再用 `npm install -g firebase-tools` 安裝 Firebase CLI。在根目錄執行：

```powershell
firebase login
firebase use --add
firebase deploy --only firestore
```

部署前先檢查 `firestore.rules` 的 UID。規則僅開放指定 UID 的資料路徑，但任何人都能讀寫該路徑；公開網站可能遭修改資料或耗用 Firestore 額度，請定期備份並查看 Usage。Firestore 文件位於 `users/{uid}/candles/{股票_週期_年或月}`：日 K 每股票每年一份文件；60 分 K 每股票每月一份。K 棒以時間作唯一鍵，同時間會合併；沒有變更時不寫入。設定、股票清單和觸發紀錄放在同一使用者的 `state` 子集合，觸發紀錄按月份拆分。

## 3. 部署行情代理

GitHub Pages 無法執行 C# 後端；瀏覽器直接呼叫 Yahoo chart endpoint 也可能被 CORS 限制。此專案的 `MarketProxy/worker.js` 只允許臺股代號、加權指數，以及固定的交易所清單端點。先建立 Cloudflare 帳戶與 Workers 免費方案，安裝 Node.js，再在 `MarketProxy` 目錄執行：

```powershell
npx wrangler login
npx wrangler deploy
```

先把 `MarketProxy/wrangler.toml` 的 `ALLOWED_ORIGINS` 改成你實際的 GitHub Pages 網域；本機預設是 `http://localhost:5289`。把部署後的 Worker URL 填到 `marketApiBaseUrl`。代理不需要 Firebase 管理員金鑰；來源限制主要避免一般瀏覽器跨來源呼叫，不能視為使用者身分驗證。Yahoo chart endpoint 是非正式來源，可能限流或改版。

## 4. 設定網頁

`Web/wwwroot/firebase-config.json` 範例：

```json
{
  "apiKey": "Firebase Web app 的 apiKey",
  "authDomain": "專案ID.firebaseapp.com",
  "projectId": "專案ID",
  "appId": "Firebase Web app 的 appId",
  "marketApiBaseUrl": "https://你的-worker.workers.dev",
  "useEmulators": false
}
```

這些 **Firebase Web 設定值是公開識別資訊**，即使放在前端也會被瀏覽器看見；Firestore Security Rules 只限制可存取的資料路徑，不限制訪客身分。**絕不可提交** Firebase 服務帳戶 JSON、private key、管理員憑證、密碼或其他 API 私密金鑰。

## 5. 匯入舊資料

原程式的資料通常在 `bin/Debug/net8.0-windows/Data`，或你平常執行的 Release/publish 目錄旁的 `Data`。先另行保留這些資料，勿刪除；Debug、Release 和 publish 可能各有一份，請挑你實際使用且最新的一份，避免重複匯入。開啟網頁後，在「匯入舊版行情 JSON」分批選取 `Data/MarketCache` 下的 `*_D.json` 與 `*_60.json`。也可以選取 `settings.json`、`stock-universe.json`、`historical-signals.json`、`market-state.json`。匯入畫面會回報新增、略過及錯誤數；已有的 K 棒和觸發訊號不會因匯入而覆寫。一次最多選 5000 個檔案，資料量大時分批或分天匯入。

原本每股票日 K 與 60 分 K 都是整份 JSON 檔，`historical-signals.json` 保存已分析日期、完整日期、訊號及設定指紋；`market-state.json` 保存確認模式。網頁目前以截至回測日的大盤 K 棒重算確認模式；匯入的舊 `market-state.json` 只保留供查核。

## 6. 本機測試 Firestore

安裝 Firebase CLI 後執行 `firebase emulators:start --only firestore`。在本機的 `firebase-config.json` 設定 `useEmulators: true`，`projectId` 與 CLI 專案一致。開啟網頁匯入一個小 JSON，重複匯入應顯示略過，再以另一瀏覽器確認可讀。

## 7. GitHub Pages

`.github/workflows/pages.yml` 只在手動觸發時建置網站；本次不會自動發佈。將程式推到 GitHub 後，在 Settings → Pages 選 **GitHub Actions**。在 Repository Settings → Secrets and variables → Actions → Variables 建立 `FIREBASE_API_KEY`、`FIREBASE_AUTH_DOMAIN`、`FIREBASE_PROJECT_ID`、`FIREBASE_APP_ID`、`MARKET_API_BASE_URL`，再執行 workflow。流程會生成前端公開設定、改寫 GitHub Pages 子路徑的 `<base href>`、建立 `.nojekyll` 並部署靜態檔。若程式庫名稱是 `username.github.io`，base path 會自動使用 `/`。

完成後在 iPhone Safari 開 `https://YOUR_GITHUB_USER.github.io/REPOSITORY/`，不用登入即可看到共用歷史資料。Safari 的分享按鈕 →「加入主畫面」可建立捷徑。

## 8. 費用與限制

Firestore Standard 免費額度目前包括 1 GiB 儲存、每日 50,000 次讀取和 20,000 次寫入，超出免費額度會影響是否可繼續使用或產生費用，取決於 Firebase 方案。現有本機快取若接近 1 GiB，應先估算實際資料及索引大小；按月分批匯入並在 Firebase Usage 檢查。全市場首次更新可能需數千次 Yahoo 請求，來源可能限流；後續只取近期資料並僅寫入有變化的 K 棒。網頁沒有背景更新，只有按鈕觸發時會更新。
