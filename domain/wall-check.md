---
name: wall-check
description: "牆壁檢查機制：偵測 Revit 牆壁的 Exterior/Interior 面方向是否正確，避免飾面錯位、容積計算邊界判斷錯誤。當使用者提到牆壁內外方向、wall orientation、wall check、Exterior Side、Interior Side 時觸發。"
metadata:
  version: "1.0"
  updated: "2025-12-14"
  created: "2025-12-14"
  contributors:
    - "shuotao"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - element-query
    - wall-orientation-check
  tags: [牆壁, wall, 內外方向, orientation, Exterior Side, Interior Side, wall check]
---

# 牆壁檢查機制

## 問題背景

建模者可能因不熟悉 Revit，導致牆壁的內外方向顛倒。這會影響：
1. 牆的 Exterior Side / Interior Side 標記錯誤
2. 牆飾面可能貼在錯誤的一側
3. 容積計算時邊界判斷錯誤

## 檢查流程

```
Step 1: classify_walls(level)
        → 區分內牆與外牆

Step 2: check_wall_orientation(wall_ids)
        → 檢查外牆內外側方向
        → 回傳：正確 / 可能錯誤 / 不確定

Step 3: highlight_walls_by_status(結果)
        → 視圖中顯示顏色標記
        → 綠色：方向正確
        → 紅色：方向可能錯誤
        → 黃色：需要人工確認
        → 藍色：內牆
```

## 判斷邏輯

### 區分內外牆
- 檢查牆的 Function 參數
- 檢查牆是否接觸建築外皮
- 檢查牆的類型名稱

### 判斷外牆方向
- 射線檢測：從 Exterior 側發射射線，檢查是否碰到其他元素
- 房間檢測：Exterior 側不應該有房間
- Bounding Box：外牆的 Exterior 側應該朝向建築外部
