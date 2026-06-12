---
name: smoke-detector-check
description: "消防偵煙探測器設置法規檢討：出風口距離、壁樑距、天花板貼附、區格涵蓋、探測面積五項規定。法源：消防法施行細則 附表五（偵煙式探測器設置標準）。"
metadata:
  version: "1.0"
  created: "2026-06-11"
  updated: "2026-06-11"
  tags: [消防, 偵煙探測器, MEP, 法規檢討, 天花板, 區格]
  related:
    - smoke-exhaust-review.md
    - mep-csa-clash-detection.md
  referenced_by:
    - ".claude/skills/smoke-detector-check/SKILL.md"
---

# 消防偵煙探測器設置法規檢討

## 適用情境

本 SOP 適用於：天花板設有偵煙型探測器（差動式、光電式等）的樓層平面。
重點場合：設有冷風機（FCU）出風口的機房、辦公、旅館等空間。

## 法規依據

- 消防法施行細則 附表五：探測器設置規定
- 建築技術規則 §127（天花板高度限制）

---

## 五項必要條件

### 條件 1：距出風口 ≥ 1.5 m（最高優先）

| 項目 | 說明 |
|------|------|
| 觸發元件 | 空調出風口（FCU、AHU、冷風機出風口 Family） |
| 量測方式 | 探測器中心 → 出風口中心，水平距離 |
| 規範值 | ≥ 1,500 mm |
| 違規處理 | 標記為 FAIL，說明「距出風口不足 1.5m」 |

> **注意**：出風口 Family 名稱可能含「FCU」「冷風機」「AHU」「送風口」「出風」等關鍵字；需由使用者確認或傳入關鍵字清單。

---

### 條件 2：距牆壁或樑 ≥ 600 mm

| 項目 | 說明 |
|------|------|
| 觸發元件 | 牆（Walls）、結構樑（StructuralFraming） |
| 量測方式 | 探測器中心 → 牆面或樑側面，水平距離 |
| 規範值 | ≥ 600 mm |
| 違規處理 | 標記為 FAIL，說明「距牆/樑不足 600mm」 |

---

### 條件 3：探測器下端距天花板 ≤ 600 mm（貼近天花板）

| 項目 | 說明 |
|------|------|
| 量測方式 | 天花板完成面高度 − 探測器底部 Z 座標 |
| 規範值 | ≤ 600 mm（探測器必須貼近天花板，不可懸掛過低） |
| 違規處理 | 標記為 FAIL，說明「探測器距天花板過遠（>600mm）」 |

> 天花板高度來源優先順序：Room 的「上限高度」參數 → Ceiling Element Z → Level + 預設層高推算。

---

### 條件 4：每個探測區格內至少一個探測器

| 項目 | 說明 |
|------|------|
| 區格定義 | 被高度 ≥ 600 mm 的樑分隔的天花板格 |
| 判斷方式 | 以樑投影線在平面上劃分區格，每格統計探測器數量 |
| 規範值 | 每格 ≥ 1 個探測器 |
| 違規處理 | 標記該區格為「缺少探測器」 |

> 實作簡化：當完整樑區格劃分複雜時，可改為掃描「任何探測器周圍 6m 內是否有另一個探測器或牆樑邊界」。

---

### 條件 5：探測器負責面積 ≤ 有效探測範圍

| 天花板高度 | 每個探測器最大面積 |
|-----------|------------------|
| < 4 m     | 150 ㎡           |
| ≥ 4 m     | 75 ㎡            |

| 項目 | 說明 |
|------|------|
| 量測方式 | 空間總面積 ÷ 探測器數量（同一空間/防火區劃內） |
| 違規處理 | 標記為 FAIL，說明「平均負責面積超過規範」 |

---

## 資料萃取欄位（C# → JS）

```jsonc
{
  "Detectors": [
    {
      "DetectorId": 12345,
      "FamilyName": "光電式偵煙探測器",
      "X": 5000.0,      // mm, 世界座標
      "Y": 3000.0,
      "Z": 3200.0,      // 探測器底部 Z（mm）
      "RoomId": 999,
      "RoomName": "辦公室",
      "CeilingZ": 3600.0,  // 天花板完成面 Z（mm）
      "DistToNearestWall": 850.0,   // mm
      "DistToNearestBeam": 1200.0,  // mm
      "DistToNearestAirOutlet": 2100.0  // mm；若無出風口則 -1
    }
  ],
  "Rooms": [
    {
      "RoomId": 999,
      "RoomName": "辦公室",
      "Area": 180.0,     // ㎡
      "CeilingHeight": 3.6,  // m
      "DetectorCount": 2,
      "BeamZones": 2     // 被 ≥600mm 樑分隔的區格數
    }
  ]
}
```

---

## 法規判定流程（JS）

```text
for each detector:
  1. distToAirOutlet < 1500 → FAIL「距出風口不足」
  2. distToNearestWall < 600 OR distToNearestBeam < 600 → FAIL「距牆/樑不足」
  3. (ceilingZ - detectorBottomZ) > 600 → FAIL「距天花板過遠」

for each room:
  4. detectorCount < beamZones → FAIL「區格缺少探測器」
  5. avgArea = roomArea / detectorCount
     maxArea = ceilingHeight < 4.0 ? 150 : 75
     avgArea > maxArea → FAIL「探測面積超標」
```

---

## 視覺化規則

| 狀態 | 顏色 | 說明 |
|------|------|------|
| PASS | 綠色（0,200,0） | 全部通過 |
| FAIL | 紅色（255,0,0）  | 任一條件違規 |
| WARN | 橙色（255,165,0）| 出風口缺資料或天花板高度推算 |

---

## 輸出報告格式

Markdown 表格，每列一個探測器，欄位：

`探測器 ID | 房間 | 距出風口 | 距牆/樑 | 距天花板 | 負責面積 | 狀態 | 違規原因`
