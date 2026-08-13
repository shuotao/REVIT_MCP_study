---
name: mep-mechanical-settings
description: "Manage → MEP Settings（Mechanical Settings / Pipe Settings）的盤點與增減 SOP：目錄盤點 → 模型用量盤點 → 法規對帳 → 增減。核心規則是「增無限、減只能刪模型沒用到的」，刪除採 列表 → 執行 → QC → 誤刪復原 四步。含 Revit API 單位陷阱與對話框會量化內徑的警告。適用 duct rectangular/oval/round 尺寸表、pipe segment 尺寸目錄、fitting 角度、坡度、流體。"
metadata:
  version: "1.1"
  updated: "2026-08-06"
  references:
    - "Revit API: Autodesk.Revit.DB.Segment / MEPSize / DuctSizeSettings / DuctSettings / PipeSettings"
    - "建築技術規則建築設備編 §106 第四款（排煙管風速下限 450 m/min）law.moj.gov.tw pcode=D0070117"
    - "建築技術規則 §102（通風量單位，見 set-project-units skill）"
    - "Autodesk Revit Help — About Duct Sizing Methods / Use Duct Sizing（Equal Friction 預設 0.10 in-wg/100 ft）"
    - "ASHRAE Handbook Fundamentals Ch.21 Duct Design（friction rate / velocity 表，未讀原書）"
    - "SMACNA HVAC Duct Design §5.9.2（未讀原書）"
    - "SHASE-S 206 給排水衛生設備規準（給水流速，未讀原書）"
    - "CNS / JIS 呼稱對照與 CNS PVC 尺寸系列（仍缺，見 §6.2）"
  related:
    - mep-csa-clash-detection.md
    - mep-extension-guide.md
    - mep-opening-candidate-scan.md
  referenced_by:
    - "mep-settings-curation"
  tags: [mep, mechanical-settings, duct, pipe, segment, size, 管徑, 風管尺寸, 角度, 坡度, curate, CNS]
---

# MEP Settings 盤點與增減 SOP

## Purpose

Revit 的 `Manage → MEP Settings` 把**風管/管路的尺寸目錄與系統參數**藏在對話框裡。這些資料 **Schedule 撈不到、System Browser 也看不到** —— 它們是設定定義，不是模型元件，唯一路徑是 Revit API。

本 domain 定義兩件事：

1. **盤點方法** —— 目錄裡有什麼、模型實際用到什麼，兩者是不同的問題。
2. **增減協定** —— 什麼可以刪、刪之前要確認什麼、刪錯了怎麼救。

## 1. 最重要的前提：目錄 ≠ 用量

這是本 domain 存在的理由。兩個層次必須分開盤點：

| 層次 | 問題 | 資料來源 | 工具 |
|---|---|---|---|
| **目錄** | Mechanical Settings 裡「列了」哪些尺寸 | `Segment.GetSizes()`、`DuctSizeSettings[shape]` | `get_mep_segments_and_sizes` |
| **用量** | 模型裡「真的有元件在用」哪些尺寸 | Duct / Pipe / Fitting / Accessory 元件本身 | `get_mep_size_usage` |

**只看目錄就刪除 = 盲刪。** 目錄裡的尺寸有沒有被用，目錄自己不知道。

`Used in Size Lists` / `Used in Sizing` 兩個勾選欄**也不等於用量** —— 它們只表示「這個尺寸要不要出現在下拉選單／要不要參與自動定管徑」，跟「模型裡有沒有元件是這個尺寸」完全無關。一個沒勾選的尺寸仍然可能有既有元件在用（勾選是後來才取消的）。

## 2. 用量盤點方法

### 2.1 風管（Duct）

| 形狀 | 元件參數 | 對應的目錄 |
|---|---|---|
| Rectangular | `RBS_CURVE_WIDTH_PARAM`、`RBS_CURVE_HEIGHT_PARAM` | Duct Settings → Rectangular |
| Oval | 同上（寬高） | Duct Settings → Oval |
| Round | `RBS_CURVE_DIAMETER_PARAM` | Duct Settings → Round |

Rectangular / Oval 的尺寸表是**單一維度清單**，同時餵給寬與高兩個下拉。所以某個值只要**出現在任一元件的寬或高**，就算被使用。

例：模型裡有一支 2438×1371 的矩形風管 → 目錄中的 `2438` 與 `1371` **兩筆都算在用**，都不可刪。

### 2.2 管（Pipe）

管尺寸掛在 **PipeSegment**（材質 × Schedule）底下，每個 segment 各有一份目錄。所以用量要**逐 segment 判**：

