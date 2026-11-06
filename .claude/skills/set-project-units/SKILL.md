---
name: set-project-units
description: Switch a whole Revit project's display units in one tool call via the set_project_units MCP tool. Use when the user asks to change project units, "改專案單位 / 切公制 / 一鍵改單位", set Air Flow to m³/h for Taiwan §102, set plumbing units for 給排水 (L/min, mH2O, 1:ratio slope), or align units to a project standard. For Taiwan HVAC use mode='taiwan'; for Taiwan 給排水 use mode='taiwan-plumbing'.
user-invocable: true
---

# set-project-units

One-call project-wide unit switching, replacing the manual `Manage → Project Units → per-discipline` clicking. Backed by the `set_project_units` MCP tool (`MCP/Core/Commands/CommandExecutor.ProjectUnits.cs`), which calls `Document.SetUnits(...)` inside a single reversible Transaction (Ctrl+Z).

## When to use
- "改專案單位", "切成公制/英制", "一口氣改所有單位", "set project units", "unit conversion".
- Taiwan MEP load/ventilation work where 建築技術規則 §102 needs Air Flow in **m³/h** (the course dataset is imperial CFM).
- Taiwan **給排水** work where 建築設備編 §43/§46 and 給水排水設備設計技術規範 need mm / L/min / 壓力 / 1:n 坡度.
- Aligning a model to a project unit standard before building schedules / load tables (Appendix B workflow).

## Key facts
- **Whole-project action.** Changes display units for every element at once, in one Transaction. Reversible with Ctrl+Z.
- Units are **per-discipline**: changing Length/Area does NOT change Air Flow or piping Flow — independent `SpecTypeId`s. The `taiwan*` modes handle the relevant ones for you.
- **Each spec is set with unit + symbol + accuracy, not just the unit.** Setting only the unit is what produced two real defects on 2026-08-20 (see below).
- ⚠️ **C-c caution (report.md 附錄 C):** changing units is project-wide. If the model already has sized duct/pipe systems, confirm it did not disturb Mechanical Settings / Segments-and-Sizes. Decide units at project-setup stage (階段 2-C), not mid-way.

## 兩個實測踩過的坑（2026-08-20，M1_04 給排水）

**① Length 的單位符號**
Revit 公制預設**不帶** Length 符號 → 標高與長度顯示成裸數字（`673` / `690` / `35221`），
而管徑類卻有 `25 mm`。同一張圖兩種寫法，對帳時分不出來。
兩個 `taiwan*` 模式都會補上 `SymbolTypeId.Mm`。

**② 精度不足會製造假綠燈**
流速精度 0.1 位時，`2.98` 與 `3.05` 都顯示成 `3.0`。當時拿英制 `10 FPS`（本身是 9.777 進位）
換算成 3.05 去驗證，**看起來通過了**，調到 0.01 位才發現真值是 2.98、預測偏高 2.3%。
`taiwan-plumbing` 因此把流速精度釘在 **0.01**。

> 通則：**不得用四捨五入後的顯示值做換算驗證**——驗證程序本身會產生假綠燈。

## 🔴 kgf/cm² 在 Revit 2026 不存在

台灣法規（設備編 §46、技術規範 3.4.4／3.4.6）的壓力單位是 **kgf/cm²**。
逐一反射 `RevitAPI.dll` 的 **305 個 `UnitTypeId`** 確認：**沒有這個單位**——
只有 `KilogramsForcePerSquareMeter`（差 10000 倍）與 `KilonewtonsPerSquareCentimeter`。

故 `taiwan-plumbing` 以 **mH2O 代用**，換算係數為整數：

| 法規值 | kgf/cm² | Revit 顯示 (mH2O) |
|---|---|---|
| 設備編 §46 給水壓力下限 | ≥1.7 | **≥17.0** |
| 技術規範 3.4.4 一般水栓 | ≥0.3 | **≥3.0** |
| 技術規範 3.4.4 沖洗閥 | ≥1.0 | **≥10.0** |
| 技術規範 3.4.6 須設減壓 | >3.5 | **>35.0** |

未採 kPa（×98.0665，需心算）與 bar（×0.980665，接近 1 易被誤讀為等值）。

## ⚠️ 坡度 `1:ratio` 的判讀陷阱

技術規範 3.2.2 寫 **1/50、1/100**，對應 `UnitTypeId.OneToRatio`（顯示 `1 : n`，變數在右）。
反向的 `RatioTo1` 會把 1/100 顯示成 `0.01 : 1`——數字對，但寫法與法規相反。

