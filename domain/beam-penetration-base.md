---
name: beam-penetration-base
description: "梁穿孔（套管）檢核的基礎協議。包含元素識別、樓層一致性檢查與輸出格式規範。所有穿梁子項 SOP (RC/SC/SRC) 均須引用此基礎。"
metadata:
  version: "1.1"
  updated: "2026-05-05"
  created: "2026-05-05"
  contributors: ["User", "Antigravity"]
  related: ["beam-penetration-rc", "beam-penetration-sc", "beam-penetration-src", "sleeve-classification-protocol"]
---

# 梁穿孔檢核基礎協議 (Base Protocol)

## 1. 元素識別規範

### 1.1 梁類型判斷 (Beam Classification)
*   **RC 梁**：矩形斷面，材質為混凝土。
*   **SC 梁**：H 型或 I 型鋼梁。
*   **SRC 梁**：矩形混凝土包覆 H 型鋼。
*   **特殊標記**：梁上柱區域應標記為 `SPECIAL_CHECK`。

### 1.2 梁地位判斷 (Hierarchy)
*   **大梁 (Major Beam)**：任一端點與「結構柱」相連。
*   **小梁 (Minor Beam)**：兩端點皆未與柱相連。

### 1.3 開孔元件識別 (Identification)
*   **貫穿判定**：套管長度 $L$ 與梁寬 $B$ 的關係：
    *   **完全貫穿**：$|L - B| \le 10 \text{ mm}$。此範圍內視為正確貫穿。
    *   **未貫穿**：$L < B - 10 \text{ mm}$。視為一般碰撞（如閥類、儀表），不納入穿梁原則檢核。
    *   **異常長度**：$L > B + 10 \text{ mm}$。仍視為穿梁，但應於檢核結果標註「套管過長」之警告。
*   **品類限定**：優先搜尋 `Pipe Accessory` (管附件) 與 `Generic Model` (一般模型)。

### 1.4 排除規範 (Exclusion)
*   **穿牆套管排除**：若套管與「牆 (Walls)」品類之元素物理相交，則該套管應判定為穿牆套管，自動從「穿梁檢核」名單中排除，除非該套管同時明確貫穿結構梁且使用者要求重複檢核。

## 2. 數據一致性檢核 (Data Consistency)

*   **樓層匹配原則**：梁的「參考樓層 (Reference Level)」必須與套管的「樓層 (Level)」參數完全一致。
*   **建模錯誤回饋**：
    *   若套管與梁在空間中物理相交，但樓層參數不符，狀態設為 `FAIL`。
    *   摘要應註明：「建模錯誤：套管樓層 ({SleeveLevel}) 與 梁樓層 ({BeamLevel}) 不符，請修正參數以利後續明細表與標註正確性。」

## 3. 檢核輸出規範 (Output Standard)

當檢核工具執行後，必須回傳以下 JSON 結構或格式化文字：
1.  **狀態**：`PASS` (合格) / `FAIL` (違規) / `WARNING` (警告) / `SPECIAL_CHECK` (人工複核)。
2.  **失敗原因**：具體指出違反了哪一項條文與數值對比（如：Zone A 禁開區, d=45cm < 60cm）。
3.  **建議動作**：提供具體的修正方向（如：水平移動至 X 座標...）。

## 4. 標準作業流程 (Standard Workflow)

執行穿梁檢核時，AI 應串聯多個技能以達成完整自動化：

### Phase 1：環境偵察與對齊 (Skill: `element-query`, `detect-clashes`)
1. **確認連結模型**：使用 `get_linked_models` 識別 MEP 與 CSA 模型。
2. **參數對齊與探測**：
    *   使用 `get_category_fields` 確認梁的幾何參數（如：`h`, `b`）。
    *   **套管對齊**：依據 `sleeve-classification-protocol` 探測本專案代表「長度」的參數名稱（如：`開口長度`）。
3. **初步碰撞掃描與過濾**：
    *   執行 `scan_penetrated_beams_in_view` 識別穿透行為。
    *   **關鍵篩選**：比對套管是否與牆體相交，過濾掉「穿牆套管」以確保檢核對象純淨。

### Phase 2：深度規則檢核 (Tool: `analyze_beam_penetration`)
1. **逐一分析**：針對 Phase 1 產出的清單，調用 `analyze_beam_penetration`。
2. **規則引用**：依據梁材質（RC/SC/SRC）引用對應的子項 SOP（如 `beam-penetration-rc.md`）進行 Zone A/B/C 判定。

### Phase 3：視覺化報告與標註 (Skill: `element-coloring`, `auto-dimension`)
1. **結果標記**：執行 `visualize_penetration` 或 `override_element_graphics` 對結果進行紅/綠標記。
2. **自動尺寸標註**：使用 `auto-dimension` 技能標註關鍵距離（如孔心至柱面距離），作為人工複核之依據。
3. **異常匯報**：產出統計摘要，列出所有 `FAIL` 項目的原因與修正建議。