1. 由 `Pipe` 元件取直徑（`RBS_PIPE_DIAMETER_PARAM`）。
2. 由該 Pipe 的 PipeType → `RoutingPreferenceManager` 取它實際用的 segment。
3. 「(segment, 直徑)」這個組合才是一筆用量。

同一個 25.4 mm 在 `Copper - K` 有用、在 `PVC - Sch 40` 沒用，是完全正常的情形。

### 2.3 不可遺漏：Fitting 與 Accessory

**只掃直管會漏。** 彎頭、三通、變徑、閥件（`OST_DuctFitting` / `OST_PipeFitting` / `OST_DuctAccessory` / `OST_PipeAccessory`）本身也有尺寸，來自它們的 **Connector**：

- `Connector.Width` / `Connector.Height`（矩形、橢圓）
- `Connector.Radius`（圓形；直徑 = 半徑 × 2）

一個尺寸完全可能「沒有任何直管在用，但有一個變徑頭的一端在用」。漏掃 fitting 就會把它誤判成可刪。

### 2.4 目錄外用量（orphan）

模型裡可能出現**不在目錄中**的尺寸（使用者手打了自訂值）。盤點時要單獨列出這類 orphan：它們代表目錄與實務脫節，是「該增」的候選，不是錯誤。

## 3. 法規對帳

盤點完之後，才有資料基礎回答「現有尺寸集是否滿足法規／標準的需求」。

### 3.1 先分清楚：法規 vs 設計手冊

> **法規只管下限／強制（附條號）；真正的 sizing 設計值在設計手冊／公會／ASHRAE（附出處）。兩者必須分開標，不可把設計手冊值寫成法規。**

台灣**沒有**任何法規明訂風管 sizing 的 friction rate 或風速上限。Revit `Duct Sizing` 對話框要填的三個數字（friction rate / velocity / sizing method）在台灣法規層是空白的。

唯一點名風管流速的條文是**建築技術規則建築設備編 §106 第四款**：

> 排煙管內風速每分鐘不得小於四五○公尺。

三個必須標清楚的性質，否則會被誤用：

1. 這是**排煙**（廚房煙罩排煙機）條文，不是一般供風 sizing。
2. 是「**不得小於**」——**下限**；與供風 sizing 的「不超過」**上限**方向相反。
3. 單位是 **m/min**，不是 m/s（450 m/min ＝ 7.5 m/s ≈ 1476 FPM）。

**不可把 §106 的 450 m/min 當成 Revit 供風管 sizing 要填的 velocity。** 用途、方向、單位三者皆不同。

### 3.2 設計值的正當出處（Tier B，非法規）

誠實標記：**✅** ＝ 讀過一手原文；**⚠️** ＝ 二手轉述，正式引用前須核原書。

| 值 | 出處與強度 |
|---|---|
| 等摩擦法 friction rate ≈0.08–0.10 in-wg/100 ft（≈0.65–0.82 Pa/m） | ASHRAE Fundamentals Ch.21 / SMACNA §5.9.2 ⚠️ 二手轉述 |
| Revit Equal Friction 對話框預設 0.10 in-wg/100 ft（＝25 Pa/30 m） | Autodesk Revit Help「About Duct Sizing Methods」✅ |
| 供風主管 1000–1500 FPM（5.1–7.6 m/s）、分支 600–1200 FPM | ASHRAE Fundamentals ⚠️ 二手 |
| >2000 FPM 起有可聞噪音；任何管段不宜超過 3000 FPM | ⚠️ 二手 |
| 給水管 ≤2.0 m/s（防水錘）、居室段 ≤1.5 m/s（防噪音） | SHASE-S 206／日規／業界通則 ⚠️；**非台灣§號** |

換算因子：1 in-wg/100 ft ＝ 8.172 Pa/m；1 FPM ＝ 0.00508 m/s ＝ 0.3048 m/min。
（業界口語把「≈1 Pa/m」對應 0.08–0.10 in-wg/100 ft **不精確**：1.0 Pa/m 實際 ≈0.12 in-wg/100 ft。）

### 3.3 單位先切，再開 Sizing 對話框

Sizing 對話框的 friction／velocity 單位**跟著 Project Units 走**，不是對話框自己決定。要用公制設計值就得：

1. 先跑 `set_project_units`（`mode='taiwan'`）→ friction 變 Pa/m、velocity 變 m/s；
2. 再開 Sizing 對話框，直接 key 公制值，不必在對話框裡心算 in-wg/100 ft ↔ Pa/m。

### 3.4 尺寸目錄與 sizing 的連動（本 domain 與 sizing 的接點）

`Used in Sizing` 這一欄決定「自動定尺寸時可以挑哪些尺寸」。因此：

**目錄裡沒有的尺寸，Revit 自動 sizing 永遠選不到；目錄裡有、但沒勾 `Used in Sizing` 的，一樣選不到。**

