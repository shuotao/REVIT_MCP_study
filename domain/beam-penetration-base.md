---
name: beam-penetration-base
description: "梁穿孔（套管）檢核的基礎協議。包含元素識別、樓層一致性檢查與輸出格式規範。所有穿梁子項 SOP (RC/SC/SRC) 均須引用此基礎。當使用者提到梁穿孔、套管穿梁、beam penetration、sleeve through beam 時觸發。"
metadata:
  version: "1.1"
  updated: "2026-05-05"
  created: "2026-05-05"
  contributors: ["SEven777-a", "Antigravity"]
  references: []  # TODO: 月小聚補建築技術規則具體條號（§68 鋼梁開孔類比 / RC 工程經驗條文）
  related: ["beam-penetration-rc", "beam-penetration-sc", "beam-penetration-src"]
  referenced_by: []  # TODO: 未來 structure-related skill 引用時補
  tags: [梁穿孔, 套管, 穿梁, 結構, beam penetration, sleeve, base, structural, RC, SC, SRC, 樓層匹配]
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
