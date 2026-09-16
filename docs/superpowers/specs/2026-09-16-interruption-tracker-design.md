# 打擾記錄器（Stop Annoying Me）設計規格

- 日期：2026-09-16
- 狀態：已核可，進入實作
- 平台：Windows 11 Pro

---

## 1. 問題與目標

上班時被同事、主管、電話打斷，事後無法量化「今天到底被打擾幾次」。

目標：提供一個常駐桌面的小工具，**被打擾當下一鍵記錄時間**，並統計當日／本週／本月的受打擾次數與時段分佈。

成功標準：
1. 從「被打擾」到「記錄完成」不超過一個動作（一次點擊或一次熱鍵）。
2. 記錄動作不需要切換視窗、不打斷當前工作。
3. 資料可長期累積、可匯出、不會因當機遺失。

## 2. 技術選型

| 項目 | 選擇 | 理由 |
|------|------|------|
| 語言／框架 | C# / .NET 10 + WPF | 本機已具 SDK 10.0.401 與 WindowsDesktop Runtime 10.0.12；XAML 便於視覺設計 |
| 系統匣 | `System.Windows.Forms.NotifyIcon`（同專案啟用 WinForms） | 免第三方套件，功能足夠 |
| 全域熱鍵 | Win32 `RegisterHotKey` P/Invoke | 不需額外相依，可在背景觸發 |
| 資料儲存 | SQLite（`Microsoft.Data.Sqlite`） | 單檔、免安裝服務、統計查詢方便 |
| 設定儲存 | JSON（`System.Text.Json`） | 人類可讀、易手動修正 |
| 測試 | xUnit | 核心邏輯可獨立驗證 |

不採用 Python + tkinter：本機 Python 3.11 已移除，且 tkinter 無法妥善實作系統匣與全域熱鍵。

## 3. 架構

分成三個專案，UI 與邏輯分離，核心邏輯可獨立測試：

```
StopAnnoyingMe.sln
├── src/StopAnnoyingMe.Core   (net10.0)          純邏輯，無 UI 相依
├── src/StopAnnoyingMe.App    (net10.0-windows)  WPF 介面、系統匣、熱鍵
└── tests/StopAnnoyingMe.Core.Tests (net10.0)    xUnit
```

### 3.1 Core 職責

| 單元 | 做什麼 | 相依 |
|------|--------|------|
| `Interruption` | 一筆打擾記錄的資料模型 | — |
| `AppSettings` | 設定資料模型 | — |
| `DatabaseInitializer` | 建立／升級資料庫結構，處理損毀復原 | Sqlite |
| `InterruptionRepository` | 新增、刪除、查詢記錄 | Sqlite |
| `StatisticsService` | 計算今日／本週／本月次數、每小時分佈 | Repository |
| `DuplicateGuard` | 判斷是否為手滑重複點擊 | — |
| `CsvExporter` | 匯出 CSV | Repository |
| `SettingsService` | 讀寫 settings.json | — |

### 3.2 App 職責

| 單元 | 做什麼 |
|------|--------|
| `MainWindow` | 主畫面：記錄按鈕、今日次數、最近記錄、可選標籤 |
| `StatsWindow` | 統計畫面：期間次數與每小時分佈長條圖 |
| `SettingsWindow` | 設定：熱鍵、置頂、開機自啟、標籤清單、資料夾 |
| `ToastWindow` | 熱鍵記錄後右下角的短暫提示（見 §5 F7） |
| `RecordEditorWindow` | 編輯單一筆記錄的來源與備註（見 §5 F14） |
| `GlobalHotkey` | 註冊／解除全域熱鍵，失敗時回報 |
| `TrayIconController` | 系統匣圖示（顯示今日次數）、右鍵選單 |
| `StartupManager` | 開機自動啟動（HKCU Run） |
| `SingleInstanceGuard` | 單一執行個體，重複啟動喚回既有視窗 |

## 4. 資料

位置：`%LOCALAPPDATA%\StopAnnoyingMe\`

- `data.db`：SQLite 資料庫
- `settings.json`：使用者設定

### 4.1 資料表

```sql
CREATE TABLE interruptions (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    occurred_at  TEXT NOT NULL,   -- 本地時間 ISO 8601，例 2026-09-16T14:32:05
    occurred_date TEXT NOT NULL,  -- 本地日期 yyyy-MM-dd，供索引與分組
    source       TEXT NULL,       -- 可選標籤（同事／主管／電話／會議／其他）
    note         TEXT NULL,       -- 可選備註
    created_at   TEXT NOT NULL    -- 寫入時間 ISO 8601
);
CREATE INDEX idx_interruptions_date ON interruptions(occurred_date);

