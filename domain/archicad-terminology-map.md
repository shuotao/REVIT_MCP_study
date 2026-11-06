---
name: archicad-terminology-map
description: "Revit 到 Archicad 的核心名詞與資料歸屬對照，以及讀取數值前必須先確認單位基準的鐵律。當使用者要把 Revit 的 Room／Family／Parameter／Sheet 等名詞轉成 Archicad 說法、或要讀取 Archicad 屬性數值做算量時參考。"
metadata:
  version: "1.1"
  updated: "2026-09-08"
  created: "2026-09-08"
  references:
    - "https://github.com/shuotao/REVIT_MCP_study/issues/98"
  related:
    - archicad-adapter-boundary.md
    - archicad-portability-matrix.md
    - quantity-takeoff-excel.md
  referenced_by: []  # 尚未被任何 SKILL.md 引用；接線待 archicad-skill-adapter 更新
  contributors:
    - "Archwiz-boss"
  tags: [archicad, terminology, mapping, units, property, 單位, 名詞對照]
---

# Archicad 名詞與資料對照（中文導讀）

## 本檔定位

本檔是**中文導讀與速查**。完整對照（含 Documentation／Collaboration 名詞、Graphisoft 官方出處連結、Property／Classification／Attribute／Library Part 四系統歸屬規則）的**權威正本**是：

> `.claude/skills/archicad-skill-adapter/references/revit-archicad-terminology.md`

兩者衝突時**以正本為準**。本檔只負責兩件正本較難速查的事：中文對照，以及下方第 3 節那條會靜默算錯的單位鐵律。

## 1. 核心模型對照

| Revit 概念 | Archicad 對應 | 對應層級 | 注意事項 |
|---|---|---|---|
| `ElementId` | `Element GUID` | **禁止直譯** | 格式不同，嚴禁跨 backend 複製、轉型或比較。 |
| `Category` | `Element Type` ／ `Classification` | 近似 | Element Type 決定可呼叫的 API；Classification 是另一套語意分類。 |
| `Room` | `Zone` | 近似 | 邊界計算、標籤與分類邏輯都不同。**兩者的面積都不是法規面積**，見第 4 節。 |
| `Level` | `Story` | 近似 | 樓層歸屬與高程行為不同。 |
| `Family` | `Library Part` ／ 工具原生元件 | **無 1:1** | 不要去找 Family 容器；先問使用者要的是幾何、可重用內容，還是分類。 |
| `Parameter` | `Property` ／ `Attribute` ／ GDL 參數 | **無 1:1** | 先確認資料的實際擁有者是哪一套系統。 |
| `Material` | `Building Material` ＋ `Surface` | **無 1:1** | 結構組成與外觀是兩種不同的 attribute。 |
| `Revit Link` | `Hotlink Module` | 近似 | 目前 API 無法判斷 hotlink 內元素的歸屬權。 |
| `View` | `View Map item` ／ Saved View | 近似 | 區分「當下視點」與「存起來的視圖設定」。 |
| `Sheet` | `Layout` | 近似 | 走 Layout Book API。 |
| `Title Block` | `Master Layout` 內容 | 近似 | Layout 基於 Master Layout，不是圖框 Family instance。 |
| `View Template` | Saved View 設定組合 | **無 1:1** | 圖層組合、Model View Options、Graphic Override、Renovation、比例是分開組成的。 |

## 2. 資料歸屬：先問「誰擁有這筆資料」

Revit 幾乎把元件資料都收進「參數」；Archicad 拆成四套各自用不同指令查詢的系統。**選錯系統會得到空結果，而空結果看起來就像資料不存在。**

| 系統 | 存放內容 |
|---|---|
| **Property** | 元件層級的資料值（內建與自訂） |
| **Classification** | 元件被歸入的語意分類體系 |
| **Attribute** | 專案層級的具名資源：Layer、Line、Fill、Composite、Surface、Profile、Pen Table、Zone Category、MEP System、Building Material |
| **Library Part 參數** | Object／Door／Window／Lamp 的 GDL 參數 |

### 沒有 Revit 對應物的三條規則

1. **Property 可用性由 classification 決定。** 一個 property 在專案裡存在，仍可能因為元件的分類而在該元件上不可用。實測：把窗／門群組的面積 property 讀在 Zone 上，回傳的是 `None`，且回應中沒有任何欄位可以區分「無值」與「此元件不適用」。**不可把空值直接當成 0 或當成缺資料回報。**
2. **不要手打 property 名稱，也不要從全域定義表挑。** 正確順序是：先用 `properties_get_all_property_ids_of_elements` 問**這個元件**有哪些 property，再與定義表 join。實測一個 Zone 擁有 312 個 property id，全數（312／312）可在 1766 筆定義表中解析。
3. **要用對指令。** 同一批 property，用 `properties_get_details_of_properties` 查會回 error `6702 Property definition not supported`，用 `properties_get_all_properties` 卻能完整解析。**單一指令回錯誤不等於該 property 不存在。**

## 3. 數值與單位合約 ⚠️ 會靜默算錯

Archicad 的數值從兩層回來，兩層對不起來。這是 Archicad 自身的設計，不是 wrapper 缺陷。

| 層 | 回傳內容 | 遵循 |
|---|---|---|
| **屬性層** | 依專案計算單位格式化的**顯示字串**，可能夾帶單位符號 | 專案的**計算單位** |
| **幾何層** | 原始型別數字 | 純 SI：公尺、平方公尺、立方公尺、弧度 |

### 實測：schema 宣告的型別不是你拿到的型別

同一個 Zone，property 定義宣告 `propertyValueType: "Real"`，實際回傳全部是字串：

