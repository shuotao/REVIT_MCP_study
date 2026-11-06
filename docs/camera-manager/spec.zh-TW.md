# 相機管理面板 規格書（現行行為）

> 狀態：2026-09-26 現行版（Wave 3＋使用者實測第四輪）｜適用：Revit 2024（程式碼可編譯 R24–R26；R22/R23 有既有的 `CommandExecutor.LinkDisplay.cs` 編譯錯誤，與本功能無關）
> 本檔只寫「現在做什麼」。為什麼這樣決定 → [adr.zh-TW.md](adr.zh-TW.md)；何時改了什麼 → [changelog.zh-TW.md](changelog.zh-TW.md)；接下來做什麼、哪些還沒驗證 → [roadmap.zh-TW.md](roadmap.zh-TW.md)。
> 除非另外註明，本檔描述的行為都**尚未經 Revit 實機完整驗證**（見 roadmap「未驗證清單」）。

---

## 1. 定位

相機管理是 Revit 原生外掛功能：一個停靠面板（Dockable Pane）＋「一鍵 4 視圖」產生的 Key Plan 視圖。目標是讓使用者看得懂每台相機在哪、多高、朝哪，能直接調整、複製、出圖。

它**不是** MCP App。為了自動測試，另外提供 8 個 MCP 工具呼叫同一套服務程式碼（§12）。

| 問題 | 面板的回答 |
|---|---|
| 性質面板的 Eye Elevation 從內部原點量起，看不出離樓板多高 | 面板所有高度一律用「離樓板高度」；Eye Elevation 原始值只唯讀顯示 |
| 相機平常在平面上看不到、不能列印 | Key Plan 平面／剖面上畫相機標記（真的 Revit 詳圖元素，可列印） |
| 沒有總覽 | 清單＋③ 位置圖一次看到所有相機；每層一張「樓層總覽」平面把該層所有相機畫在一起（§10.10） |
| 每台相機要手動排圖紙、試不同角度要另開相機 | 一鍵出圖紙（§10.13）；具名「視角版本」存在 .rvt、可切回（§11.5） |

## 2. 名詞

| 名詞 | 意思 |
|---|---|
| 相機 | 專案中 `IsPerspective = true`、非樣板的 `View3D` |
| 建築樓層 | Level 的「建築樓層（Building Story）」＝是（`LEVEL_IS_BUILDING_STORY`）。一個都沒勾時退回全部 Level；略過非建築樓層時寫 log（`CameraMath.GetStoreyLevelsSorted`） |
| 下方樓層 | 相機的樓層（2026-09-26 第四輪，D-040）。**室內**（眼睛在房間內，§10.2）＝{房間的 Level} ∪ {房間垂直範圍內、有非樣板樓層平面的 Level} 中高程 ≤ 眼睛 − 300 mm 的最高者（多層挑空／樓梯間／樓中樓）；都不符合＝房間的 Level。**室外**＝往下射線打到的站立面 +300 mm 以下、有樓層平面的最高 Level；沒打到＝眼睛下方最近的建築樓層 |
| 離樓板高度 | 眼睛高度 − 下方樓層高程（cm） |
| 平面 X／Y | 眼睛的模型座標，**內部原點座標系**（cm） |
| 水平角 | 0° = 專案北（+Y），順時針為正 |
| 俯仰角 | 0° = 水平，正值往上看 |
| Key Plan | 一鍵 4 視圖為某台相機產生的平面／剖面／等角三張真實 Revit 視圖 |
| 相機標記 | B 型＝Key Plan 平面／剖面／樓層總覽上的註解標記（詳圖線＋填滿區域）；A 型＝一般模型族群「RevitMCP 相機標記」＋視野扇形（§10.11） |
| 樓層總覽 | `Key Plan-總覽-<樓層名>`：一個建築樓層一張、畫該層所有相機的平面（§10.10） |
| 室內／室外 | 眼睛在某個房間內＝室內，否則＝室外（§10.2） |
| 相機圖紙 | `Key Plan-<相機名>`：一台相機一張、放透視＋Key Plan 三視圖的圖紙（§10.13） |
| 視角版本 | 存在相機上的具名視角快照（例「A 案」），可還原（§11.5） |

## 3. 面板整體

- 以 `RegisterDockablePane` 在 Revit 啟動時註冊，預設停靠右側；功能區「MCP Tools → 相機管理」按鈕顯示面板。
- 寬度 300–600 px 可用，**沒有外層捲軸**；只有 ② 相機清單內部可捲動。註冊時宣告 `DockablePaneState.MinimumWidth = 300`（2026-09-26）。Revit API 沒有讀寫「目前停靠寬度」的方法，無法在並排後還原使用者拖過的寬度（D-043）。
- 單頁、無分頁，固定垂直順序，每區上方有 11px semibold 小標（⑥ 例外，只在狀態文字前加「⑥」）：
  「① 工具列」「② 相機清單」「③ 位置圖」「④ 視圖操作」「⑤ 人視高預設值」「⑥ 狀態列」。
- 每個按鈕／下拉／勾選框的 ToolTip 都以「區號＋名稱」開頭（例：「④ 一鍵 4 視圖：…」）。
- ② 與 ③ 之間有 `GridSplitter`；② 預設佔「清單＋位置圖」高度的 1/3（`CameraManagerSettings.CameraListFractionV3`），拖曳後記住。面板太矮時 ③ 先縮（最小 120px），② 其次（最小約 140px）。
- 色彩、字型、圓角、間距集中在 `MCP/UI/CameraManagerTheme.cs`（palette B：背景 `#F3F7FC`、卡片 `#FFFFFF`、邊框 `#DCE6F2`、文字 `#1C2B3A`、accent `#3B82C4`，10px 圓角、8px 間距）。所有容器都有顯式背景。
- 間距規則（2026-09-25 Wave 3）：區與區之間、④ 動作列按鈕之間、Popup 按鈕之間一律 8 px（`CameraManagerTheme.Spacing`）；① 工具列的小圖示按鈕群組內 4 px（300 px 寬時放得下）；卡片內距 8 px。
- 選單與 Popup 一律主題色、點外面即關閉；每個選單只建立一次重複使用；從選單開 Popup 一律延到選單關閉後（Dispatcher Background）。所有處理器 try/catch＋log；不套任何 WPF `Style` 到型別不符的元素。

### 3.1 載入與執行緒模型

- **懶載入**：開檔、`ViewActivated`、面板 `Loaded` 一律不讀、不匯出、不畫，停在「按『重新整理』載入相機」。使用者按「重新整理」（或點清單／地圖區）才讀快照＋匯出預設底圖。
- 之後同一份文件的 `DocumentChanged` 只做輕量清單更新（沿用現有底圖重畫標記，不重新匯出）。文件切換／關閉時清回中性狀態。
- 面板的 Revit 工作一律排入專用的 `CameraManagerEventQueue`（FIFO、不丟動作、每個動作獨立 try/catch、只寫 log 不跳 TaskDialog）；面板不使用 MCP 共用的 `ExternalEventManager`。
- 結果回 UI 一律 `Dispatcher.BeginInvoke(Background)`；快照內的 Revit `Transform` 一律攤平成純 double，不把 Revit API 物件帶到 WPF 執行緒。
- 第一次「重新整理」時做一次 ES Schema 自我檢查，結果寫 RevitMCP log（OK／FAIL）。
- 診斷：`CameraManagerDiagnostics` 以 WriteThrough 同步寫 `%APPDATA%\RevitMCP\Logs\CameraManagerCrash_yyyyMMdd.log`（AppDomain／FirstChance（有條件）／UnobservedTask／Dispatcher 例外、版面風暴偵測）。

## 4. ① 工具列

單列：搜尋框（吃剩餘寬度，依名稱篩選）｜重新整理｜從透視圖同步（在透視圖內導覽後清單沒更新時強制重讀）｜「⋯」選單（匯出 Excel、開啟預覽視窗）。

## 5. ② 相機清單

### 5.1 預設欄位（300–600px 不出現水平捲軸）

| 欄位 | 行為 | 寬度 |
|---|---|---|
| 名稱 | 第一欄凍結；唯讀；超長橢圓＋ToolTip 全名。**單擊＝只選取；雙擊＝一鍵 4 視圖**（2026-09-26，D-043；不進編輯） | 星號，最小 45px |
| 樓層 | 下方樓層（室內＝房間的 Level；該 Level 不是建築樓層時也補進下拉清單）；常駐 `ComboBox`。**選另一個樓層＝複製相機到該樓層**（§11.1），原相機不動 | 依最長樓層名量字寬，64–140px |
| 離樓板 cm | 1 位小數、右對齊；< 120 或 > 200 時整列標色＋ToolTip | ~50px |
| 水平角 ° | 右對齊 | ~50px |
| 俯仰角 ° | 右對齊 | ~50px |

清單下方一行次要色提示：「雙擊名稱開 4 視圖・點數字直接修改・右鍵更多」（2026-09-26；省略號＋完整 ToolTip 保險，ToolTip 另說明單擊＝選取、改樓層＝複製、右鍵選單內容、橘色列與粗體列的意思）。
列 ToolTip 前綴顯示「室內／室外」，室外另附扇形半徑；另附「樓層判定」說明。
- **橘色列**＝離樓板高度 < 120 cm 或 > 200 cm（列 ToolTip 寫明；300 px 提示列放不下，圖例放在 ① ⋯ 選單的停用項目）。
- **目前正在看的相機**（作用中視圖是它的透視圖）＝粗體＋左側 4 px accent 條（不改底色，保留選取中的藍底）；`ViewActivated` → Idling 跟隨路徑更新，作用中視圖不是透視相機時清除（2026-09-26，D-043）。

### 5.2 選用欄位（右鍵欄標題勾選；本機記憶；預設全部隱藏）

| 欄位 | 可編輯 |
|---|---|
| 平面 X cm、平面 Y cm（內部原點座標） | 是 |
| 遠裁剪 cm | 找不到可寫的遠裁剪參數時該列唯讀（灰字） |
| 視野角 °（估算） | 唯讀，絕不寫回 |
| Eye Elevation 原始值 | 唯讀；欄標題 ToolTip「Revit 原始值，從內部原點量起」 |

### 5.3 編輯機制（常駐控制項，不走 DataGrid 編輯模式）

- DataGrid 全唯讀；可編輯欄是 `DataGridTemplateColumn`，儲存格內常駐 `TextBox`／`ComboBox`（程式碼建 `FrameworkElementFactory`，無 XAML）。
- 數字欄：點一下＝取得焦點＋全選＋選取該列（地圖高亮，**不**觸發一鍵 4 視圖）。Enter 或失焦＝解析（先 Invariant 再 CurrentCulture，接受全形逗點／句點）→ 與列值差 ≤ 0.05 視為沒變、不開交易 → 送出；Esc＝還原。
- 樓層欄：下拉展開中選到另一個樓層（`SelectionChanged`）或下拉關閉（`DropDownClosed`）先到者提交一次；下拉未展開時滾輪／上下鍵不換值。
- 任何儲存格有焦點或下拉展開中，清單重建（`SetRows`／`SetLevels`／`Items.Refresh`）延後到焦點離開。
- **編輯快速路徑（2026-09-26 第六輪，核心功能：改離樓板高度後透視圖要馬上變）**：數字欄、平面 X/Y、遠裁剪、③ 地圖拖曳共用 `CameraManagerService.ApplyPoseEditFast`，只帶「這次改的欄位」（`CameraPoseEdit`，其餘沿用**相機目前實際值**，不再用清單列補齊）。排進相機管理佇列的**優先佇列**（插隊到讀快照／匯出底圖／相機變動後更新之前）→ 一個小交易「編輯相機」：第一次修改前自動存「A案」（只寫 ES JSON）＋一次 `SetOrientation`（＋遠裁剪）；不找房間、不讀其他相機、不碰 Key Plan／總覽／標記 → 立刻重繪透視圖 → 讀回實際值 → UI 端**就地更新這一列**（逐格 UpdateTarget＋列樣式，不換 ItemsSource；正在輸入的那一格不動），平面位置／水平角有變時 ③ 標記用現有底圖重畫。`DocumentChanged` 看到「只有剛就地更新過的相機、交易名＝編輯相機」→ 不排整份快照重讀。改角度時眼睛位置不動、上方向重算成不滾動。log：「⏱ 高度編輯 → 視圖更新 total=…ms [排隊, 交易, 重繪]」與「⏱ 高度編輯 → 清單列就地更新 …ms」。
- log：每次提交「清單編輯：<相機> <欄位> <舊>→<新>」、解析失敗、取得焦點、第一個按鍵與第一個文字輸入。

