---
name: room-boundary
description: "房間邊界計算：容積計算需要正確的邊界位置（外牆算外緣、內牆算中心、同戶內牆依規定處理），但 Revit 的 Room 預設使用 Finish Face。本文件定義三種邊界（Finish/Center/Core）的適用情境。當使用者提到房間邊界、room boundary、Finish Face、Center Line、Core Line、容積邊界時觸發。"
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
  tags: [房間邊界, room boundary, Finish Face, Center Line, Core Line, 容積邊界]
---

# 房間邊界計算

## 問題背景

容積計算需要正確的邊界位置：
- 外牆：算到外緣
- 內牆（兩戶之間）：算到牆中心
- 內牆（同戶內）：依規定處理

但 Revit 的 Room 預設是用牆內緣（Finish Face）計算。

## 邊界差異

| 邊界位置 | Revit 術語 | 適用情境 |
|---------|-----------|---------|
| 牆內緣 | Finish Face | 室內淨面積 |
| 牆中心 | Wall Center | 共用壁計算 |
| 牆外緣 | Core Face | 容積計算 |

## 解決方案

### 方案 A：使用 Area 元素
- 建立 Area Scheme（面積方案）
- 繪製 Area Boundary（面積邊界線）
- 手動設定邊界位置

### 方案 B：計算偏移量
- 取得 Room 面積
- 查詢邊界牆的厚度
- 計算偏移後的面積

## 工具設計

```
get_room_boundaries(room_id)
→ 回傳：
  - 邊界牆清單
  - 每面牆的類型（外牆/內牆）
  - 每面牆的厚度
  - 目前邊界位置（內緣/中心/外緣）
```
