# Image Plan · BIM_MCP 站點配圖規劃 → Codex CLI 執行

> **角色分工**：
> - **Claude Code（規劃者）**：定義「**為什麼放這裡 / 風格如何約束 / 該配什麼圖**」——本檔。
> - **Codex CLI（執行者）**：依本檔執行生成 + 置入，不做風格判斷。
>
> **撰寫日期**：2026-05-18
> **位置**：`docs/BIM_MCP/_IMAGE_PLAN_CODEX.md`
> **配套檔**：`docs/BIM_MCP/_HANDOFF_CODEX.md`（純頁面建構，與本檔正交）
> **目標完工**：5/23 月小聚前
>
> ---
>
> **本檔的核心承諾**：每一張圖在被生成前，都能回答三個問題——
> 1. **為什麼這頁要這張圖**？（內容驅動，非裝飾）
> 2. **為什麼放這個位置**？（閱讀節奏，非填空）
> 3. **為什麼這樣畫**？（單一風格憲法，非審美投票）

---

## 0. 章節地圖

1. [風格憲法 STYLE CONSTITUTION](#part-1-style)
2. [配圖邏輯 PLACEMENT LOGIC](#part-2-logic)
3. [完成狀態 COMPLETION STATUS](#part-3-status)
4. [檔案規範與引用方式](#part-4-files)
5. [一致性驗收檢查](#part-5-qa)

---

<a id="part-1-style"></a>

## 1. 風格憲法 STYLE CONSTITUTION（非可協商）

### 1.1 視覺基線

| 元素 | 規則 | 違反處理 |
|---|---|---|
| **背景** | 純黑 `#000000`，**不准**漸層、紋理、星空、模糊 | 圖片打回重生成 |
| **主色** | 純白 `#FFFFFF` 線條/形狀，**不准**米色、淺灰當主色 | 重生成 |
| **唯一品牌色** | `#60a5fa` 藍。每張圖至多用 1-2 處作 highlight（≤ 5% 畫面面積） | 重生成 |
| **狀態色** | `#ef4444` 紅（錯誤）/ `#4ade80` 綠（成功）/ `#fbbf24` 黃（警告）僅在對照圖出現，且僅一處 | 重生成 |
| **陰影** | **禁用**任何 box-shadow / drop-shadow | 重生成 |
| **3D 透視** | **禁用**任何 isometric / perspective / depth shading | 重生成 |
| **漸層** | **禁用**任何 linear/radial gradient（**例外**：圖 #13 hero quote 背景允許 ≤ 5% 不透明 radial glow） | 重生成 |
| **質感** | **禁用** noise / grain / paper texture / photo overlay | 重生成 |

### 1.2 構圖規則

| 規則 | 說明 |
|---|---|
| **單一概念** | 一張圖只表達一個概念。無法用一句話描述的圖砍掉 |
| **幾何優先** | 圓、方、線、箭頭、點。複雜形狀請拆成多個基本形狀 |
| **置中或對稱** | 主元素居中或鏡像對稱（極少數例外標明） |
| **留白 ≥ 30%** | 畫面至少 30% 是純黑空白，**不要填滿** |
| **文字嵌入** | 僅 JetBrains Mono uppercase，1-3 個字、tracking 1-3px |
| **不要繁中文** | 圖內文字**全英文**。中文留給 HTML 文字層處理 |
| **線粗** | 1px（細節）/ 2px（次主元素）/ 3-4px（主元素）三檔，**不可混超過 3 種** |

### 1.3 Jack Butcher / Visualize Value 美學基準

參考：https://visualizevalue.com/

**符合（DO）**：
- ✅ 「兩個方塊用一條線連起來表達一個關係」
- ✅ 「一個曲線從左下到右上 + 4 個標籤點」
- ✅ 「兩個並排圖案，一個劃 X 一個劃 ✓」
- ✅ 「同心圓 + 一條徑向線 + 三個 label」
- ✅ 黑底白線稿，等寬字旁註

**不符（DON'T）**：
- ❌ 任何寫實插畫（人臉、樹葉、機械零件細節）
- ❌ flat illustration 風格（圓潤角色 + pastel 色塊）
- ❌ 任何 Memphis / 80s / synthwave / vaporwave 風格元素
- ❌ 線條漸細漸粗（calligraphic stroke）
- ❌ 手繪 sketch 質感

### 1.4 攝影選項規則（hero 可選替代方案）

如果某 hero 想用真實攝影代替插畫：
- 必須是**高對比黑白**（不要彩色、不要 sepia）
- 主題限：**建築 / 工地 / 結構 / 都市天際線 / 工具特寫**
- 禁人臉、禁手勢、禁辦公室
- 必須**留有空白區**讓 HTML 文字疊上去可讀
- 必須**降低細節**（grain 可、銳利寫實不要）

→ 但攝影選項**整站最多用 1-2 張**。其餘必須是插畫，**維持家族感**。

### 1.5 「整站家族感」測試

把任意兩張產出圖並列，**必須看起來像同一個人/工作室畫的**。否則某張必須重做。具體：

- ✅ 線粗一致、字型一致、色票一致
- ✅ 留白比例相近
- ✅ 文字位置習慣相近（如全在底部置中）
- ❌ 一張是線稿、一張是面色塊 → 不行，挑一種
- ❌ 一張字在頂、一張字在左 → 不行，統一在底

---

<a id="part-2-logic"></a>

## 2. 配圖邏輯 PLACEMENT LOGIC（為什麼放這裡）

### 2.1 三類圖位定義

| 類別 | 位置 | 尺寸 | 任務 |
|---|---|---|---|
| **HERO 視覺** | 每頁 `<header class="bim-hero">` 內，標題下方 | 1600×900（16:9） | 一張圖讓人在 3 秒內理解本頁主旨 |
| **SECTION 開頭視覺** | 主要 section 的 `<h2>` 上方或右側 | 800-1600 寬 × 400-600 高 | 為該 section 的概念建立**圖示心錨** |
| **CARD 內嵌小圖** | 卡片左上 icon 位置 | 400×400（1:1） | 替代純文字標題，加快**掃讀** |

<a id="part-3-status"></a>

## 3. 完成狀態 COMPLETION STATUS

```
DONE (25):
  #1  index__hero__three-layer.svg
  #2  index__section01__site-map.svg
  #3  philosophy__hero__22-stars.svg
  #4  three-const__hero__pyramid.svg
  #5  three-const__c1__passive-vs-active.svg
  #6  three-const__c2__tool-vs-prior.svg
  #7  three-const__c3__sop-vs-intuition.svg
  #8  industry__hero__cost-curve.svg
  #9  industry__s01__three-stats.svg
  #10 industry__s03__timeline.svg
  #11 spectrum__hero__01-vs-spectrum.svg
  #12 spectrum__s02__decision-tree.svg
  #13 spectrum__quote__resign-king.svg
  #14 skills-domain__frame__concentric.svg
  #15 skills__hero__19-grains.svg
  #16 domain__hero__35-grains.svg
  #17 deployment__hero__one-cmd.svg
  #18 deployment__s02__arch.svg
  #19 troubleshooting__hero__9-cases.svg
  #20.1 troubleshooting__fix1__overflow.svg
  #20.2 troubleshooting__fix2__view-pollution.svg
  #20.3 troubleshooting__fix3__batch.svg
  #20.4 troubleshooting__fix4__by-design.svg
  #21 contributor__hero__dual-write.svg
  #22 contributor__s04__6-step.svg

TODO:
  none
```

---

<a id="part-4-files"></a>

## 4. 檔案規範與引用方式

- 圖檔目錄：`docs/BIM_MCP/_images/`
- 命名規則：`<page-stem>__<position>__<concept>.svg`
- reference 頁面引用：`../_images/<filename>.svg`
- hub 頁面引用：`_images/<filename>.svg`
- SVG 必須可被 git review，優先用外連 `<img>`，避免 inline 長 SVG 汙染 HTML。
- alt text 寫概念，不寫裝飾描述；純背景圖可用 `alt=""` 並加 `aria-hidden="true"`。

<a id="part-5-qa"></a>

## 5. 一致性驗收檢查

- 25 張 SVG 均已放入 `_images/`。
- 每張 SVG 已掛到對應頁面或對應 section/card。
- 共用 figure 樣式已集中於 `styles.css`，保留少量舊頁 inline style 但不影響新圖。
- SVG 已用 XML parser 驗證可解析。
- HTML 內引用的 SVG 檔案已確認存在。

**END OF IMAGE PLAN**

> **後續配合**：
> - 頁面建構 / 互動升級 → `_HANDOFF_CODEX.md`
> - 配圖（25 張，**Part 7 是 single source**）→ Codex CLI 引用本檔執行
> - 風格憲法（Part 1）為最高優先級——任何違反一律重做
>
> Codex 若對 prompt 或位置有疑問，**先回 Claude（總體規劃者）**，不自行決定風格事項。