### 5.4 右鍵選單（清單列與地圖標記共用）

一鍵 4 視圖／僅打開透視圖（2026-09-26：切到透視圖並**關閉其他所有視圖**，同一鍵 4 視圖的關閉邏輯）／**一鍵出圖紙**（§10.13）／**視角版本 ▸**（§11.5）／重新命名…（主題化輸入 Popup，拒絕重名）／複製到其他樓層…／重設剖面位置／**剖面轉 90°（另一向）**（可勾選，§10.5.3a）。每個項目都有 ToolTip。

### 5.5 預設選取

每次重新整理若沒有選取：作用中視圖是透視相機就選它，否則選第一列。

## 6. ③ 位置圖（迷你 key plan）

### 6.1 版面

吃掉剩下高度。右上角一條半透明浮動工具列：小型底圖 `ComboBox`＋切換鈕「全」（顯示全部平面）、「層」（顯示其他樓層）、「⤢」（符合畫面）、「框」（顯示對位框，預設關、不存設定）。

### 6.2 底圖清單

- 只提供**已啟用裁剪框**的平面（樓層平面、天花平面、面積平面）。整份文件都沒有時改畫中性格線、依相機分布自動縮放，此時不做樓層過濾。
- 預設（「全」關）只列有相機的樓層、每層一張，標籤「<樓層名> — <平面名>（N 台相機）」；多個樓層挑到同一張時合併、相機數相加。目前選取的底圖不在精簡清單時附加在最後。

### 6.3 自動挑底圖（`BaseMapPlanChooser.Choose`）

預設底圖、跟隨作用中透視圖、選取相機自動切換、精簡清單每層一張，全部用同一個挑法：

| 步驟 | 規則 |
|---|---|
| 0 | 相機放置時的平面（放置當下由 `DocumentChanged` 讀作用中視圖，Idling 時以 ES `RevitMCP_CameraSourcePlan` 記在相機上）。已裁剪直接用；未裁剪改找其父／從屬／同主視圖中包含相機的已裁剪平面 |
| 1 | 同層、裁剪框包含眼睛 XY 的樓層平面（非從屬優先、面積最大） |
| 2 | 其他樓層高程在 ±1.5 m 內且包含眼睛（高程最近、面積最大） |
| 3 | 眼睛下方任一建築樓層且包含眼睛（最高樓層、面積最大） |
| 4 | 同層面積最大；再退到最近的下方、上方樓層 |

- 預設底圖（多台相機）＝相機最多的樓層，用最多台相機共用的放置平面當步驟 0 提示。
- 跟隨／選取相機時，目前底圖若同樓層且已包含該相機就不換。
- log 記選中平面名稱、id、裁剪寬×高（m）、步驟與理由。

### 6.4 底圖匯出（確定性）

`CameraManagerService.ExportBaseMap` 在 `TransactionGroup` 內建立暫存複本平面（不含詳圖）：拿掉視圖樣板、`CropBoxActive=true`／`CropBoxVisible=false`、裁剪形狀還原成矩形、開啟註記裁剪且四邊外擴設到 Revit 允許的最小值（以讀回值為準）、以視圖層級隱藏會伸出裁剪框的註記類別（格線、樓層、剖面／立面／詳圖索引／視圖標記、尺寸、高程／座標點、文字、參考平面、範圍框、接圖線、所有 `*Tags`）→ Commit → 在沒有交易的狀態下以 `ImageExportOptions`（FitToPage、PNG、1600px）匯出 → RollBack 整個 group（不留復原紀錄）。group 內匯出失敗時改走「Commit → 匯出 → finally 用新交易刪除暫存視圖」；暫存視圖建不出來才退回舊版推測範圍匯出。

- 讀 PNG 檔頭取得實際像素，在「模型裁剪框」與「模型裁剪框＋最小註記外擴」兩者中挑長寬比較接近圖片者；兩者都寫 log。
- 底圖用 `ImageBrush.Stretch=Fill` 填滿「寬高＝像素數」的畫布，與 PNG 的 DPI 中繼資料脫鉤。
- 模型→像素：`local = CropBox.Transform⁻¹·eye`；`px = (local.X−Min.X)/(Max.X−Min.X)·W`；`py = (Max.Y−local.Y)/(Max.Y−Min.Y)·H`。
- 匯出期間 `IsExportingBaseMap=true`，`DocumentChanged` 忽略這段期間的變更。
- 底圖只在「重新整理／使用者選底圖／使用者點選相機（且需要換圖）」時匯出；進行中的新要求排隊合併。
- **底圖點陣快取**（2026-09-26，D-041）：以平面 Id 為鍵保留最近 6 張匯出結果；同一份文件、模型 epoch（相機以外的元素變更次數）沒變、底圖是本工具視圖（Key Plan／樓層總覽）時本工具視圖 epoch 也沒變 → 直接用記憶體點陣重畫，不呼叫 Revit。「重新整理」與換文件時清空。log：「⏱ 底圖快取命中 … ms」／「⏱ 底圖匯出 …」「⏱ 底圖疊圖 …」。
- 對位健檢：換算範圍長寬比與圖片長寬比差 > 1% 時狀態列附加警示。「框」模式畫藍虛線（換算範圍）＋紅虛線（模型裁剪框）。

### 6.5 標記與樓層過濾

- 每台相機：圓點（選取中＝accent、其他＝次要色，白 1.5px 外框）＋方向桿＋眼睛楔形（alpha≈70%）＋視野扇形（~15% 填色＋細邊線）；扇形用非對稱左右邊界角（§10.3）。選取變色即時重上色。
- 室外相機另畫一個**模型比例**的虛線扇形（半徑＝室外扇形半徑，隨底圖縮放）。
- 只畫「底圖所在樓層」的相機；底圖取自別的樓層時，共用挑法會挑到這張底圖的相機樓層也視為同層。「層」開啟後其他樓層相機淡化顯示（不透明度 ~55%、標籤加「（樓層名）」、不參與選取變色）。
- 相機標記落在底圖外時左下顯示「相機不在這張底圖範圍內」。
- 選取的相機若已有 Key Plan 剖面，疊畫一條 accent 虛線代表剖面**目前實際**位置（讀剖面 `CropBox.Transform`，反映手動調整）。沒有 Key Plan 剖面的相機不排這個讀取。

### 6.6 跟隨作用中透視圖

作用中視圖變成透視相機（專案瀏覽器開啟、一鍵 4 視圖、點並排視窗…）→ `ViewActivated` 只設旗標 → 下一次 Idling（live sync 已啟用、同一文件、面板顯示中）→ 程式化選取該列；底圖樓層不同才切換；地圖平移置中（已在可視範圍內則不動；換底圖時等底圖畫好再置中）。作用中透視圖還不在清單（剛放的新相機）→ 記為待跟隨，快照套用後自動跟隨；600ms 內沒有快照就自己排一次輕量刷新（不匯出底圖）。

### 6.7 縮放／平移

- 單一 `MatrixTransform` 套在整個地圖內容層；每個相機標記另包一層 `Canvas` 以 `ScaleTransform(1/scale)` 反向抵銷，標記永遠是固定螢幕像素大小。
- 預設「符合畫面」；地圖區大小改變且使用者沒手動調整過時自動重新符合；換底圖／重畫時重設。
- 滾輪以游標為中心縮放（每格 ×1.2，限制在符合畫面倍率的 0.5×–20×）；左鍵或中鍵拖曳平移（門檻 4px）；雙擊空白處＝符合畫面。
- （2026-09-26，D-043）左鍵**單擊**相機圓點＝選取（同清單單擊）；**雙擊**圓點＝一鍵 4 視圖；右鍵圓點＝§5.4 選單；空白處單擊不做事、雙擊空白＝符合畫面。
- **拖曳改相機**：按在「已選取」相機的圓點上拖曳（門檻 4 px）＝移動；Shift＋拖曳，或拖視線末端的旋轉把手（只有選取中的相機顯示）＝旋轉。拖曳中只移動／旋轉 WPF 畫布上的圓點、楔形、扇形，**完全不呼叫 Revit**；放開時以地圖反轉換算回模型 XY（或水平角），送出**一次**編輯（與 `set_camera_pose` 同一路徑：離樓板高度與方向不變），之後照常去抖更新標記／Key Plan（§9）。Esc 或滑鼠捕捉中斷＝取消並還原標記。拖曳中不重畫標記；底圖不重新匯出（樓層沒變時沿用目前底圖）。按在空白處仍是平移，未選取的相機第一下只選取不拖曳。

## 7. ④ 視圖操作

單列：

| 控制項 | 行為 |
|---|---|
| 一鍵 4 視圖（主鈕） | §10 |
| ▾ 選單 | 等角範圍（整層（見上層樑）／只到當層（不含上層））、等角近側（相機位置（預設）／牆內側／保留，§10.6）、剖面方向（對齊房間長邊（預設）／沿視線）、剖面位置（相機位置（預設）／視野中心，§10.5.3）、**剖面轉 90°（另一向，目前選取相機；沒有選取時停用，§10.5.3a）**、室外範圍半徑… (m)（預設 60，有效 5–1000）、重設剖面位置、全部符合畫面、**更新樓層總覽**（有選取相機＝該相機的樓層；沒有選取＝所有有相機的樓層；只更新一張時自動開啟它，§10.10）、**一鍵出圖紙**（§10.13）、**圖紙圖框 ▸**（自動（最常用）＋專案內每個圖框類型，附已使用張數；本機設定 `SheetTitleBlockTypeId`，0＝自動；清單在「重新整理」時讀取） |
| 並排 | 開啟選取相機的透視圖＋下方樓層一張**既有**平面（已開啟 > 已裁剪 > 名稱排序；找不到就報錯，不新建），先關閉其他視圖，再 `PostCommand(TileViews)` |
| 還原 | 關閉並排額外開的視圖、切回之前的作用視圖；已被關掉的其他視圖不會重新打開 |
| ⋯ 更多 | 複製到其他樓層…、**相機標記 ▸（B 詳圖（預設）／A 模型／A+B，§10.11）**、標記顏色 ▸（藍 `#3B82C4` 預設／紅 `#D9534F`／橘 `#E8A33D`／綠 `#4CAF7A`／紫 `#8E6BBF`／黑 `#1C2B3A`／自訂…）、Key Plan 樣式 ▸（淡化背景（預設）／原始）、⑤ 人視高預設值…、匯出 Excel、開啟預覽視窗 |