| Property | measure | 宣告型別 | 實際回傳 | 實際 Python 型別 |
|---|---|---|---|---|
| 計算面積 | Area | `Real` | `'3.46'` | `str` |
| 被測量的面積 | Area | `Real` | `'8.15'` | `str` |
| 牆表面面積 | Area | `Real` | `'23.31'` | `str` |
| 牆週長 | Length | `Real` | `'7.91'` | `str` |
| 門寬度 | Length | `Real` | `'0.80'` | `str` |

同一時間，幾何層對同一面牆的傾斜角回的是 `1.570796327`（弧度），屬性層回的是 `"90°"`。兩者是同一個角度。

上表全部是 2 位小數，因為該專案的計算單位設定是 Meter／2 位小數 —— **小數位數來自專案設定，不是來自數值本身**。

### 開發者鐵律

- **讀值前先呼叫 `project_get_calculation_units`**，並在報告中明列數據單位基準。
- **不可把 property 字串直接當數字運算或分組。** 它可能夾單位符號，小數位來自專案設定。
- **不可混用兩層。** 屬性層的長度與幾何層的厚度不保證同單位。
- **`domain/` 底下的算量方法全部以 mm 計算。** 把 Archicad 的公尺值餵進那些公式，是 1000 倍誤差，而且下游每一個總計都會照單全收，不會有任何錯誤或警告。
- **同名不同義要分辨。** 實測同一個 Zone 同時有「計算面積 `'3.46'`」與「被測量的面積 `'8.15'`」，差異來自不同的面積定義，不是誤差。
- **Archicad 的「工作單位」是第三個設定**，管的是繪圖介面，與 API 回傳無關。使用者說「我的專案是公分」通常指的是它。

**離開這個轉譯層的每一個數值，都必須附帶單位基準。沒有單位基準的數字不算證據。**

## 4. Zone 面積的應用邊界 ⚠️ 取得數字 ≠ 可以拿來用

工具能從 Zone 提取面積，不代表那個面積可以用在任何地方。這條邊界是建築專業判斷，不是 API 限制，**不會有任何錯誤訊息提醒你越界**。

### 建模約定先行

Zone 要算「總面積」還是「淨面積」，**必須在建模階段就定義並統一**，不是在算量階段才挑一個。Zone 的建構方式（邊界怎麼畫、抓牆心還是完成面、是否含柱）直接決定提取出來的數字代表什麼。

Archicad 同時提供多個名字都像「面積」的屬性（計算面積、被測量的面積、測得淨面積、縮小面積…），實測同一個 Zone 可能一個是 `'49.95'`、另一個是 `'53.34'`。**這不是要在報表階段裁定的問題，而是建模約定沒有統一時才會浮現的症狀。** 同一批 Zone 若沒有共同依據，任何加總都不成立。

### 可以用在哪

- **初步規劃階段的量體檢核**：大致判斷容積、建蔽率**是否明顯超出**法規值，作為方案調整的參考。

### 不可以用在哪

- **不得作為建照申請的面積依據。** 送審的面積計算是另一條獨立工作流，必須以**面積單線圖**與計算式逐項證明，不能以模型的 Zone 面積代替。
- **不得直接當成容積率、建蔽率的計算結果。** 那兩者各有自己的計算規則（何者計入、何者折減、何者免計），與 Zone 面積的加總不是同一回事。
- **不得把「初步判定不超過法規值」寫成「符合法規」。** 前者是規劃參考，後者要經正式面積計算與檢討。

### 面積計算的捨入規則：先個別四捨五入，再加總

這條規則與 backend 無關（Revit 側同樣適用），但它決定了從模型取出的面積數字能不能對得上圖面。

建照審查**不接受直接把程式算出的面積值寫在圖面上**。必須搭配**面積單線圖**表明每一塊的形狀與長度，列出計算式，才能作為依據。而依規範算出來的面積，與程式直接計算的面積**會有小數位差距**，這是正常的，不是誤差。

正確的計算順序：

1. 面積單線圖的長度**取自圖面上顯示的長度**，以公尺標示，至少三位小數（三或四位，未特別限定上限）。
2. **一個形狀一條計算式**，計算結果為平方公尺。
3. 每一條計算式的結果**個別四捨五入到小數點後兩位**。
4. 再把各項的**顯示值**加總。

**絕對要避免以原值相互加總。** 六位小數的原值直接加總，會因進位而與「先個別四捨五入再加總」的結果不同。先捨入再加總，才能確保後續每一步都與圖面對得上，事後保存登記也不會出問題。

### 這條規則與 Archicad 兩層數值的關係

回頭看第 3 節的兩層模型，它們各自對應到這條規則的不同位置：

| 層 | 回傳 | 在面積計算中的角色 |
|---|---|---|
| 屬性層 | 依專案計算單位格式化的**顯示字串**（本專案為 2 位小數） | 形式上接近規範要的「顯示值」，但仍**不可**直接充當計算依據——它沒有對應到面積單線圖的形狀與長度 |
| 幾何層 | 原始 SI 數值（6 位以上小數） | **不可直接加總**。它正是上面禁止的「原值」 |

換句話說：幾何層的高精度在這裡不是優點。**用它直接加總得到的總面積，與依規範製作的面積計算表不會一致，而且不一致的方向無法事後補救。**

### 對轉譯層的要求

任何從 Archicad 取出 Zone 面積的流程，輸出時必須同時載明：**用的是哪一個面積屬性、Zone 的建構約定為何、以及這份數字的適用範圍**。缺少其中任何一項，數字就可能被下游當成它不是的東西使用。
