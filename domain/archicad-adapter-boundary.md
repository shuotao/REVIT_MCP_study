---
name: archicad-adapter-boundary
description: "Archicad MCP 轉譯邊界與限制：Revit 預設不變、Archicad opt-in 的架構原則，以及因 API／wrapper 能力落差而必須降級或改道的功能清單。當使用者要把既有 Skill 套到 Archicad、或要判斷某個能力在 Archicad 是否可行時參考。"
metadata:
  version: "1.1"
  updated: "2026-09-08"
  created: "2026-09-08"
  references:
    - "https://github.com/shuotao/REVIT_MCP_study/issues/98"
    - "https://github.com/SzamosiMate/tapir-archicad-MCP/issues/24"
  related:
    - archicad-terminology-map.md
    - archicad-portability-matrix.md
    - tool-capability-boundary.md
  referenced_by: []  # 尚未被任何 SKILL.md 引用；接線待 archicad-skill-adapter 更新
  contributors:
    - "Archwiz-boss"
  tags: [archicad, boundary, capability-gap, adapter, 轉譯邊界]
---

# Archicad Adapter Boundary（轉譯邊界與限制）

## 本檔定位

本檔是**中文決策依據**：判斷某個 Revit 側能力在 Archicad 該不該做、要不要降級、走哪條路。

**權威正本另有其檔，本檔不取代它們**：

| 內容 | 正本 |
|---|---|
| 逐一指令的實測行為、回傳形狀、版本差異 | `.claude/skills/archicad-skill-adapter/references/archicad-runtime-facts.md` |
| Wave 2 各 Skill 的轉譯規劃與驗收條件 | `docs/integrations/archicad-skill-translation-wave2.md` |
| 名詞對照與單位合約（英文完整版） | `.claude/skills/archicad-skill-adapter/references/revit-archicad-terminology.md` |

本檔與上列任一檔衝突時，**以上列正本為準**，並回報差異。

## 實測基準

本檔的能力判斷來自下列環境的實機測試。換版本必須重測。**各節的證據日期不同**，沿用先前觀察的部分已於該節標明。

| 項目 | 值 |
|---|---|
| Wrapper | `tapir-archicad-mcp` `0.5.3` |
| Archicad | 28 |
| Tapir Add-On | 1.5.8 |
| 測試日期 | 2026-09-08 |
| 測試專案 | 單機專案，計算單位 Meter／2 位小數／DecimalDegree |

## 1. 架構原則（Revit-first, Archicad opt-in）

- **隔離執行**：Revit MCP 與 Archicad MCP 的 backend 完全獨立。git 內的 `.mcp.json` 與 `.vscode/mcp.json` 維持 Revit-only，Archicad 由使用者主動加入；停用只需移除 `archicad-mcp` 項。
- **識別碼不共用**：禁止將 Revit `ElementId` 與 Archicad `GUID` 互相傳遞、比較或混入同一 payload。不同 Archicad instance port 的資料同樣不可混用。
- **不改 Revit 側**：不修改 Revit MCP 原始碼、Add-in、port `8964`、build 或部署腳本。

## 2. 執行前提（不滿足就不要開始）

這三項在 2026-09-08 實測中都會造成「看起來像能力不存在」的假象：

1. **wrapper 啟動需要 pin `mcp<2`。** `uvx --from tapir-archicad-mcp==0.5.3 archicad-server` 在目前的依賴解析下會抓到 `mcp` 2.x，而 `0.5.3` 仍 import v1 的 `mcp.server.fastmcp`，server 直接以 `ModuleNotFoundError` 結束。可行的啟動參數需加 `--with "mcp<2"`。
2. **同一個 MCP session 內必須先呼叫 `discovery_list_active_archicads`。** 未先呼叫時，即使 port 正確，`archicad_call_tool` 也會回 `Port <n> is not an active Archicad connection`。discovery 不只是取得 port，它同時建立這個 session 的連線註冊。
3. **參數要包在 `params` 裡。** `0.5.3` 的 `archicad_call_tool` arguments 形狀是 `{port, params:{…}, page_token?}`。把指令參數平放在 arguments 頂層，會被 pydantic 以 `extra_forbidden` 連同 `port` 一起拒絕，錯誤訊息指向 `port`，容易誤判成 port 有問題。

## 3. 能力落差與降級策略

### A. 圖面視覺覆寫（Graphic Overrides）

> 證據日期：schema 於 **2026-09-08** 重讀確認（欄位與必填組合如下）；下方的單位風險由同日的
> property 實測直接支持。