- 所有設定存 `ServiceSettings.CameraManager`（本機 JSON），即改即存。
- 一鍵 4 視圖有重入防護：同一時間只跑一個，連點只執行第一次，狀態列提示「正在建立 4 視圖，請稍候...」；旗標以 `try/finally` 保證清除。
- 「全部符合畫面」立即對選取相機已開啟的 4 張視圖做 `ZoomToFit`。
- 標記顏色：單一交易「變更相機標記顏色」同時改線型「相機標記線」的 `LineColor`、「相機標記-實心」前景色，以及所有既有扇形／楔形填滿的元素取代色（保留各自透明度）。自訂色用 `System.Windows.Forms.ColorDialog`（反射呼叫，不新增 WinForms 參考）。

## 8. ⑤ 人視高預設值

「⋯ 更多 → ⑤ 人視高預設值…」開主題化 Popup：數值 cm（預設 160，本機設定）＋「套用到選取」＋「套用到全部」（後者先確認）。只改眼睛 Z（保持 XY 與方向），一個交易「套用相機人視高」。

## 9. ⑥ 狀態列與雙向連動

- ⑥ 顯示最近一次動作的結果或錯誤，前面加 `HH:mm`；ToolTip＝全文。live sync 的輕量清單刷新**不**覆寫狀態列（只有「重新整理」或換文件時顯示「共 N 個透視相機」），所以動作結果會一直留著直到下一個動作（2026-09-25 Wave 3）。
- 面板改數值 → 相機立即更新；Revit 內改相機（平面拖曳、改性質）→ `DocumentChanged` → 下一次 Idling 輕量刷新清單與 ③ 標記。自己的修改以抑制旗標避免迴圈。
- **相機變動後更新（2026-09-26 第四輪改為增量，D-041）**：來源有三——`DocumentChanged` 收集被修改的透視 `View3D`（事件內不改模型）、清單／地圖編輯成功後（附變動前姿態）、**導覽監看**（下一條）。面板 `DispatcherTimer` 去抖 **800 ms**、同相機合併（變動前姿態只留最早一筆）→ 佇列工作 `CameraKeyPlanService.UpdateCamerasAfterChange`：
  1. 導覽來源且相機沒有任何視角版本 → 先把「移動前」姿態存成「A案」（交易「儲存相機視角版本」）。
  2. **分級（2026-09-26 第六輪取代「移動 > 300 mm 或轉 > 5° 就重算 Key Plan」）**：與上次畫標記的姿態比對（本 session 沒有紀錄時用變動前姿態＝清單列舊值），由輕到重——**只有高度變** → 只重畫剖面上的標記群組；**俯仰角變** → 剖面標記＋A 標記（A 族群有俯仰角參數）；**水平角／視野（裁剪框、遠裁剪）變** → 平面＋剖面標記（＋總覽／A）；**平面移動、仍在同一房間且 ≤ 房間外框最大邊長** → 平面標記＋剖面重新對位（`RealignSectionForCamera`：同一套剖面規格，剖面框跟著眼睛移，手動移過的剖面保留；不動平面裁剪／等角）；**換樓層、換房間、超過房間大小**（沒有房間紀錄時沿用 > 300 mm）→ 一鍵 4 視圖的更新路徑（交易「建立相機 Key Plan」，**不並排**）。高度／俯仰角的編輯**永遠不走**完整 Key Plan（除非高度讓相機換了樓層）。換樓層／房間後面板補一次清單重讀（樓層欄、室內外）。log：「⏱ 相機變動後續更新（去抖；N 台）總計 … ms（重畫：…）」，每台一行「→ 只重畫剖面標記（相對上次標記：平面 0mm、高度 200mm…）」。
  3. 標記重畫 → `UpdateMarkersForCameras`（可只畫指定群組 `MarkerParts`）（單一交易「更新相機標記」）：姿態雜湊（眼睛、方向、裁剪框、遠裁剪＋模型 epoch＋標記型式）與上次畫標記時相同 → 整台略過；只重畫該相機 Key Plan 平面／剖面標記；簡化樓層記號只在相機樓層變了（或沒有紀錄）才重畫；「只留本相機剖面／隱藏其他 viewer」只在強制重畫（切換相機標記型式）時做；**不動**裁剪框、剖面範圍、等角 Section Box。
  一鍵 4 視圖進行中時延後再試。每步 `⏱` 計時寫 log。
- 同一個交易也重畫這些相機在**樓層總覽**上的標記：知道上次畫在哪一層時只清「舊樓層＋新樓層」兩張總覽上這台相機的標記（不知道才清全部），再畫到目前樓層的總覽（該層沒有總覽不新建）；相機標記含 A 時只同步**變了的相機**的 **A 型標記**（§10.11；即時更新不重驗扇形樣式、沒有新放置就不重跑透視圖隱藏）。總覽／A 標記存在時，沒有 Key Plan 的相機也會處理。
- **導覽監看（2026-09-26，D-042）**：Navigation Wheel／繞行／ViewCube 移動透視相機時 Revit 不發 `DocumentChanged`。`Application` 的 Idling 對作用中透視相機每 ≥ 300 ms 讀一次姿態雜湊（只讀）；變了 → `SetRaiseWithoutDelay` 直到停住；停住 1 秒不再變 → 排入上面的相機更新，更新後補一次輕量清單刷新。使用者還在動時不開任何交易。
- **清單／`set_camera_pose` 編輯後重繪透視圖**：相機是作用中視圖 → `RefreshActiveView`；只是開著 → `UpdateAllOpenViews`（API 沒有單一 UIView 的重繪方法；`UIView.ZoomAndCenterRectangle` 在透視圖會改相機，不能拿來「只重繪」）。`set_camera_pose` 回傳 `Timing {transactionMs, refreshMs, totalMs}`（只量快速路徑），後續更新改為 800 ms 去抖（面板啟用時交給面板的同一個計時器，否則內建計時器經相機管理佇列執行），不在回應中同步執行。
- **拖 A 標記 → 相機**（§10.12）與**刪除後清孤兒標記**也排進同一個去抖計時器。
- **快照合併與快取（2026-09-26）**：佇列中已有尚未開始的快照讀取時，新的快照要求（含 live sync）合併進去；房間／樓層判定以「相機＋眼睛座標（1 mm）＋模型 epoch」快取（`⏱ 讀相機快照 … 房間/樓層快取 命中 N、重算 M`）。模型 epoch 在 `DocumentChanged` 變更了相機以外的元素時 +1（本工具自己的交易不算）。
- 回饋迴圈防護：`IsUpdatingMarkers` 旗標＋交易名稱比對（忽略「更新相機標記」「更新 Key Plan 樓層線」「更新相機 A 標記」「載入相機標記族群」「更新樓層總覽」「記錄相機放置平面」「建立相機圖紙」「儲存相機視角版本」「刪除相機視角版本」；**不**忽略「由標記更新相機」與「還原相機視角：<名稱>」，因為相機改了之後標記要跟著重畫）。
- **樓層記號同步**：主體 Level 改高程／改名 → 去抖排入交易「更新 Key Plan 樓層線」，依目前 Level 值重畫剖面的簡化樓層記號（§10.5.5）。
- 透視圖內純滑鼠導覽不觸發 `DocumentChanged`（使用者實測），改由導覽監看處理；仍保留「從透視圖同步」按鈕當保險。

## 10. 一鍵 4 視圖（Key Plan）

實作：`CameraKeyPlanService.CreateOrUpdateFourViews`（建立／更新）＋`CameraManagerService.OpenAndTileFourViews`（開啟並排）。面板與 MCP 工具 `run_camera_keyplan` 共用。

### 10.1 流程

1. 交易外算好所有範圍（相機參數、FOV、眼睛所在房間、扇形半徑、等角 XY、本層頂），全部寫 log。
2. 單一交易「建立相機 Key Plan」：平面 → 表現法 → 平面標記 → 剖面 → 表現法 → 剖面標記 → 平面只留本相機剖面 → 等角。平面／剖面標記與表現法各自包 `SubTransaction`＋try/catch，失敗只回滾那一步並記 log。整體失敗則回滾整筆交易並回報原因。
3. 依序啟動透視／平面／剖面／等角 → 關閉作用中文件其他所有視圖（每張獨立 try/catch）→ 等角 `ZoomToFit` → `PostCommand(TileViews)` → `CameraViewZoomService` 訂閱 Idling，等視窗大小穩定（連續兩個 tick 相同，最多 20 tick）後對每張 `ZoomToFit`。透視圖採防護式：前後比對 `GetOrientation()`，有變就還原並記 log，本工作階段之後跳過透視圖。**2026-09-26**：目前開著的視圖剛好就是這 4 張 → 不啟動、不關閉、不重新並排，只對 4 張 `ZoomToFit`（重複點同一台相機不再每次重新並排）。

4. （2026-09-25 Wave 2）交易成功後，另開交易「更新樓層總覽」更新這台相機樓層的總覽（§10.10）；⋯ 相機標記含 A 時再開交易「更新相機 A 標記」同步 A 型標記（§10.11）。兩者失敗都只記 log，不影響 4 視圖。**2026-09-26**：該樓層所有相機（姿態、名稱）與模型 epoch 自上次自動更新總覽後都沒變、總覽也還在 → 略過總覽；標記型式＝B（面板設定）→ 不碰 A 標記（MCP `markerMode=B` 單次覆寫時才刪這台相機的 A 標記）。
5. （2026-09-26）各步驟計時：`⏱ 一鍵 4 視圖（Key Plan 建立/更新） 總計 … [相機參數/房間, 範圍計算, Key Plan 交易, 樓層總覽, A 標記]`、`⏱ 一鍵 4 視圖：開啟／關閉其他／並排`、`… ZoomToFit 完成 … ⏱ 排程到完成 N ms`。

視圖命名：`Key Plan-<相機名>-平面`／`-剖面`／`-等角`。對應關係用 ES（§10.9）記在視圖元素上，相機改名後重跑仍更新同一批視圖；找不到 ES 連結時退回精確名稱比對，都找不到才新建。

### 10.2 室內／室外判定

`FindEyeRoom`（`MCP/Core/CameraKeyPlanService.EyeRoom.cs`，2026-09-25 MCP 自測後改寫，D-031）：

1. 候選＝已放置房間（面積 > 0、與透視圖 `VIEW_PHASE` 同一個 Phase）中，BoundingBox XY 含眼睛者（前篩），再以房間邊界線段（`Tessellate`，所有迴圈奇偶規則）判定眼睛 XY 在多邊形內。
2. 候選中垂直範圍 [底, 頂] 含眼睛高度者優先（多個取底最高者）；都不含 → 取垂直距離最近且 ≤ 2.0 m 者（例：眼睛比房間底低 150 mm、閣樓相機略高於房間上限）。底＝房間 Level 高程＋底部偏移；頂＝底＋`UnboundedHeight`（取不到用 BoundingBox 頂）。
3. 同 Phase 沒有任何 XY 候選 → 不分 Phase 再找一次並記 log。
3a. **最近房間（2026-09-25 第二輪）**：眼睛 XY 不在任何房間輪廓內（相機在未放房間的樓梯間／挑空，或剛好在邊界外）→ 取「眼睛到房間邊界的水平距離 ≤ 1.5 m、且垂直範圍含眼睛（或差 ≤ 2 m）」的最近房間，判為室內；`foundBy`＝「最近房間 d=…mm」。Key Plan 的房間範圍同時包含眼睛。都沒有 → `Room.Log` 附「最近房間：水平 1500mm 內無符合垂直條件的房間」。
4. 找到＝室內，否則＝室外。挑選結果（候選、垂直範圍、距離、判定方式）寫進 `Room.Log` 與 log（同一相機＋房間只記一次）。房間輪廓＝邊界線段（取不到用 BoundingBox）；房間頂＝房間 BoundingBox Max.Z。

