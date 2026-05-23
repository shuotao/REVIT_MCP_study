---
name: beam-penetration-rc
description: "RC 混凝土梁專屬穿孔檢核原則。定義分區、尺寸、間距與邊距規定。引用 beam-penetration-base。"
metadata:
  version: "1.1"
  updated: "2026-05-05"
  created: "2026-05-05"
  contributors: ["User", "Antigravity"]
  references: ["beam-penetration-base"]
---

# RC 混凝土梁穿孔規則

## 1. 幾何限制
*   **形狀**：僅允許**圓孔**。若為矩形孔，狀態設為 `FAIL`。
*   **長度誤差**：遵循 `beam-penetration-base` 之 $\pm 10 \text{ mm}$ 容許誤差規範。

## 2. 穿孔分區與尺寸 (大梁)
以「梁中心線與柱面之交點」為起點 ($d=0$)：
*   **禁開區 (Zone A)**：$d < 1.0 \cdot H$
    *   結果：`FAIL`。
*   **限制區 (Zone B)**：$1.0 \cdot H \le d < 1.5 \cdot H$
    *   直徑限制：$D \le 1/4 \cdot H$。
*   **許可區 (Zone C)**：$d \ge 1.5 \cdot H$
    *   直徑限制：$D \le 1/3 \cdot H$。

## 3. 穿孔分區與尺寸 (小梁)
以「梁端點面」為起點 ($d=0$)：
*   **位置要求**：$d \ge 1/2 \cdot H$。
*   **直徑限制**：$D \le 1/3 \cdot H$。

## 4. 淨間距與邊距 (Safety Clearance)
*   **上下淨距**：外緣至梁頂/梁底 $\ge 1/3 \cdot H$。
    *   **絕對底限**：大梁 $\ge 20 \text{ cm}$, 小梁 $\ge 15 \text{ cm}$。
*   **孔間淨距**：兩孔邊緣距離 $S_{edge} \ge (D_1 + D_2)$。
*   **接頭避讓**：距離小梁與大梁接合處（大梁側） $\ge 1/2 \cdot H$。