- **限制**：Revit 可對特定視圖永久覆寫元件顏色；Archicad MCP 目前只提供暫時性的 `elements_highlight_elements`。
- **邊界**：降級為「暫時高亮」。不宣稱能寫入圖層或存進特定 View。
- **視覺副作用比「高亮」兩個字大得多。** schema 有一個 `wireframe3D` 選項，開啟後**會把 3D 視窗裡所有未被高亮的元素切換成線框**（另有 `nonHighlightedColor` 可改非高亮元素的顏色）。使用者看到的是「整個模型變線框，只剩一個元件有顏色」——那是一次 highlight 呼叫的結果，不是兩件事，也不是模型損壞。
- **這不是 Archicad 的顯示設定，UI 裡關不掉。** 它是 Tapir 疊加的臨時狀態；3D Styles 與 Model View Options 都沒有對應開關，實測 Model View Options 本身也不會被改動。使用者在自己的軟體裡找不到地方關，是預期行為。
- **清除路徑**：內建於同一支指令 —— `elements` 與 `highlightedColors` **兩者皆為必填**，各傳一個空陣列即清除所有既有 highlight，線框同時恢復。因此「可清除」這項前置條件**讀 schema 即可確認**，不需要真的先染色再測試清除。
- **對使用者的義務**：任何會下 highlight 的流程，必須在同一次互動中一併告知如何清除，或在流程結束時自動清除。否則使用者會面對一個在自己軟體裡找不到開關的狀態。
- **附帶風險**：若以 property 值作為分組上色的鍵，該值是依專案計算單位格式化的字串（見 [archicad-terminology-map.md](archicad-terminology-map.md)），兩個實際不同的數值可能格式化後相同而被併成同一組。

### B. 碰撞檢查（Clash Detection）

> 證據日期：本節全部沿用 **2026-08-19** 的觀察，**2026-09-08 未重測**。

- **限制**：`elements_get_collisions` 只回傳 GUID 對與布林結果，無法提供穿透深度、截面積或精確出入口座標。
- **`settings` 的正確用法**：可以整個省略（預設 null）；但**一旦給定，`volumeTolerance`、`performSurfaceCheck`、`surfaceTolerance` 三者皆為必填**（預設值 `0.001` / `false` / `0.001`）。給一半會被拒絕。
- **邊界**：降級為「縮限版」——只產出 GUID 對撞報告，不對外提供推測的穿透深度與體積估算。
- **Hotlink 排除（有反面證據）**：`project_get_hotlinks` 只回傳 `location` 字串陣列，沒有模組名稱、沒有 GUID ownership，無法判斷哪個元素屬於哪個 hotlink。因此第一版碰撞檢查**不包含** Hotlink 元素。附帶提醒：專案若含 hotlink 節點，任何元素計數都必須聲明是否包含 hotlink 內容。

### C. 牆面方向與內外側判定（Wall Orientation）

**這一項不是能力缺口，是路線選擇。** 早期判斷「Archicad 缺少 Revit `Wall.Flipped` 等價值，只能降級為人工檢查」的前提已不成立。

對應的 Revit 側方法見 [wall-check.md](wall-check.md)，其判斷邏輯之一是「房間檢測：Exterior 側不應該有房間」。Archicad 有兩條原生路線可以回答同一個問題：

| 路線 | 指令 | 語意 | 實測結論 |
|---|---|---|---|
| **關係路線（建議）** | `elements_get_relations_of_elements` 對 Zone | `zoneRelations.wallParts` 回 `{elementId, roomEdgeIndex, begDistance, endDistance}`，即「哪一段牆構成這個 Zone 的第幾條邊」 | **可用且可規模化**，一次可帶 50 個 Zone |
| 邊界路線 | `elements_get_zone_boundaries` | 直接回 `isExternal`、`neighbouringZoneElementId`、幾何層 `area`、`polygonOutline` | 語意更直接，但**不可靠，不要當主路線**（見下） |

**關係路線的實測結果**（測試樓層：105 個 Zone、280 面牆）：

- 105 個 Zone **全數**回傳 `wallParts`，0 個空關係。
- 建立 wall → Zone 反向索引後：**196 面牆被 2 個以上 Zone 界定（內牆候選）、69 面牆只被 1 個 Zone 界定（外牆候選）、15 面牆不被任何 Zone 界定（無判定）**，覆蓋率 **94.6%**。
- `wallParts` 引用到的牆，**0 筆**落在該樓層之外。

**邊界路線為何不可當主路線**：`elements_get_zone_boundaries` 是單 Zone 呼叫且回傳完整 3D 多邊形。實測中它**只有首次呼叫成功**，之後對**同一個 GUID** 的重複呼叫持續逾時（60 秒以上，三次嘗試皆然）。整層逐一呼叫在本專案規模下無法完成。

**邊界規則**：

- 判定結果一律標為**候選**，不自動宣稱 correct／incorrect，也不自動翻轉任何牆。
- 「無 Zone 界定」必須與「確實無邊界」分開回報，不可省略、不可當成外牆。
- 空的 relations 結構同樣要與「有結構但無 wallParts」分開回報。