5. （2026-09-26，D-040）**房間頂以上的候選**：眼睛高於候選房間頂時，往下打站立面；站立面 ≥ 房間頂 − 300 mm、且 > 房間底 + 1 m（站在房間上方另一層樓板／屋頂上，而不是這個房間自己的地板；例：屋頂花園相機與下一層房間）→ 排除該房間（最近房間 fallback 也套用）。

樓層（2026-09-26，D-040；`CameraMath.ComputeGeometry` → `ApplyIndoorStorey`，清單、③ 位置圖跟隨、Key Plan、樓層總覽分組、剖面樓層記號、A 標記、人視高套用、複製相機都用同一個值），離樓板高度以該樓層計算：
- **室內**：{房間的 Level} ∪ {房間垂直範圍 (底, 頂] 內、至少有一張非樣板樓層平面的 Level} 中，高程 ≤ 眼睛 − 300 mm 的最高者；都不符合＝房間的 Level。例：大廳樓梯房間掛 L1、往上跨到 L3，眼睛在 L3 → L3。
- **室外**：`ReferenceIntersector`（非透視、非樣板、沒開剖面框的 3D 視圖，優先名稱「{3D}」；類別 樓板／屋頂／地形／基地／建築墊／Toposolid；含連結模型）從眼睛往下 `FindNearest` → 站立面 Z；樓層＝有樓層平面、高程 ≤ Z + 300 mm 的最高 Level，沒有＝高程最接近 Z 的 Level；沒打到＝眼睛下方最近的建築樓層。射線結果（Z、打到的元素、3D 視圖）寫 log。
- 結果以「相機＋眼睛座標（1 mm）＋模型 epoch」快取；樓層有變才寫 log。`list_cameras` 回傳 `LevelSource`（room／roomSpan／surface／elevation）、`LevelByElevation`、`StoreyLog`、`StandingSurface`（ZMm、Log）。

### 10.3 視野角與扇形

- `CameraMath.ComputePerspectiveFov`：候選 C＝裁剪形狀每個頂點相對眼睛分解成 right/up/forward 取 atan2；A＝CropBox 寬高在前裁剪面深度；B＝在後裁剪面深度。採用 C → A → B，全部寫 log。
- 另回傳四個帶號邊界角：`LeftDeg`／`RightDeg`（相機右為正）、`DownDeg`／`UpDeg`（上為正）。使用者只拖一邊裁剪框時視錐不對稱，扇形一律用這四個角；退回 A/B 時為對稱 ±h/2、±v/2。
- 畫面中心線＝左右緣角平分線（非對稱時 ≠ 相機正前方）。
- 扇形半徑（平面與剖面共用）：
  - 室內：中心線離開眼睛所在房間的距離 +0.6 m，夾在 [1.5 m, 遠裁剪]；算不出來＝min(遠裁剪, 8 m)。
  - 室外：min(遠裁剪, 室外範圍半徑)，下限 1.5 m。
- 相機沒有可用遠裁剪值時預設 20 m。

### 10.4 平面 `Key Plan-<相機>-平面`

- 從下方樓層的既有平面 `Duplicate`（樓層平面優先；已開啟 > 已裁剪 > 名稱排序）；沒有就用樓層平面類型新建。
- 清除繼承的視圖樣板、確保 Sections 類別可見。
- **樓層不符重建（2026-09-26 第五輪）**：已連結平面的 GenLevel（不可改）≠ 相機目前樓層 → 退役舊平面（沒放在任何圖紙上＝刪除；在圖紙上或為作用中視圖＝清掉本相機標記、解除 ES 連結、改名 `<原名>-舊樓層`），再走上面同一條複製路徑重建，後續樣式／標記／「只留本相機剖面」都作用在新平面。log「Key Plan 平面樓層不符（舊 X → 新 Y）→ 重建」；`list_cameras` 的 `KeyPlan.PlanLevelMismatch` 可偵測。樓層總覽同理：既有總覽 GenLevel ≠ 本次底圖（挑選器結果；無結果＝本樓層）的樓層 → 退役後重建。標記更新（相機移動）交易不重建，只記 log 提示。
- **室外相機視圖範圍（2026-09-26，D-040）**：有站立面時，平面的剖切面＝站立面 + 1200 mm、底與視圖深度＝站立面 − 1000 mm、頂＝剖切面 + 1000 mm（全部相對相機樓層），讓屋面與女兒牆看得到；前後值寫 log。
- **視圖整理（每次建立／更新都做，在設裁剪框之前，2026-09-25 MCP 自測後）**：`VIEWER_VOLUME_OF_INTEREST_CROP`＝無（解除 Scope Box；否則 CropBox 設定被忽略、裁剪停在 Scope Box 範圍、比例被迫 1:200）、方位（`PLAN_VIEW_NORTH`）＝專案北、移除非矩形裁剪、裁剪框若仍旋轉就轉正、圖紙上的標題（`VIEW_DESCRIPTION`）＝「Key Plan 平面」（不沿用來源平面的標題）。`CameraKeyPlanService.NormalizeDuplicatedPlan`。
- 裁剪框設完一律讀回：寬／高或角點位移與預期差 > 1 % → log `WARNING` 並附可能原因（Scope Box 仍鎖定／非矩形／樣板／從屬視圖），同時放進 `run_camera_keyplan` 的 `Warnings`。
- 裁剪框只改範圍（沿用來源平面座標系）：
  - 室內：眼睛所在房間 +0.6 m，必要時外擴包住完整扇形（弧上 16 點取樣，外留 0.5 m）。
  - 室外：非對稱扇形（眼睛＋兩緣＋弧上 16 點）外接框 +2 m。
  - 都算不出來：扇形特徵點 +2 m。
- **只顯示本相機剖面**：找出本相機剖面的 viewer（依序 `GetDependentElements(OST_Viewers)` → viewer 名稱＝剖面名稱 → `VIEW_NAME` 參數；log 記比對依據），其他所有 viewer（剖面／立面／詳圖索引，2026-09-25 起不再只限剖面）與立面標記本體（`OST_Elev`）逐元素隱藏，並隱藏立面／詳圖索引類別；視圖自己的裁剪框元素（同名 viewer）不動；找不到本相機 viewer 時本次不隱藏任何標記。`HideForeignViewers`。
- 確保本相機剖面線看得到：剖面「在比例粗於…時隱藏」設成可接受的最粗值（5000 → … → 200 依序嘗試）、Sections 類別可見、viewer 元素取代（非半色調、標記色、線寬 5）。

### 10.5 剖面 `Key Plan-<相機>-剖面`

#### 10.5.1 方向

| 模式 | 規則 |
|---|---|
| 對齊房間長邊（預設） | 候選方向＝平面 X 軸＋每條邊界線段方向，取投影外接矩形面積最小者；沒比軸對齊框小 10% 以上就用軸對齊。長短邊差 < 15%（近正方形）→ 取與相機前方向較接近的軸。方向號誌與相機前方向同側（剖面上視線朝右）。剖切線通過「可見範圍中心」＝中心線上半個扇形半徑處（Z＝眼睛高） |
| 沿視線 | 剖切平面包含通過眼睛的畫面中心線 |
| 室外 | 一律沿視線（忽略設定） |
| 室內但找不到房間 | 沿視線 |

#### 10.5.2 範圍

- 水平：對齊房間＝房間沿該軸投影範圍 ±0.6 m；沿視線＝中心線穿越房間外接框的區段 ±0.6 m、眼睛後方至少 1 m；室外＝眼睛後 2 m … 扇形半徑。
- 垂直：室內＝樓層 −1 m 到房間頂 +0.5 m（無房間：上層樓板底 +0.5 m，再算不出來＝眼睛 +3 m）；室外＝與等角相同 Z 範圍。
- **必含平面剖切面**：Key Plan 平面 View Range 剖切面的絕對高程不在垂直範圍內時外擴 0.5 m（否則 Revit 不在平面上畫剖面線）。
- 深度：前後各 1.5 m；眼睛超出深度時外擴到含眼睛（+0.5 m），讓眼睛標記仍在範圍內。（「視野中心」位置；「相機位置」見 10.5.3）

#### 10.5.3 位置（2026-09-26 第五輪，④ ▾「剖面位置」，本機設定 `SectionAnchor`；`run_camera_keyplan` 的 `sectionAnchor` 可單次覆寫）

| 設定 | 規則 |
|---|---|
| 相機位置（預設） | 剖切面剛好通過相機眼睛（原點＝眼睛，方向依 10.5.1）；水平範圍同 10.5.2、改為相對眼睛。深度＝剖切面（區域 Max.Z＝0，通過眼睛）往看向側延伸扇形半徑（至少 1 m）。對齊房間長邊時若畫面中心線落在剖切面背後，軸反向（範圍對調）讓相機前方的扇形在剖面裡。Revit 的剖切面＝區域 Max.Z，所以「眼睛後方再留 300 mm」與「剖切面通過眼睛」互斥，取後者；眼睛標記是詳圖元素，不受深度裁切 |
| 視野中心 | 舊行為：對齊房間長邊＝中心線上半個扇形半徑處；沿視線／室外＝眼睛、深度前後各 1.5 m |

手動拖過的剖面仍保留（同 10.5 既有判定）；「重設剖面位置」依目前的位置設定重算。

#### 10.5.3a 剖面轉 90°（2026-09-27，D-044，單一相機、持久化開關；`run_camera_keyplan` 的 `sectionRotate90` 可設定並持久化）

與 10.5.1／10.5.3 是「單一相機」層級的開關，不是面板全域設定（不影響其他相機、也不寫進 `ConfigManager` 設定檔）：記在**相機元素本身**的 ES（Schema `RevitMCP_CameraSectionOptions`，欄位 `SectionRotate90: bool`，預設 false），右鍵選單／④ ▾「剖面轉 90°（另一向）」可勾選切換（④ 下拉作用在目前選取的相機，沒有選取時停用）。