這就是為什麼 curate 尺寸目錄會直接影響 sizing 結果——把台灣採購得到的公制管徑補進目錄並勾起 `Used in Sizing`，自動 sizing 才會給出買得到的尺寸。反過來說，只改 Project Units 而不動目錄，sizing 仍然只會在原本那組英制尺寸裡挑。

## 4. 增減協定

> **增無限，減嚴格。** 這是本 domain 的核心規則。

### 4.1 增（Add）

新增尺寸沒有安全性限制 —— 多一個選項不會破壞任何既有元件。

- Pipe：`Segment.AddSize(new MEPSize(nominal, inner, outer, usedInSizeLists, usedInSizing))`
- Duct：`DuctSizeSettings.AddSize(shape, size)`
- 需要全新 schedule 系列時：`PipeScheduleType.Create` → `PipeSegment.Create`

**一律走 API，不要手動改對話框**（理由見 §5.1）。

### 4.2 減（Remove）—— 四步協定

**只有「模型中沒有任何既有元件在用」的尺寸才可以刪。** 執行順序不可跳步：

```
① 列表（dry run）
   ↓  盤點用量 → 標出每個候選的 usage count
   ↓  usage > 0 的一律擋下，不進入執行清單
   ↓  把要刪的每一筆完整定義（nominal / inner / outer / 兩個勾選旗標）存成復原清單
② 執行
   ↓  包在單一 Transaction（可 Ctrl+Z）
   ↓  Segment.RemoveSize(nominal) / DuctSizeSettings.RemoveSize(shape, nominal)
③ QC
   ↓  重新讀目錄，與「執行前目錄 − 預期刪除清單」逐筆比對
   ↓  多刪 = 誤刪；少刪 = 沒刪成功。兩種都要報出來
④ 誤刪復原
   ↓  以 ① 存下的完整定義原樣 AddSize 回去
   ↓  復原後再跑一次 ③ 確認回到預期狀態
```

**為什麼 ③ 是必要的而不是形式**：`RemoveSize` 以 nominal diameter 為鍵。同一 segment 內若有浮點誤差極接近的兩筆，或呼叫端傳入的 nominal 精度與目錄存的不完全相同，都可能刪錯對象或刪不掉。不比對就不會知道。

**為什麼要自帶復原清單而不是靠 Ctrl+Z**：Transaction 復原只在使用者當下手動按才有效；工具驅動的流程可能已經接了後續動作。復原清單讓工具自己能救回來。

### 4.3 擋下的情形要講清楚

被擋下的候選必須回報**為什麼**（哪些元件在用、幾個），不能只回一句「不能刪」。使用者要據此決定是「先改那些元件」還是「放棄刪這一筆」。

## 5. Revit API 陷阱（實測驗證，2026-08-06）

### 5.1 對話框會量化內徑 —— 進去按 OK 就會劣化資料

**進出 `Segments and Sizes` 對話框按 OK，Revit 會把所有 inner diameter 寫回成顯示精度。**

實測（Snowdon Towers Sample HVAC，Copper - K 全 16 筆）：

| nominal | 開檔當下 | 按過對話框 OK 之後 |
|---|---|---|
| 6.35 mm | 0.305"（真實銅管 K 型 ID） | 0.3125" = 10/32" |
| 25.4 mm | 0.995" | 1.0" = 32/32" |
| 152.4 mm | 5.741" | 5.75" = 184/32" |

修改後 16 筆**全部**是 1/32" 的整數倍，修改前一筆都不是。外徑未變（本來就落在 1/32" 格線上）。

**含意**：內徑正是水力計算與尺寸對帳要用的欄位。因此
- 對帳前先跑一次目錄盤點存基準；
- **增減一律走 API**，API 直接給 `MEPSize(nominal, inner, outer, …)`，不經過顯示精度的往返。

### 5.2 單位不是你以為的那樣

以下全部經反射 + 實測確認，**踩過的坑不要再踩**：

| API | 實際單位 | 錯誤做法 |
|---|---|---|
| `MEPSize.NominalDiameter / InnerDiameter / OuterDiameter` | 內部單位 **feet** | — 需 `ConvertFromInternalUnits(..., Millimeters)` |
| `DuctSettings/PipeSettings.GetSpecificFittingAngles()` | **已經是「度」** | 再做一次弧度→度會多乘 57.2958 倍 |
| `PipeSettings.GetPipeSlopes()` | **百分比** | 當成比值去格式化會大 100 倍（變成 12½" / 12" 這種荒謬坡度） |
| `PipeSettings.ConnectorTolerance` | **角度（弧度）** | 當長度轉 mm 會得到無意義的 26.6 mm（實際是 5°） |
| Duct 的 `MEPSize.InnerDiameter / OuterDiameter` | **固定佔位值（12 ft）** | 風管尺寸表只有 nominal 一個維度，輸出 inner/outer 會誤導 |

