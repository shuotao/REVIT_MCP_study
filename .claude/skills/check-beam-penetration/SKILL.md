---
name: check-beam-penetration
description: 在 Revit 視圖中執行 RC 穿梁套管合規檢核：掃描被套管穿過的結構梁、萃取精確幾何（距柱邊淨距、梁深、孔徑）、依大小梁法規逐項判定 PASS/FAIL，並在圖面上以顏色與標籤視覺化結果。觸發條件：穿梁, 套管, 穿梁檢核, 套管檢核, RC 梁開孔, 穿梁套管, 禁開區, beam penetration, sleeve, penetration check。
---

# 穿梁套管檢核 (Beam Penetration Check)

依據 `domain/beam-penetration-base.md` 與 `domain/beam-penetration-rc.md` 執行 RC 梁穿孔（套管）合規檢核。
本 Skill 收編自鈺傑 (SEven777-a) 的穿梁 rc 開發線，並改寫為正規的 MCP 工具編排（不再以直連 WebSocket 的腳本為主路徑）。

## 方法權威

- 幾何與法規方法一律以 domain 檔為準：`domain/beam-penetration-base.md`（基礎協議、套管身分、樓層一致性、輸出格式）、`domain/beam-penetration-rc.md`（禁開區/限制區/一般區、垂直淨距、相鄰套管淨距）。
- SC/SRC 梁請改讀 `domain/beam-penetration-sc.md`／`domain/beam-penetration-src.md`（草稿階段，部分數值待江老師確認）。
- 演算法背景（JoinGeometry 端面消失、實體頂點極值投影、法向量過濾、1D 降維排隊標註）見 `domain/beam-penetration-algorithm.md`。

## 工具

- `get_active_view` — 重新錨定當前視圖。
- `scan_penetrated_beams_in_view` — 掃描視圖中所有被套管穿過的結構梁，回傳梁 ID、連結模型 ID 與套管數量。
- `analyze_beam_penetration` — 針對單一梁萃取精確幾何（距柱心/柱邊淨距、梁深、孔徑等），支援動態參數名 Fallback 與連結模型。
- `get_src_beam_mapping` — 偵測 RC 梁與鋼梁重疊區（SRC 映射），標記須優先套用鋼梁原則的區域。
- `visualize_penetration` — 依檢核結果將套管標色（合格/不合格）並放置標籤文字。
- `clear_previous_annotations` — 清除上一輪穿梁輔助標註（比對 "BeamPenetration_Helper" 註解）。

## 前置

1. 確認 Revit 已開啟、MCP 服務已啟用、`localhost:8964` 可達。
2. 若套管在連結（MEP/CSA）模型中，先確認連結模型已載入且可讀。

## 工作流程

1. **重新錨定視圖**
   - 呼叫 `get_active_view` 確認當前作用視圖（勿沿用前一輪的 view ID）。

2. **掃描穿梁清單**
   - 呼叫 `scan_penetrated_beams_in_view` 取得被套管穿過的梁清單（含各梁的套管數與所屬連結模型 ID）。

3. **逐梁萃取幾何**
   - 對每一支梁呼叫 `analyze_beam_penetration`（帶入 beamId，必要時帶 linkInstanceId 與參數名清單）。
   - 讀回：套管外孔邊緣到柱邊/正交梁物理邊緣的水平淨距 `d`、梁深 `H`、孔徑 `D`、上下邊到梁頂/梁底淨距、相鄰套管中心距等欄位。
   - 若回傳缺少法規判定所需欄位，停止並補齊參數或回報分析未定義，不得臆測數值。

4. **依 domain 判定（不靠模型常識）**
   - 依 `domain/beam-penetration-rc.md`：
     - 大梁禁開區 `d < 1.0·H`；限制區 `1.0H ≤ d < 1.5H` → `D ≤ H/4`；一般區 `d ≥ 1.5H` → `D ≤ H/3`。
     - 小梁禁開區 `d < 0.5·H`；一般區 `d ≥ 0.5H` → `D ≤ H/3`。
     - 大小梁接頭避讓：套管至相交小梁物理邊緣 `≥ 0.5·H_minor`。
     - 垂直淨距：`d_top`、`d_bottom ≥ max(H/3, H_limit)`（大梁 200mm、小梁 150mm；梁頂增打厚度須扣除）。
     - 相鄰套管淨距：`S_net ≥ D1 + D2`。
     - 圓孔限定、長度誤差 `±10mm`、穿牆/穿板套管排除，均依 base 協議。
   - SRC 區域：先以 `get_src_beam_mapping` 找出 RC×鋼梁重疊，該區改套 SC/SRC 原則（草稿階段以 `SPECIAL_CHECK` 標記待人工複核）。
   - 每筆輸出狀態：`PASS` / `FAIL` / `WARNING` / `SPECIAL_CHECK`，並附具體違反條文與數值對比。

5. **視覺化與回報**
   - 呼叫 `visualize_penetration`，傳入各套管的 `SleeveId`、`IsOk`、`Message`、`Position`，於圖面標色與標籤。
   - 以簡潔表格回報 PASS/FAIL 數量與逐項 FAIL 原因，並引用所用 domain 檔（例：`Per domain/beam-penetration-rc.md §2.1 …`）。
   - 需清除上一輪輔助標註時，呼叫 `clear_previous_annotations`。

## Notes（保留鈺傑的工作流知識）

- **穿牆排除**：以 BoundingBox 與長寬比對自動剔除「長度短於梁寬（誤差 > 10mm）」或實質為穿牆/穿板的假套管（見 base §1.3–1.4）。
- **實體極值投影法**：正交梁捨棄 `W/2` 推算，改擷取正交梁實體頂點投影至大梁中心線取極值，還原真實接合面（連結模型同樣適用）。
- **法向量過濾**：以 `dot > 0.8` 排除平行牆接合干擾，精準捕捉大梁端部截斷面。
- **降維排隊標註**：將 3D 端點/套管節點降維為 1D 陣列，依中心距離排序後由「左節點右緣」畫至「右節點左緣」，規避方向誤判。

## 開發期備援（非主路徑）

- `MCP-Server/scripts/checkBeamPenetration.js` 為 rc 遺留的 dev-only 腳手架，直連 raw WebSocket 發送 `advanced_analyze`／`visualize_penetration`，繞過 MCP tool 定義與 dispatcher，違反「Do Not Bypass MCP」守則。僅供離線除錯，正式檢核請一律走上述 MCP 工具編排；此腳本在併入 main 前需改造或移除。