- **時機**：在 10.5.1（方向）與 10.5.2（範圍，含房間長軸模式把原點移到中心線那一步）都算完之後、10.5.3（位置＝相機位置）之前套用；10.5.3、`ClampSectionSpecToPlanCrop`（10.5.4 第七輪）之後照樣對「已轉向」的結果運作，不需要另外改。
- **規則**：新軸＝目前軸繞垂直軸轉 90°（`XYZ.BasisZ.CrossProduct(軸)`）；水平範圍沿新軸重新投影（相對目前原點）——室內＝眼睛所在房間頂點投影 ±0.6 m buffer（與 10.5.2 同一個 buffer）；室外／找不到房間＝等角 XY 範圍（房間外接框 +0.6 m，或找不到房間時的扇形外接框，見 §10.6）投影；水平深度重置，交回 10.5.2／10.5.3 的既有預設值決定（位置＝相機位置時會整個覆蓋，只影響「位置＝視野中心」＋「方向＝對齊房間長邊」這個組合，改用預設 ±1.5 m）。
- **深度（2026-09-27 D-045 修正，MCP 實測：轉 90° 後只看到近側牆／固定物、看不到房間內部）**：位置＝相機位置（10.5.3 預設）時，深度原本一律＝扇形半徑；轉 90° 且找得到眼睛所在房間 → 深度改＝`max(扇形半徑, 房間頂點沿「翻轉之後最終看向」投影範圍 + 0.6 m buffer)`，「翻轉之後最終看向」＝扇形在剖切面背後而軸反向（10.5.3 的 flip）之後、用最終軸重算的看向，不是翻轉前的方向；室外／找不到房間仍用扇形半徑。位置＝視野中心不受影響（仍是上面的預設 ±1.5 m）。剖面遠裁剪（新建與更新既有剖面兩條路徑）都是從同一個意圖框讀 Z 範圍，這裡改一處深度公式即對兩條路徑同時生效。
- **剖面標記垂直扇形抑制（2026-09-27 D-045 修正，MCP 實測：轉 90° 後側視扇形誤導）**：`DrawSectionMarker` 判斷剖面「看向」（−BasisZ）與相機水平視線的夾角，< 45° 時視為「順著視線看進剖面」（不是側視），只畫眼睛圓圈、不畫垂直視野扇形／楔形；純看幾何角度，不看是否轉 90°，所以任何其他方向組合恰好湊出這個夾角也會套用。平面視圖的扇形（DrawPlanMarker）不受影響。
- **自動跳過眼前的牆（2026-09-28 D-046，使用者決策，MCP 實測：相機站在門邊，沿視線看的剖面卡在牆上）**：與上一條共用同一個「沿視線看」幾何判斷（夾角 < 45°）才套用；側視剖面（預設模式）不受影響、剖切面仍通過眼睛。判斷式成立時，從眼睛沿剖面最終看向打水平射線（`ReferenceIntersector`，沿用站立面射線同一張 3D 視圖、含連結模型），目標類別牆／門／窗／帷幕嵌板；只認眼睛前方 1.5 m 內的最近命中（更遠的牆是場景一部分，不處理）。命中後，在同一條射線裡找「同一元素」（`Reference.ElementId`＋`LinkedElementId`）中最遠的命中當「元素背面」（合理厚度上限 1.5 m），剖切面挪到背面 +100 mm；遠裁剪維持原世界位置（深度＝原深度 − 位移量，下限 0.5 m）；剖面原點沿看向前移對應距離，水平範圍（沿軸向）與垂直範圍不受影響（看向與軸、與世界 Z 都垂直）。`ClampSectionSpecToPlanCrop`／`UpdateExistingSectionFrame` 本來就是對通用的 `spec.Origin`／意圖框運作，不假設原點＝眼睛，不需要另外改。前方 1.5 m 內沒有命中、找不到可用 3D 視圖、或射線例外 → 剖切面維持在相機位置，記 log 說明原因（不拋例外）。
- **剖面標記：眼睛落在裁剪框外（2026-09-28 D-046）**：跳過眼前牆之後，眼睛不再落在剖切面上（在裁剪框「相機背後」那一側）——`DrawSectionMarker` 把眼睛投影點換算到剖面裁剪框本機座標，落在 X/Y 範圍外就整個標記（圓圈＋扇形）略過，不拋例外。
- **切換行為**：切換時先寫 ES（單一小交易「剖面轉90°」），成功後照「重設剖面位置」流程（強制重建）立即生效，狀態列顯示中／還原中。
- **失敗退回**：基準軸與垂直軸重合、投影範圍算不出來、任何例外 → 記 log 並維持原方向，不拋例外、不影響其他規則。
- 詳見 [adr.zh-TW.md](adr.zh-TW.md) D-044、D-045、D-046。
- **MCP**：`run_camera_keyplan` 的 `sectionRotate90`（布林）有給值＝設定並持久化這台相機的開關＋強制這次重設剖面；`list_cameras` 於 `SectionOrientation.Rotate90` 回報目前設定；`Decisions.SectionRotate90` 回報本次實際套用值。

#### 10.5.3 保留手動調整

ES `RevitMCP_CameraSectionStateV2` 記下上次程式產生剖面時的原點與 X 軸方向——2026-09-25 第二輪起存的是「CropBox 套用＋Regenerate 之後讀回」的實際 `CropBox.Transform`（Revit 會正規化剖切面、深度外擴會平移原點；舊版存要求值 → 沒人動過也被判成手動移過）。重跑時若剖面目前的 `CropBox.Transform` 與記錄差 > 5 mm 或 > 0.5°（兩者都是模型座標讀回值），視為使用者手動調整過，log 記下兩組值與法向位移：這次只補畫標記，裁剪框完全不動，狀態列「剖面位置已保留（你曾手動調整）」。沒有記錄（舊版產生）視為沒動過。「重設剖面位置」無條件依目前方向設定重算覆蓋。

**更新既有剖面（2026-09-26 第三輪，D-037，修回歸）**：對既有 `ViewSection` 設 `CropBox` 時 Revit **不採用新 Transform**，只把 Min/Max 套在舊座標系（實測：log 的新原點 (-8880,7230,7370) mm，讀回仍是舊原點 (-6505.4,2222.6,4740.4)，區域範圍卻換成新值 → 世界 Z 2111–7523、畫面跑到鄰近樓梯核與下層）。改為：
1. 找剖面的 viewer（`GetDependentElements(OST_Viewers)` 中 `OwnerViewId` 不是剖面自己者；退回同名 viewer）；
2. 先繞「目前原點」的鉛直軸 `ElementTransformUtils.RotateElement` 到新方向 → Regenerate → 讀回 → `MoveElement` 水平位移到新原點 → Regenerate；
3. 讀回實際 `CropBox.Transform`，確認 BasisX／BasisY 與目標差 < 0.05°，在**實際座標系**內把意圖框的 8 個世界角點換成區域 Min/Max 再設 CropBox；
4. 後檢：Regenerate 後把意圖框與讀回框的角點都投到意圖座標系比對（沿剖面／高程／深度），最大偏差 > 5 mm 列警告；結果放進 `Decisions.SectionApplyLog`、`Decisions.SectionExtentDeviationMm`；
5. 任一步做不到（找不到 viewer、方向仍不符、偏差 > 5 mm）→ 剖面**不在任何圖紙上**才刪除重建（同名、重掛 ES 連結與狀態）；在圖紙上 → 範圍不動、列警告（請按「重設剖面位置」或手動調整）。
新建剖面也做同一個後檢。之後照舊 `SaveSectionStateActual` 存讀回狀態。

#### 10.5.4 表現與標記

見 §10.7、§10.8。剖面裡的其他剖面／立面／詳圖索引標記（例：穿過畫面、編號「-/---」的大剖面標頭）一律逐元素隱藏，立面／詳圖索引類別隱藏（2026-09-25 MCP 自測後；一鍵 4 視圖與「更新相機標記」都做）。視線、扇形邊線一律投影到剖面平面（斜交時自然縮短）；視線與剖面平面夾角 > 60° 記 log，接近 90° 只畫眼睛圓圈。

**2026-09-26 第七輪（Exterior Street View：室外剖面沿線超出平面裁剪框、近端被眼睛楔形蓋住，平面上看不到剖面線）**：剖面沿線範圍（10.5.2/10.5.3）套用後，兩端各截到 Key Plan 平面目前裁剪框內縮 1 m；平面上的 viewer 元素取代改為投影線色深灰 64、線寬 6、專案有 Dash 圖案就套虛線（halftone 仍固定 false，不跟淡化走）；平面上另畫的紅色視線中心線與剖面實際軸向（讀既有剖面 `CropBox.Transform.BasisX`）夾角 < 3° 時不重畫（由剖面線代表），扇形邊線／弧／楔形不受影響。

#### 10.5.5 樓層（淡化樣式下）

- 整類隱藏 `OST_Levels`（主體與連結模型的樓層基準都不顯示）。
- 改畫**簡化樓層記號**：保留樓層（2026-09-26 使用者規則：室內＝相機樓層的下一個建築樓層（高差 ≥ 1 m）＋相機樓層＋高差 ≥ 1 m 的上一個建築樓層；室外＝剖面裁剪框高度範圍內的所有建築樓層；落在裁剪框外的記號會被裁掉，範圍不因此外擴）各一條橫跨裁剪寬度的詳圖線（線型「相機Key Plan樓層線」灰 160、線寬 1、有虛線樣式就用虛線）＋左端 TextNote（類型「相機Key Plan樓層字」約 2 mm、背景透明、灰 128；內容「樓層名  +高程」依專案單位）。
- 每個記號以 ES（相機 UniqueId＋Role＝LevelMark＋Level UniqueId）連結真實 Level，重跑整批替換不重複；Level 改高程／改名時自動重畫（§9）。
- 因記號用 TextNote，剖面的文字類別不整類隱藏，改逐元素隱藏其他文字。
- 「原始」樣式：取消隱藏樓層類別、刪除這些記號、還原舊版留下的逐元素設定。

#### 10.5.6 連結模型

淡化樣式下，Key Plan 平面與剖面中每個 `RevitLinkInstance` 的顯示設定改為「依主體視圖」（`SetLinkOverrides(ByHostView)`；已是或無取代者不動；Revit 2024+ API，R22/R23 只記 log）。log 記每個連結前→後設定。

### 10.6 等角 `Key Plan-<相機>-等角`

- 方向＝**真正的標準等角**：在 ViewCube 上方四角（yaw 45°/135°/225°/315°）中挑水平方向最接近相機前方向者，forward＝(±1, ±1, −1)/√3（俯角 ≈ 35.264°，`CameraMath.SnapToIsoCorner`）；眼睛放在 Section Box 中心沿 −forward 退到框外（至少 5 m）；up 不滾動。
- 室內範圍：
  - XY＝眼睛所在房間 +0.6 m；無房間＝扇形框（夾 8 m）。
  - **近側（2026-09-26，D-038 → D-039 改預設）**：等角 forward＝(sx, sy, −1)/√3，觀看者在 −sx／−sy 側 → 近側兩面＝X：sx > 0 ? MinX : MaxX、Y：sy > 0 ? MinY : MaxY。設定 ④ ▾「等角近側：**相機位置（預設）**／牆內側／保留」（`CameraManagerSettings.IsoNearWalls`：Camera／Wall／Keep）：
    - 相機位置＝近側兩面剛好放在眼睛的 X／Y（不留後退距離）；夾限：與對面至少留 100 mm，眼睛在框外時該軸不動。
    - 牆內側＝D-038 規則：眼睛所在房間邊界線外接框（平面、軸向）往內 100 mm（擋住室內的兩道牆被切掉）；內縮後 min ≥ max → 不切。
    - 保留＝不切。
    遠側兩面維持房間 +0.6 m；Z 不變；室外相機不切。`run_camera_keyplan` 的 `isoNearWalls`（camera／wall／keep；舊值 cut＝wall）單次覆寫。切了哪兩面與數值寫 log 並回傳 `Decisions.IsoNearWalls`／`IsoNearWallsLog`。
  - Z 下限＝min(本層樓板底 −50 mm, 支撐樑底)；本層樓板＝XY 重疊、頂面在樓層高程 ±300 mm 內的樓板取最低；找不到＝樓層 −300 mm。支撐樑規則同 `set_3d_section_box` 的 `includeSupportingBeams`（共用 `SectionBoxLevelHelper`）。
  - Z 上限：上層樓板只認「底面或頂面 ≥ 房間頂 −0.1 m」的樓板／屋頂（房間內較低的夾層不算）；「整層（見上層樑）」＝min(房間頂 +0.3 m, 上層樓板底 −10 mm)；「只到當層（不含上層）」＝再與上層樑底 −10 mm 取小。無房間時：上層樓板底（找不到 → 上一樓層高程 − 該樓層樓板厚中位數（無樓板 300 mm）；無上一樓層 → 眼睛 +3 m）。上限 ≤ 下限時保底 +3 m。
- 室外範圍：XY＝非對稱扇形外接框 +2 m；Z＝[min(下方樓層, 最低樓層) −1 m, max(XY 範圍內模型最高點, 眼睛) +2 m]（`SectionBoxLevelHelper.FindMaxModelTopInXY`）；不做樓層切、不找上層樓板／支撐樑。
- 每次都確保剖面框外框看得到：拿掉視圖樣板、取消隱藏 `OST_SectionBox`、`AreAnnotationCategoriesHidden` 為 true 時改 false，再逐類別隱藏其他註解（只留剖面框）。DisplayStyle 不變。
- 等角不畫相機標記。