另：格式化坡度時要用 `UnitFormatUtils.Format(..., forEditing: true)`，否則專案若把坡度顯示精度設成 1/2"，1/8" 與 1/4" 會被捨進成同一個字串。

### 5.3 跨版本差異（R22–R26）

`MEPSize` / `Segment` / `PipeSegment` / `DuctSizeSettings` 介面**五版完全一致**，且 `PipeSegment` 是 `Segment` 唯一的衍生類別（所以掃管段用 `OfClass(typeof(PipeSegment))` 是完備的）。

但 `DuctSettings` 有兩處要條件編譯：

| 成員 | R22 | R23 | R24 | R25 | R26 |
|---|---|---|---|---|---|
| `AirViscosity` | ✔ | ✔ | ✔ | ✔ | ✘（移除） |
| `AirDynamicViscosity` | ✘ | ✘ | ✘ | ✔ | ✔ |
| `NetworkBasedCalculations` | ✘ | ✘ | ✔ | ✔ | ✔ |

→ 黏度用 `#if REVIT2025_OR_GREATER`，network-based 用 `#if REVIT2024_OR_GREATER`。

### 5.4 角度清單讀得到 ≠ 生效

`GetSpecificFittingAngles()` 一直回得出清單，但只有 `FittingAngleUsage == UseSpecificAngles` 時那份清單才真正管制。回報時必須一併標明 usage 模式，否則會讓人誤以為那些角度正在生效。

**關掉某個角度會讓既有彎頭失效**，風險等級高於改尺寸，不要與尺寸 curate 混在同一支工具裡。

## 6. 台灣在地化判讀

### 6.1 判斷原則

在地化**不是「全部換成公制」**，要逐 segment 判：

- **金屬管（鋼／銅／不鏽鋼）**：台灣 CNS 沿用 JIS「A」呼稱（15A / 20A / 25A…），**物理尺寸就是英制 nominal**（25A ＝ 1" ＝ 25.4 mm）。→ 現有目錄對台灣鋼管其實是**正確的**，只是標籤不直觀，**不該砍**。
- **PVC**：台灣 CNS PVC 系列 **≠** 英制 Schedule 40/80。→ **這才是真正要 curate 的部分。**

因為要逐 segment 判，所以 §1 的全量目錄盤點是前提。

### 6.2 仍缺的資料：離散尺寸系列（curate 前必須補齊）

**注意「設計值」與「尺寸系列」是兩種不同的資料，不要混為一談：**

| | 內容 | 狀態 |
|---|---|---|
| **連續設計值** | friction rate、風速上限 —— 填進 Sizing 對話框的數字 | 已有出處，見 §3.2 |
| **離散尺寸系列** | 目錄裡「有哪幾個尺寸」—— curate 要增減的東西 | **仍缺** |

補資料時每一筆需要的欄位：

| 缺什麼 | 需要的欄位 | 為什麼工具需要 |
|---|---|---|
| CNS PVC 管系列 | 每個呼稱的**外徑 + 管厚** → 換算內徑 | `curate_mep_sizes` 新增管尺寸強制要 `inner_mm` + `outer_mm` |
| CNS／JIS 鋼管 A 呼稱對照 | 呼稱 ↔ 實際外徑／內徑 | 用來驗證 §6.1「不必換」這個主張站不站得住 |
| 台灣風管標準尺寸級距 | Rect／Oval／Round 各自的標準尺寸表 | 風管只需 `nominal_mm`（inner/outer 是佔位值，見 §5.2） |

每一筆都要標 Tier 與 ✅/⚠️，比照 §3.2 的誠實框架。**補齊之前不要臆造尺寸** —— `curate_mep_sizes` 在缺 inner/outer 時寧可報錯也不代為填值，正是為此。

### 6.3 協定不等於標準

增減協定（§4）與目標尺寸集是兩件事，**協定不依賴標準內容**。所以在 CNS 對照補齊之前，工具仍可先用於：

- 盤點現況、找出 orphan（模型有用但目錄沒有的尺寸）；
- 刪除確認沒有任何元件在用的冗餘尺寸；
- 依專案自訂需求新增（由使用者提供 inner/outer）。

## 7. 相關

| 主題 | 去處 |
|---|---|
| 專案顯示單位切換（台灣 MEP `mode='taiwan'`） | `set-project-units` skill |
| MEP 與結構碰撞 | `mep-csa-clash-detection.md` |
| 開孔候選掃描 | `mep-opening-candidate-scan.md` |
| pyRevit MEP 擴充 | `mep-extension-guide.md` |