**`GetDetailsOfElements` 仍然壞著，但不影響這條路線**：實測回 522 個 validation error（上游 [tapir-archicad-MCP#24](https://github.com/SzamosiMate/tapir-archicad-MCP/issues/24)，仍 open）。值得注意的是 **`flipped` 已被 `0.5.3` 接受**，仍被拒的是 `referenceLineLocation`、`profileType`、`slantAlpha`／`slantBeta`、`topOffset`、`visibility`、`zoneRel` 等 14 個欄位。這是 wrapper 回應模型過期造成的，**不是 Archicad 的 API 缺口**——資料一直都在，錯誤訊息本身就夾帶了那些值。

### D. 分頁與大量列舉

- 實測在 `0.5.3` 上，未加 filter 的全量 `Wall` 列舉 **7376 個 GUID／74 頁／0.6 秒**完整走完，未出現 session 逾時。早期在 `0.4.3` 觀察到的「約 1600 個 GUID 後 `Pagination session expired`」在此版本未重現。
- 但**分頁未走完就下結論，是本轉譯層最容易犯的錯**。實測中 `properties_get_all_properties` 首頁只回 100 筆，據此會誤判「專案裡沒有面積屬性」；走完分頁實際有 **1766 筆／18 頁**。
- **規則**：任何「查不到 ⇒ 能力不存在」的結論，必須先證明分頁已走完。

### E. 模型寫入（兩道強制關卡）

Archicad 的 property 寫入有一個會讓人誤判成功的失敗模式，**任何寫入流程都必須加上這兩道關卡**。

**關卡一：寫入前確認目標可編輯。**

`propertyIsEditable` 描述的是**屬性本身**，不是「這個元素現在能不能寫」。元素層級的判準是
`elements_get_elements_by_type` 的 `IsEditable` filter。實測一個鎖定的樓層：

| 查詢 | 結果 |
|---|---|
| `Zone` + `["OnActualFloor"]` | 105 |
| `Zone` + `["OnActualFloor", "IsEditable"]` | **0** |
| `Zone` + `["IsVisibleByLayer", "OnActualFloor"]` | 105 |

該層完全可見、可讀，但一個元素都寫不進去。filter 之間是 AND，`IsEditable` 是唯一能預測寫入會不會生效的那一個。**掉出過濾的元素列為 skipped 回報，不要硬寫。**

**關卡二：逐筆檢查 `executionResults`，並且一定要 read-back。**

`properties_set_property_values_of_elements` 把失敗放在 per-element 的 `executionResults` 裡，**工具層不會回報錯誤**：

```json
{"executionResults": [
  {"success": false, "error": {"code": -2130312909, "message": "Failed to set property value for element"}}
]}
```

實測：對鎖定樓層的 105 個 Zone 重新編號，回報「五個批次全部成功、耗時 1.1 秒」，實際一個都沒改。只檢查工具層結果的呼叫端會直接宣稱成功。

**確認可編輯後，寫入路徑本身是可靠且可逆的**：42 個 Zone 的編號寫入 `executionResults` ok=42／failed=0，read-back `success=42／unchanged=0`；單筆寫入後改回原值也同樣成功。

**其他寫入注意事項**：

- **沒有 `elements_modify_zones`。** Wall／Column／Beam／Door／Object 等都有對應的 `elements_modify_*`，Zone 沒有。Zone 的資料修改只能走 property 寫入。
- **原子性未知。** 無法假設批次寫入具備交易或 rollback 保證，使用最小可驗證批次，並在寫入前就向使用者聲明這個限制。
- **同名屬性可能互為鏡像。** `區域/區域號碼` 與 `ID和類別/區域號碼` 指向同一份底層資料（寫入前 105/105 相同、寫入後 42/42 相同）。寫任一個看似等價，但要用讀取確認鏡像關係，不要假設。

## 4. 穩定性與收手條件

連續大量請求之後，實測出現過 Tapir Add-On 停止回應：`discovery_list_active_archicads` 退化為空陣列 `[]`，而 port 仍在監聽、Archicad 行程仍存活。**空陣列與「Add-On 未載入」「沒有 Archicad 在跑」三種情況無法從回應區分。**

- 觀察到的觸發條件：短時間內大量列舉與關係查詢（本次累計約 7000 筆 GUID 列舉、1766 筆屬性定義、105 個 Zone 的關係查詢）。
- 觀察到的恢復方式：終止殘留的 `archicad-server` 行程、釋放對該 port 的既有連線，等待數分鐘後 discovery 自行恢復正常。
- **收手條件**：discovery 回空陣列時，**停止**並回報「無法確認 instance 狀態」，不要重試性地連續打；也不要把它記為某個指令的能力缺口。

## 5. 通用停止條件

出現下列任一情況，停止並回報能力缺口，不猜測 API：

- discovery 找不到涵蓋該 Domain 步驟的指令，且分頁已走完；
- 只有近似對照存在，而使用者意圖會改變結果；
- 指令 schema 缺少必要欄位或單位；
- 寫入結果無法以同一 backend read-back 驗證；
- 選定的 instance 變更或不可用；
- 識別碼來自 Revit，或來自另一個 Archicad port。