### 10.7 表現法（④ Key Plan 樣式）

只作用在本工具自建的 Key Plan 平面／剖面（等角見 §10.6），每次一鍵 4 視圖冪等重套，每步獨立 try/catch＋log。

**淡化背景（預設）**

1. 拿掉視圖樣板；Display Model＝一般（log 記 `AsValueString`）。
2. 類別取代：牆、柱、結構柱、樓板、屋頂、結構框架、帷幕嵌板／豎框／系統、樓梯——剖切線黑、剖切線寬（牆 5、柱 4、剖面中樓板／屋頂 5）、剖切圖案色與投影線中灰 128。其他 `CategoryType.Model` 且可取代的類別一律半色調；**不淡化** `OST_Lines`、`OST_DetailComponents`、`OST_Viewers`、`OST_SectionBox`、`OST_Cameras`。`OST_RvtLinks` 半色調。
3. 牆元素取代：帷幕牆或牆類型功能＝外牆 → 剖切線黑、線寬 7；其他牆 → 深灰 64、線寬 5（只看牆類型「功能」參數）。
4. 註解減量（`CanCategoryBeHidden` 才動）：隱藏軸線、樓層（平面）、尺寸、所有 `*Tags`（房間標籤保留但半色調）、文字（剖面改逐元素）、參考平面、範圍框、立面標記、詳圖索引、高程點／座標／坡度、接圖線、修訂雲形。不隱藏 Sections 類別。
5. 比例：平面取 1:50／1:100／1:200、剖面取 1:20／1:50／1:100／1:200 中最細且裁剪範圍放得進 A3 橫式約 400×270 mm 者；都放不下用 1:200。比例在畫標記前設定。

**原始**：略過上述淡化並還原（Display Model 一般、取消隱藏同一組類別、清掉類別與牆元素取代、取消隱藏樓層並刪除樓層記號）；比例不還原。

### 10.8 相機標記（B 型）

- 平面：眼睛圓圈（兩個 180° `Arc`，紙上半徑 1 mm × 比例）＋中央視線（到扇形半徑）＋水平扇形邊線與弧＋扇形填滿＋眼睛楔形。
- 剖面：同樣的圓圈＋投影後的視線＋垂直扇形（pitch+Down … pitch+Up）＋填滿＋楔形。
- 眼睛楔形：頂點在眼睛、沿同兩條邊界張開的實心小扇形，長度＝max(紙上 8 mm × 比例, 15% × 扇形半徑)，表面透明度 30%。
- 扇形填滿：表面透明度 90%（`MarkerFanTransparency`）。填滿用 `FilledRegion`＋類型「相機標記-實心」＋依元素取代（實心前景、標記色）；設定失敗退回半色調＋淡化色並記 log。
- 疊放順序：建立後以 `DetailElementOrderUtils` 把扇形送到該視圖詳圖最底層，楔形與標記線拉到最上。
- ⋯ 相機標記＝「A 模型」時，Key Plan 平面／剖面**不畫** B 型標記（舊的照刪）；「B」「A+B」照畫。樓層總覽一律畫 B（§10.10）。
- 所有標記線套線型「相機標記線」（`OST_Lines` 子類別，不存在時建立一次，線寬 5、實線；已存在只更新線寬）。
- 每個標記元素貼 ES（Role＝`MarkerPlan`／`MarkerSection`），每次重畫先刪同相機同 Role 的舊標記，不累積。

### 10.9 Extensible Storage

| Schema | GUID | 欄位 | 貼在 |
|---|---|---|---|
| `RevitMCP_CameraKeyPlanLink` | `6C9E2F0A-6E2C-4E7C-9C8B-2F6E9B0F1A2D` | `CameraUniqueId`、`Role`（Plan／Section／Iso／MarkerPlan／MarkerSection／MarkerName／MarkerA／MarkerAFan／Overview／Sheet） | Key Plan 視圖、標記元素、A 型族群實例與扇形、樓層總覽（Role＝Overview 時 `CameraUniqueId` 欄位存的是**樓層** UniqueId）、相機圖紙（Role＝Sheet） |
| `RevitMCP_CameraSectionStateV2` | `5B2D8E61-3C4A-4F7B-A1D9-7E0C6B3F2A58` | `OriginX/Y/Z`（Length/Feet）、`ForwardX/Y`（Number/General） | Key Plan 剖面 |
| `RevitMCP_CameraSourcePlan` | 見 `CameraSourcePlanStore.cs` | 單一 string（放置平面） | 相機 View3D |
| `RevitMCP_CameraViewVersions` | `A8D12364-5CAE-46E8-9548-41728EC43746` | 單一 string `VersionsJson`（JSON 陣列，§11.5） | 相機 View3D |

Schema 在交易外先建；剖面狀態讀寫失敗只記 log，不中斷建立視圖。已發佈的 GUID 與欄位定義不可再改。

### 10.10 樓層總覽 `Key Plan-總覽-<樓層名>`（2026-09-25 Wave 2）

實作：`CameraKeyPlanService.CreateOrUpdateOverviews`（`MCP/Core/CameraKeyPlanService.Overview.cs`）。入口：④ ▾「更新樓層總覽」、MCP `run_camera_overview`、一鍵 4 視圖成功後自動更新該相機樓層（獨立交易，失敗只記 log，不影響 4 視圖）。

- 一個樓層一張，只為「有相機」（相機的下方樓層＝該層；室內相機＝房間的 Level，可能不是建築樓層，`all=true` 也會列入）的樓層建立。ES：Role＝`Overview`、`CameraUniqueId` 欄位存 Level UniqueId；重跑更新同一張，找不到 ES 才退回精確名稱比對。
- 來源平面＝`BaseMapPlanChooser.Choose`（與 ③ 位置圖同一個挑法：該層相機的眼睛座標＋最多台共用的放置平面），候選排除本工具自建（有 ES 連結）的 Key Plan 視圖；`Duplicate(ViewDuplicateOption.Duplicate)`（不含詳圖）。挑不到 → 該層既有平面（同 §10.4）→ 新建樓層平面。拿掉視圖樣板、移除非矩形裁剪形狀；同 §10.4 做視圖整理（解除 Scope Box、專案北、圖紙上的標題＝「樓層總覽 <樓層名>」），之後才設裁剪框。
- 回報的範圍（`ExtentsMm`）一律是**讀回視圖裁剪框**的實際值；程式計算的預期值另列 `IntendedMm`；兩者差 > 1 % 放進 `Warnings`（附可能原因）。
- 其他剖面／立面／詳圖索引標記逐元素隱藏（同 §10.4，總覽沒有要保留的 viewer）。
- 裁剪（只改範圍、不旋轉）：
  - 該層全部室內：每台相機扇形（室內扇形半徑、非對稱左右緣、弧上 16 點）外接框 **+3 m** 的聯集，另含每個眼睛 +3 m。
  - 有室外相機：再聯集底圖的裁剪框（＝建築範圍）；室外扇形（可達 60 m）不撐大總覽。
  - 2026-09-26 第三輪（D-036）：裁剪與畫扇形共用同一個半徑函式 `OverviewFanRadiusFeet`；每台相機用的半徑寫進 `ExtentsMode`。畫完標記後再讀回**室內相機**的 B 標記元素（`OwnedByView`，Role＝MarkerPlan／MarkerName）在本視圖的外接框，超出裁剪（含 3 m 餘裕）就外擴並列警告——裁剪一定包得住實際畫出的扇形。
- 表現法同 Key Plan 平面（§10.7）；但文字類別不整類隱藏，改逐元素隱藏「不是我們的」文字，保留相機名稱。
- 每次更新先刪除這張視圖內**所有**相機標記（Role＝MarkerPlan／MarkerName），再逐台重畫：B 型平面標記（§10.8，不論 ⋯ 相機標記設定）＋名稱 `TextNote`（文字類型「相機標記名稱字」2.5 mm、背景透明、黑；放在眼睛後方紙上 3 mm）。已刪除或換到別層的相機因此不殘留。
- 單一交易「更新樓層總覽」，每層一個 `SubTransaction`：某層失敗只回滾那一層。

### 10.11 A 型相機標記（模型族群，2026-09-25 Wave 2）

實作：`MCP/Core/CameraKeyPlanService.MarkerA.cs`。開關：⋯ 更多 → 相機標記（`CameraManagerSettings.MarkerMode`：B／A／AB，本機設定，預設 B）。切換時立即排入：含 A → 確保族群並同步全部相機；不含 A → 刪除全部 A 實例與扇形（族群留在專案）；接著對全部相機跑一次「更新相機標記」，讓 B 標記依新設定重畫或刪除。

- **族群「RevitMCP 相機標記」**：第一次需要時以程式建立——樣板路徑由執行中 Revit 版本組成：`%ProgramData%\Autodesk\RVT <版本>\Family Templates\English\Generic Model.rft`，找不到依序試 `English\Metric Generic Model.rft`、`English-Imperial\Generic Model.rft`、`English_I\Generic Model.rft`、`Application.FamilyTemplatePath` 下同名檔（本機 2024 只有 `English\Metric Generic Model.rft`）。都找不到 → 回報原因、A 略過、B 不受影響。族群內容：子類別「相機標記」（一般模型）、相機體擠出 200×120×120 mm（眼睛在前緣中心＝原點）、扁平視線箭頭擠出（長 350 mm）；實例參數 視野角左、視野角右、遠裁剪、俯仰角、相機名稱。存成 `%TEMP%\RevitMCP\RevitMCP 相機標記.rfa`，在交易「載入相機標記族群」內 `LoadFamily`。
- **每台相機一個實例**：放在眼睛（樓層＝相機的下方樓層，室內＝房間的 Level），繞 Z 旋轉使族群 +Y＝相機水平前方向；非工作平面族群不能傾斜，一律水平，俯仰角只寫進參數。參數隨相機更新。
- **視野扇形**：參數驅動的族群扇形未實作（見 D-026）；改為每台相機一個 `DirectShape`（一般模型，幾何以 `SolidOptions(材料, GraphicsStyleId)` 指定子類別「相機標記」的投影 GraphicsStyle、材料「RevitMCP 相機標記」70% 透明），水平扇形 10 mm 厚、左右緣＝非對稱邊界角、半徑＝與 Key Plan 相同的扇形半徑；釘選避免誤拖。2026-09-25 第二輪：GraphicsStyle 取不到時先 Regenerate 再取；`SetShape` 後讀回幾何確認每個實體的 `GraphicsStyleId`＝子類別樣式，不符記在 `AMarkerLog`（「子類別樣式 gs=…，不符 N」）。2026-09-26 第三輪：讀回前先 Regenerate（`SetShape` 後不 Regenerate 讀不到幾何，舊版因此每台都報「讀不到扇形幾何」），Options＝Fine＋IncludeNonVisibleObjects＋ComputeReferences（再退一組 Fine），逐層展開 GeometryInstance，實體（含各面）與網格的 GraphicsStyleId 都比對；扇形本身不改。
- **透視圖不看到標記**：每次同步對所有透視圖（含出圖用透視複本）`SetCategoryHidden(子類別「相機標記」)`（已隱藏者不動）。**視圖本身不能隱藏且套了視圖樣板（樣板控制 V/G）→ 改在該樣板上隱藏這個子類別**（2026-09-25 第二輪，D-033；只動本工具的子類別，套同一樣板的其他 3D 視圖也一併隱藏），樣板名稱記進 `AMarkerLog` 與 log；連樣板都不能隱藏才列為失敗。一鍵出圖紙建立／同步透視複本後也跑一次。Enscape 是否也排除＝使用者實測（roadmap）。
- **標記型式不含 A 時**（面板設定或 MCP `markerMode=B`）：一鍵 4 視圖成功後刪除「這台相機」的 A 實例＋扇形（交易「更新相機 A 標記」，`AMarkerLog`＝「標記型式＝B：已刪除本相機 A 實例／扇形 N 個」）；不含 B 時 Key Plan 平面／剖面的 B 標記照舊刪除。
- **淡化樣式**：相機標記含 A 時，Key Plan 淡化表現法不把「一般模型」類別半色調（否則 A 標記被淡化）。
- **同步時機**：一鍵 4 視圖成功後（交易「更新相機 A 標記」）、「更新相機標記」去抖交易（相機被修改）、切換相機標記型式、MCP 無直接入口（`run_camera_keyplan` 會觸發一鍵 4 視圖的同步）。
- ES：實例 Role＝`MarkerA`、扇形 Role＝`MarkerAFan`（`CameraUniqueId`＝相機）。同步時同一台相機只留一個實例與一個扇形（複製出來的多餘者刪除）；相機被刪除後，下一次去抖清掉孤兒標記（A 實例、A 扇形、總覽／Key Plan 的 B 標記與名稱）。