**但 `1:ratio` 有代價**：**零坡度會顯示成 `1:0.00`**，字面語意是相反的
（`1:0` 在數學上代表無限陡，實際卻是「平的」）。看圖的人照字面讀，
會把**沒做洩水坡度的管誤判為陡管**。教材必須明講這一點。

## Procedure
1. **Re-anchor** active Revit state first (repo CLAUDE.md rule): call `get_project_info` (and `get_active_view` if a view matters). If the bridge is unreachable, stop and say so.
2. **確認開的是複本，不是原始資料集檔**（鐵則 #1）。單位切換是全案寫入。
3. **切換前先抓基線**：讀一顆代表元素的完整參數（`get_element_info`），存進單位凍結紀錄。
   沒有基線就無法證明某個數字是換算來的還是被改過的。
4. **寫下預測再切**：把關鍵欄位的換算值先寫下來，切完讀回比對。先切再解釋＝事後合理化。
5. **Call `set_project_units`**：
   - Taiwan 空調：`{ "mode": "taiwan" }`
   - Taiwan 給排水：`{ "mode": "taiwan-plumbing" }`
   - 純公制／英制：`{ "mode": "metric" }` / `{ "mode": "imperial" }`
   - 細調（疊在 mode 之上）：`length` `area` `volume` `airFlow` `pipeSize` `flow` `velocity` `pressure` `friction` `slope`
6. **One call at a time.** The bridge holds a single-connection lock; concurrent calls wedge it with HTTP 409 (recover with the ribbon's 「切換/釋放連線」 button).
7. **Verify by read-back, not by echo.** 回傳的 `Result` 是**套用後從 `Document` 回讀**的實際 unit/symbol/accuracy。
   再讀一次步驟 3 那顆元素，逐格對照步驟 4 的預測。
8. Report what changed and remind the user it is Ctrl+Z reversible.

## Parameters (set_project_units)

| Param | Values | Notes |
|---|---|---|
| `mode` | `taiwan` \| `taiwan-plumbing` \| `metric` \| `imperial` | 選這個或 `system`。 |
| `system` | `metric` \| `imperial` | `mode` 省略時的基底（預設 metric）。 |
| `length` | `m` `mm` `cm` `ft` `ft-in` | 覆寫。 |
| `area` | `m2` `sf` | 覆寫。 |
| `volume` | `m3` `l` `cf` | 覆寫。 |
| `airFlow` | `m3/h` `l/s` `cfm` | 覆寫（§102 → `m3/h`）。 |
| `pipeSize` | `mm` `cm` `m` `in` | 覆寫。 |
| `flow` | `l/min` `l/s` `m3/h` `gpm` | 覆寫（技術規範 3.4 → `l/min`）。 |
| `velocity` | `m/s` `fps` | 覆寫。 |
| `pressure` | `mh2o` `mmh2o` `kpa` `pa` `bar` `kgf/m2` | 覆寫。**無 kgf/cm²**，見上。 |
| `friction` | `mmh2o/m` `mh2o/m` `pa/m` | 覆寫。 |
| `slope` | `1:ratio` `ratio:1` `%` `deg` | 覆寫。台灣寫法為 `1:ratio`。 |

## 兩個 taiwan 模式各設了什麼

| Spec | `taiwan` | `taiwan-plumbing` | 精度 | 符號 |
|---|---|---|---|---|
| Length | mm | mm | 1 | `mm` |
| AirFlow | m³/h | m³/h | 0.1 | 預設 |
| PipeSize | — | mm | 1 | `mm` |
| Flow | — | L/min | 0.1 | `L/min` |
| PipingVelocity | — | m/s | **0.01** | `m/s` |
| PipingPressure | — | mH2O | 0.01 | `mH2O` |
| PipingFriction | — | mmH2O/m | 0.1 | `mmH2O/m` |
| PipingSlope | — | 1 : ratio | 0.01 | `1:` |

其餘 spec 一律沿用該基底系統（metric/imperial）的 Revit 預設。

## Guardrails
- Never claim units changed without the tool's success `Result` in this turn (repo Tool-Call-Data-Honesty rule).
- Do not run against a model with committed duct/pipe sizing without flagging the C-c side-effect risk first.
- 個別失敗不致命：每個 spec 各自 try/catch，失敗會出現在 `Applied` 的 `error`／`warning`／`symbol: "failed: ..."` 欄位。
  **回報前務必掃過 `Applied`**，不要只看 `Success: true`。