CREATE TABLE schema_version (version INTEGER NOT NULL);
```

以本地時間字串儲存而非 UTC：使用者關心的是「上班日的哪個時段」，本地時間直接對應；跨時區不是本工具的使用情境。

### 4.2 設定欄位

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| `hotkey` | `Ctrl+Alt+D` | 全域熱鍵 |
| `hotkeyEnabled` | `true` | 是否啟用熱鍵 |
| `alwaysOnTop` | `true` | 視窗置頂 |
| `runAtStartup` | `false` | 開機自動啟動 |
| `minimizeToTray` | `true` | 關閉視窗時最小化到系統匣而非結束 |
| `duplicateGuardSeconds` | `3` | 去重秒數 |
| `sources` | 同事／主管／電話／會議／其他 | 可選標籤清單 |
| `windowLeft` / `windowTop` | `null` | 視窗位置記憶 |

## 5. 功能規格

| # | 功能 | 行為 |
|---|------|------|
| F1 | 記錄打擾 | 點主按鈕或按熱鍵 → 以當下本地時間新增一筆，今日次數 +1 |
| F2 | 今日次數 | 主畫面大字顯示，跨午夜自動歸零 |
| F3 | 最近記錄 | 顯示今日最近 5 筆時間（HH:mm:ss）與標籤 |
| F4 | 可選標籤 | 主畫面標籤列，點一下把標籤補到今天最後一筆；再點同一顆取消標記；不點不影響 |
| F5 | 撤銷 | Undo 刪除最後一筆（僅限今日最後一筆） |
| F6 | 去重保護 | 距上一筆未滿 `duplicateGuardSeconds` 秒 → 不記錄，顯示「剛剛已記錄」提示 |
| F7 | 全域熱鍵 | 背景可用，記錄後於右下角顯示 2.5 秒提示告知今日次數 |
| F8 | 系統匣 | 圖示上顯示今日次數；右鍵選單：+1／開啟主畫面／統計／設定／結束 |
| F9 | 置頂切換 | 主畫面可切換 always-on-top，設定持久化 |
| F10 | 統計 | 今日／本週（週一起算）／本月次數；24 小時分佈長條圖 |
| F11 | CSV 匯出 | 選擇期間匯出「發生日期, 發生時間, 星期, 來源, 備註」，UTF-8 with BOM（Excel 相容） |
| F12 | 開機自啟 | 寫入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| F13 | 單一執行個體 | 重複啟動時喚回既有視窗並結束新行程 |
| F14 | 編輯記錄 | 點「最近記錄」的任一列開啟編輯視窗，可改來源與備註（最多 500 字）。發生時間唯讀 —— 那是本工具唯一的事實依據，能改就失去意義。備註內容直接顯示在清單列上 |

## 6. 邊界條件與例外處理

| 情境 | 處理方式 |
|------|----------|
| 資料庫檔不存在 | 自動建立目錄與資料表 |
| 資料庫損毀（開啟失敗） | 更名為 `data.db.corrupt-yyyyMMddHHmmss.bak`，重建新庫，主畫面顯示警告 |
| 設定檔損毀或格式錯誤 | 使用預設值，將壞檔更名保留，不阻擋啟動 |
| 熱鍵被其他程式佔用 | 註冊失敗 → 主畫面顯示提示並提供改鍵入口，程式仍可用 |
| 快速連點 | 依 F6 去重，不寫入重複資料 |
| 跨午夜 | 每 30 秒檢查日期變更，變更時重算今日次數並更新匣圖示 |
| 無記錄時按 Undo | 按鈕停用，不產生錯誤 |
| 匯出檔案被佔用／無寫入權限 | 捕捉例外，顯示明確訊息，不中斷程式 |
| 系統匣不可用 | 降級為一般視窗模式，不影響記錄功能 |
| 開機自啟寫入登錄檔失敗 | 顯示錯誤訊息，設定回復為關閉 |
| `%LOCALAPPDATA%` 不可寫 | 啟動時顯示錯誤並指出路徑，不靜默失敗 |

## 7. 測試策略

xUnit 覆蓋 Core：

- `InterruptionRepository`：新增／查詢／刪除最後一筆；空資料庫查詢
- `DatabaseInitializer`：新建、既有庫重開、損毀檔復原
- `StatisticsService`：今日／本週（跨週邊界、週一起算）／本月（跨月邊界）；每小時分佈含 0 值時段
- `DuplicateGuard`：邊界值（剛好等於門檻秒數）、無前一筆
- `CsvExporter`：欄位含逗號與引號的跳脫、空資料
- `SettingsService`：檔案不存在、JSON 損毀、欄位缺漏

測試使用暫存目錄的獨立資料庫檔，互不干擾。

UI 以實際建置執行驗證：啟動、點擊記錄、熱鍵、系統匣、統計、匯出。

## 8. 視覺設計

配色、字體層級、按鈕與圖示樣式由 `codex exec` 產出，成果為
`src/StopAnnoyingMe.App/Themes/Design.xaml` 與 [../../design-notes.md](../../design-notes.md)。
對話框需要而主題未涵蓋的控制項（TextBox、CheckBox、對話框視窗樣式、統計長條）
補在 `Themes/Controls.xaml`，色彩一律沿用 Design.xaml 的 token，不另外定義顏色。

設計方向：深色為主、單一強調色（青綠 `#4FC3B6`）、主按鈕為畫面焦點；
文字對比皆達 WCAG AA（最低組合 5.36:1）。

視窗尺寸：主畫面 340×540。原始版面草圖為 340×460，但實作後確認
460 高度放不下 5 筆記錄（會出現捲軸），故加高到 540。

應用程式圖示由 `tools/generate-icon.ps1` 以設計文件中的幾何路徑渲染成
8 種尺寸組成 `.ico`，圖示調整時重跑腳本即可，不需手動修圖。

## 9. 明確不做（YAGNI）

- 雲端同步、多人統計
- 打擾時長計時（只記次數與時間點）
- 通知／提醒／專注模式
- 安裝程式（以資料夾綠色版發佈）