### 10.12 拖標記 → 相機（反向同步，2026-09-25 Wave 2）

- 只支援 **A 型族群實例**。B 型標記是一個個獨立的詳圖線／填滿區域，沒有群組、不能當一個單位拖曳，所以不做（D-027）。
- 使用者移動／旋轉 A 實例 → `DocumentChanged`（live sync 已啟用時）收集被修改的 A 實例 → 500ms 去抖 → 交易「由標記更新相機」：眼睛 XY＝實例位置、眼睛 Z 不變（維持離樓板高度）、水平角＝實例旋轉、俯仰角不變、上方向不滾動。與相機差 < 1 mm 且 < 0.1° 的略過。
- 迴圈防護：相機改了會照常觸發 B 標記／A 標記重畫（「更新相機標記」交易），那次 A 實例已在正確位置，不會再動；即使動了也是自己的交易名稱，`DocumentChanged` 會忽略。
- 面板沒按過「重新整理」（live sync 未啟用）時不做反向同步。

### 10.13 一鍵出圖紙 `Key Plan-<相機名>`（2026-09-25 Wave 3）

實作：`CameraKeyPlanService.CreateOrUpdateCameraSheet`（`MCP/Core/CameraKeyPlanService.Sheet.cs`）。入口：④ ▾「一鍵出圖紙」、清單列／地圖標記右鍵「一鍵出圖紙」、MCP `create_camera_sheet`。完成後面板把該圖紙設為作用中視圖並**關閉其他所有視圖**（含 Key Plan 4 張，2026-09-26）；MCP 工具不切換視圖。

1. **先確保有 Key Plan**：相機缺平面／剖面／等角任一張 → 先跑一鍵 4 視圖（不並排，獨立交易「建立相機 Key Plan」）。
2. **圖框類型**：MCP 參數 `titleBlockTypeId` → 面板設定「圖紙圖框」→ 專案內**已放置次數最多**的圖框類型 → 第一個圖框類型（名稱排序）→ 專案沒有圖框＝不放圖框。既有相機圖紙的圖框類型與選擇不同時換類型（不動位置）。
3. **圖紙**：以 ES（`CameraKeyPlanLink`，`CameraUniqueId`＝相機、Role＝`Sheet`）找本相機的圖紙；沒有就 `ViewSheet.Create`（圖紙編號由 Revit 自動給），名稱 `Key Plan-<相機名>`（相機改名後重跑會跟著改）。不以名稱比對找既有圖紙（避免接管使用者自己的同名圖紙）。
4. **可繪區**：圖框實例在圖紙上的外接框（`get_BoundingBox(sheet)`）四邊各內縮 `SheetMarginMm`（本機設定，預設 15 mm）；沒有圖框或讀不到＝A3 橫式 420×297 mm 內縮同樣邊界。**扣除圖框標題欄（2026-09-25 第二輪，D-034）**：讀圖框實例在圖紙視圖中的幾何（`get_Geometry(Options{View=sheet})`，展開 GeometryInstance 取 Line／PolyLine）——右側標題欄＝長度 ≥ 60 % 圖框高的垂直線、位在圖框右側 40 % 內、離最右長直線 > 25 mm（排除外框／內框）者取最左；下方標題欄＝長度 ≥ 60 % 圖框寬的水平線、位在下方 35 % 內、離最下長橫線 > 25 mm 者取最高。可繪區右緣／下緣退到標題欄線再內縮邊界。都偵測不到且圖紙橫式 → 保留右側 `SheetReservedStripFraction`（本機設定，預設 0.20）。判定結果寫進 `DrawAreaSource`。2026-09-26 第三輪（MCP 自測：E1 30 x 42 讀到 0 條線）：依序試 `Options{View=sheet}`、`{View=sheet, IncludeNonVisibleObjects}`、`{無 View, Fine}` 取線最多的一組；GeometryInstance 的 `GetInstanceGeometry()` 為空時改 `GetSymbolGeometry(gi.Transform)`；另收實體的直線邊；各類幾何物件數寫進 `DrawAreaSource`（讀不到時看得出原因）。仍保留 20 % 退路。
5. **版面（2026-09-26 第三輪，D-035，取代「透視 55 %」）**：透視圖**無法**以放大裁剪框放大（實測 FOV 64°→101°，已由 FOV 讀回還原），所以：
   - 先處理透視圖（出圖複本仍試放大，失敗即還原），Regenerate 後讀回**實際視埠寬**，透視格寬＝實際寬＋左右 6 mm，夾在可繪寬 20–70 %；其餘寬度全給右側。
   - 右側試幾種版面：單欄（由上而下平面、剖面、等角，列高依內容長寬比分配，夾 0.25–4）、上二下一（最扁的視圖放下列橫跨全寬，上下列高 40／50／60 %）、2×2（左上／右上／左下）；各自挑放得進的最細比例，取「最粗比例最小、再比例和最小」者。版面判定寫進 `DrawAreaSource`。
   - **圖紙上透視圖的大小只能在 Revit 內手動調整**：選視埠 → 裁剪區域大小（Size Crop）→「縮放（鎖定比例）」。面板完成訊息與透視視埠的 `Note` 都會提示。
   - 格子之間留 6 mm（給視埠標題）。先確保四個視埠都在圖紙上（新的暫放中心），Regenerate 後量尺寸、調整，最後 `SetBoxCenter` 置中於格子。
6. **自動比例與大小（2026-09-25 第二輪，D-034，取代舊的「不改比例」）**：
   - Key Plan 平面／剖面／等角（本工具的視圖）：挑「內容 ÷ 比例 ≤ 格子 95 %」的最細標準比例（1:20、25、50、75、100、150、200）；都放不進再試 1:250、300、400、500、750、1000 並列警告。改比例失敗（例：樣板控制比例）列警告。
   - 出圖用透視複本：裁剪框等比放大 k＝0.9 × min(格寬／視埠寬, 格高／視埠高)，讓視埠填滿格子約 90 %。依序試「X/Y/Z 一起乘 k」「只乘 X/Y」，每次 Regenerate 後以 `ComputePerspectiveFov` 讀回比對水平／垂直 FOV 與四個邊界角（容許 0.2°），都不符就還原並列警告。複本之後被去抖「更新相機標記」同步裁剪框時，會等比縮放回同步前的圖紙外框大小（FOV 不變）。圖紙上放的是原相機時不動它的裁剪框。
   - 仍放不進格子才回報 `Overflow`。
7. **視埠類型**：沿用專案既有、「顯示標題」不是「否」的視埠類型中最常用者（`VIEWPORT_ATTR_SHOW_LABEL` 先讀顯示字串，取不到才以整數 0＝否判斷；整數對應未查證）。候選＝Category＝Viewports、FamilyName＝Viewport／視埠／視口、既有視埠用到的類型的聯集（2026-09-25：舊版只看 Category，部分版本視埠類型 Category 為 null → 候選清單是空的，log 卻說「沒有顯示標題的類型」）；事前挑不到時，以第一個新視埠的 `GetValidTypes()` 再挑一次；都沒有就沿用 Revit 預設類型、不新建。`ViewportTypeLog`＝候選與挑選紀錄＋「實際使用：<類型名>」（讀回每個視埠的類型）。
8. **一張視圖只能在一張圖紙上**：視圖已在本相機圖紙上＝重新排版（狀態「重新排版」；2026-09-25 第二輪起不再保留原位）；在其他圖紙上＝略過並回報「已在其他圖紙「編號 - 名稱」上」；`Viewport.CanAddViewToSheet` 為 false 也略過並回報。
   **透視圖例外（2026-09-25 MCP 自測後）**：實案常把相機放在自己的 3D 圖紙上。原相機已在「別張」圖紙上時，改放出圖用透視複本 `<相機名>-Key Plan 圖紙`（`View3D.Duplicate(ViewDuplicateOption.Duplicate)`，ES Role＝`PerspectiveForSheet`，已有就沿用）。選擇順序：原相機已在本相機圖紙上 → 原相機；已有複本 → 複本；原相機在別張圖紙上 → 建立複本；否則原相機。複本在每次 `create_camera_sheet` 與去抖「更新相機標記」時同步原相機的方向、裁剪框（含開關）、遠裁剪（值＋開關）。複本不是相機：清單、樓層總覽、A 標記、MCP 標記計數、DocumentChanged 新相機偵測都排除它。回傳 `Perspective.Placed`（原相機／透視複本（新建）／透視複本（沿用並同步））與說明。
9. 單一交易「建立相機圖紙」（`DocumentChanged` 忽略這個交易名稱）；整體失敗回滾整筆並回報原因。

## 11. 其他功能

### 11.1 複製相機到其他樓層

- 同平面 XY、同離樓板高度、同方向、同視野與遠裁剪；新名稱 `<原名> - <樓層名>`，重名加「 (2)」等序號；一個交易「複製相機到其他樓層」。
- 入口：④ 更多／右鍵「複製到其他樓層…」（勾選多個樓層的 Popup）、② 樓層欄選另一個樓層（單一目標，完成後選取新相機，原列樓層格還原顯示；狀態列「已複製「X」到 L5 → 「X - L5」（原相機不變）」）。
- 原相機與其 Key Plan 視圖不受影響；新相機沒有 Key Plan。

### 11.2 匯出 Excel

ClosedXML；欄位：名稱、樓層、離樓板高度、X/Y、水平角、俯仰角、視野角、遠裁剪、Eye Elevation 原始值；預設檔名 `相機清單_<yyyyMMdd>.xlsx`。

### 11.3 預覽視窗

獨立無模式視窗（`MCP/UI/CameraPreviewWindow.cs`），兩個 `PreviewControl`（透視圖＋所在樓層平面）；關閉時 `Dispose()`。文件關閉與 Revit 關閉時集中強制關閉所有預覽視窗。

### 11.5 視角版本（2026-09-25 Wave 3）

實作：`MCP/Core/CameraKeyPlanService.Versions.cs`。入口：清單列／地圖標記右鍵「視角版本 ▸」、MCP `camera_version`。

- **內容**：名稱、儲存時間、眼睛 XYZ、前方向、上方向、裁剪框啟用、裁剪框 Min/Max＋Transform（原點與三軸）、遠裁剪距離與「遠裁剪啟用」參數值。長度一律 feet（Revit 內部單位）。
- **儲存位置**：相機 View3D 上的 ES `RevitMCP_CameraViewVersions`（單一 string 欄位 `VersionsJson`，JSON 陣列，依儲存順序）。存在 .rvt，同事開檔也能切換（D-030）。
- **右鍵選單**：`視角版本 ▸` → 「儲存目前視角…」（主題化名稱 Popup，預設＝下一個空字母「B案」「C案」…；1–60 字；同名＝覆蓋）／每個版本名稱一項（**新到舊**；目前視角符合的版本標「✓ 目前」、粗體；點一下＝還原）／「刪除版本 ▸」逐一列出。選單每次開啟時依該列的版本名稱重建（版本名稱與「目前符合」在清單快照時讀取）。
- **自動版本（2026-09-26，D-043）**：相機第一次被任何路徑修改前（清單編輯、`set_camera_pose`、地圖拖曳、拖 A 標記、人視高套用、導覽監看）若沒有任何版本 → 自動把原始視角存成「A案」（與該次修改同一個交易；導覽則在更新時另開交易存「移動前」姿態）。還原前，目前視角若與每個已存版本都不同（眼睛 1 mm、方向 0.05°、裁剪框 1 mm）→ 先存「自動備份 HH:mm」（同名加秒）。儲存時間含秒。
- **儲存**：交易「儲存相機視角版本」，只寫 ES。**刪除**：交易「刪除相機視角版本」，只移除 ES 裡那一筆，不動任何模型元素。兩者都在 `DocumentChanged` 忽略名單內，完成後面板自己輕量刷新清單。
- **還原**：單一交易「還原相機視角：<名稱>」——`SetOrientation`（眼睛／上方向／前方向）→ 裁剪框啟用與 `CropBox`（Transform＋Min/Max）→ 遠裁剪啟用與距離。裁剪框或遠裁剪還原失敗只列為警告（眼睛與方向照常套用）；相機視圖被鎖定時拒絕並提示。相機被修改後照常經 `DocumentChanged` → 500 ms 去抖 → 「更新相機標記」（B／A 型標記、樓層總覽標記；§9）。Key Plan 的裁剪框／剖面範圍**不**自動重算（同 §11.4），要重算再按一鍵 4 視圖。

### 11.4 不做的事

- Project Browser「顯示相機」在平面上的對應功能：查無公開 API／Postable Command，不以暫時元素假裝。
- （2026-09-26 起）Key Plan 只在相機相對上次 Key Plan 移動 > 300 mm 或轉 > 5° 時自動重算範圍（§9）；小幅變動只重畫標記。樓層總覽的裁剪只在「更新樓層總覽」／一鍵 4 視圖時重算。
- 拖 B 型詳圖標記不會移動相機（§10.12）。

## 12. MCP 測試工具（Wave 1–3）

C#：`MCP/Core/Commands/CommandExecutor.CameraManager.cs`；TS：`MCP-Server/src/tools/camera-manager-tools.ts`（`full`、`architect` profile）。走既有 MCP ExternalEvent 路徑，不經面板佇列；呼叫的服務方法不含 WPF 型別。長度輸出為內部原點座標 mm。

| 工具 | 讀／寫 | 參數 | 呼叫的共用程式碼 |
|---|---|---|---|
| `list_cameras` | 唯讀 | `nameContains?` | `ReadSnapshot`／`BuildCameraRow`、`ComputePerspectiveFov`、`FindEyeRoom`、`BaseMapPlanChooser.Choose`、ES 連結查詢 |
| `run_camera_keyplan` | 寫入 | `cameraId?`、`tile`（預設 false）、`sectionMode?`（room／sight）、`isoMode?`（full／current）、`resetSection`（預設 false）、`markerMode?`（B／A／AB，2026-09-25）、`isoNearWalls?`（camera／wall／keep，舊值 cut＝wall；2026-09-26）、`sectionRotate90?`（布林，2026-09-27；有給值＝設定並持久化這台相機的「剖面轉 90°」開關＋強制 `resetSection=true`，不是單次覆寫，§10.5.3a） | `CreateOrUpdateFourViews`（單次覆寫設定，不寫回設定檔）、`tile=true` 時 `OpenAndTileFourViews`；另回傳 `MarkerModeUsed`、`Warnings`（裁剪框讀回不符）、`Plan.Extents.ScopeBox`、`Decisions.SectionRotate90` |
| `set_camera_pose` | 寫入 | `cameraId?`＋任一：`heightAboveLevelCm`、`yawDeg`、`pitchDeg`、`planXcm`、`planYcm`、`farClipCm` | `BuildEditRequestFromRow`＋`ApplyDetailEdit`（第一次修改前自動存「A案」）→ `RefreshCameraViewIfOpen`（回傳 `ViewRefresh`）→ `UpdateCamerasAfterChange`（回傳 `CameraUpdate`：標記更新／略過數、是否重算 Key Plan；2026-09-26） |
| `copy_camera_to_level` | 寫入 | `cameraId?`、`levelId` 或 `levelName` | `CopyCameraToLevels` |
| `run_camera_overview` | 寫入（Wave 2） | `levelId` 或 `levelName`，或 `all=true`（所有有相機的樓層） | `CreateOrUpdateOverviews`（§10.10）；回傳每層視圖 id、名稱、是否新建、相機數與名稱、底圖來源與理由、範圍模式、裁剪外接框 mm、比例、依角色的標記元素數 |
| `create_camera_sheet` | 寫入（Wave 3） | `cameraId?`、`titleBlockTypeId?`（省略＝面板設定→最常用） | `CreateOrUpdateCameraSheet`（§10.13）；回傳圖紙 id／編號／名稱、是否新建、是否先跑了一鍵 4 視圖、圖框與來源、可繪區、視埠類型挑選紀錄、每個視埠（角色、視圖、視埠 id、新放置／重新排版／略過、比例或裁剪縮放說明、中心與尺寸 mm、格子、是否超出）、警告 |
| `camera_version` | list 唯讀；save／restore／delete 寫入（Wave 3） | `cameraId?`、`action`（list 預設／save／restore／delete）、`name`（save／restore／delete 必填） | `SaveVersion`／`RestoreVersion`／`DeleteVersion`／`ReadVersionsNewestFirst`（§11.5）；回傳目前姿態、`CurrentMatchesVersion`（目前視角符合的版本名稱或 null）、`NextSuggestedName`，全部版本新到舊（眼睛 mm、水平角、俯仰角、裁剪框尺寸、遠裁剪、`MatchesCurrent`）；restore 的 `Warnings` 會寫出自動備份名稱 |
| `get_view_extents` | 唯讀（通用） | `viewId?` | 直接讀視圖：類型、比例、裁剪框、註解裁剪、Section Box、遠裁剪、Display Model、詳細等級、樣板、隱藏類別、連結顯示設定、視圖專屬元素計數；2026-09-25 加 `ScopeBox`（id／名稱）、`Orientation`、`TitleOnSheet`、`VisibleViewers`（目前可見的剖面／立面／詳圖索引 viewer 與立面標記數） |

`cameraId` 省略＝作用中透視圖；可給相機 View3D 或平面上的相機元件。沒有任何工具會刪除使用者元素（一鍵 4 視圖／樓層總覽只刪除自己先前畫的標記；`camera_version` delete 只移除 ES 裡的一筆版本資料）。MCP 標註：`create_camera_sheet`、`camera_version` 依 `annotations.ts` 預設規則為「寫入、非破壞性」（`camera_` 不是唯讀前綴；不在破壞性白名單）。A 型標記沒有獨立的 MCP 工具：相機標記設定含 A 時，`run_camera_keyplan` 會一併同步，回傳 `AMarker`（同步摘要，未啟用 A＝null）；`OverviewViewId`＝自動更新的樓層總覽 id。

## 13. 儲存位置

| 資料 | 存在哪 | 誰看得到 |
|---|---|---|
| 相機、Key Plan 視圖、樓層總覽、相機圖紙、標記、樓層記號、A 型族群與實例、視角版本、ES 連結 | .rvt | 所有開這個檔的人 |
| 面板設定（人視高、欄位顯示、分割比例、等角範圍、等角近側牆、剖面方向、室外半徑、標記顏色、Key Plan 樣式、相機標記型式、底圖清單模式、圖紙圖框、圖紙邊界…） | `ServiceSettings.CameraManager`（本機 JSON） | 只有本機 |

## 14. 技術約束

- 模型修改一律在 Transaction 內、每次操作一個中文復原名稱。
- ElementId 用 `RevitCompatibility` 輔助（R24 Int32、R25+ Int64）。
- `get_camera_info` 的輸出欄位不可改變（與面板共用 `MCP/Core/CameraMath.cs`）。2026-09-25 起其 `LevelBelow`／`AboveLevelBelowMm` 的**值**對室內相機改為房間的 Level（欄位不變）。
- 部署到 Revit 2024 前先關閉 Revit；部署後比對 DLL SHA256。
- 不用 PowerShell `Get-Content`／`Set-Content` 改寫原始碼（`domain/lessons.md` L-032）。

## 15. 程式碼地圖

| 檔案 | 職責 |
|---|---|
| `MCP/UI/CameraManagerPaneProvider.cs`、`CameraManagerPanelControl.cs` | 停靠面板註冊與組裝、事件接線 |
| `MCP/UI/CameraListSection.cs`、`KeyPlanSection.cs`、`PresetsSection.cs`、`MultiViewSection.cs`、`CameraPreviewWindow.cs`、`CameraManagerTheme.cs` | ②／③／④⑤／並排／預覽／主題 |
| `MCP/Core/CameraManagerService.cs` | 讀清單、底圖匯出、編輯、複製、匯出 Excel、開啟並排（不含 WPF） |
| `MCP/Core/CameraKeyPlanService.cs` | 一鍵 4 視圖、標記、表現法、ES |
| `MCP/Core/CameraKeyPlanService.Overview.cs` | 樓層總覽（§10.10） |
| `MCP/Core/CameraKeyPlanService.MarkerA.cs` | A 型族群建立／載入、A 標記同步、孤兒清理、拖標記 → 相機（§10.11–10.12） |
| `MCP/Core/CameraKeyPlanService.Sheet.cs` | 一鍵出圖紙、圖框／視埠類型挑選（§10.13） |
| `MCP/Core/CameraKeyPlanService.Versions.cs` | 視角版本 ES 讀寫、還原（§11.5） |
| `MCP/Core/CameraKeyPlanService.EyeRoom.cs` | 眼睛所在房間搜尋、室內相機樓層＝房間 Level（§10.2） |
| `MCP/Core/CameraKeyPlanService.ViewHygiene.cs` | 複製平面整理（Scope Box／專案北／圖紙標題）、裁剪讀回比對、其他視圖標記隱藏、出圖用透視複本、markerMode 單次覆寫 |
| `MCP/Core/CameraMath.cs` | 樓層、角度、FOV、方向數學 |
| `MCP/Core/CameraManagerModels.cs` | 資料模型、`BaseMapPlanChooser`、設定 |
| `MCP/Core/CameraManagerEventQueue.cs`、`CameraManagerDiagnostics.cs`、`CameraViewZoomService.cs`、`CameraSourcePlanStore.cs`、`SectionBoxLevelHelper.cs` | 佇列、診斷、並排後縮放、放置平面、Section Box 共用 |
| `MCP/Core/CameraKeyPlanService.LiveUpdate.cs` | 相機變動後的增量更新、姿態雜湊、Key Plan 重算判斷（§9，2026-09-26） |
| `MCP/Core/CameraPerf.cs` | `⏱` 步驟計時 log（2026-09-26） |
| `MCP/Core/Commands/CommandExecutor.CameraManager.cs` | MCP 測試工具 |

## 16. 驗收（每個交付）

1. R24／R25／R26 建置 0 錯誤；R22／R23 不新增錯誤。
2. 部署到 Revit 2024，DLL SHA256 一致。
3. `scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy` 無新增 FAIL。
4. 先以 MCP 工具自測（roadmap「MCP 自測」），再由使用者實機測試並截圖確認。
