---
name: room-numbering
description: "依 domain 排序規則對 Revit 房間批次自動編號：以樓層前綴分組（B<F<R）、Y 座標由上至下分排（分群容差 3000mm）、排內 X 座標由左至右排序，各樓層自 X01 起編。先產出 dry-run 預覽表供使用者確認，確認後才批次寫入房間編號並抽樣驗證。當大量房間需要重新編號、編號順序混亂、或新放置的房間尚未編號時使用。觸發條件：使用者提到房間編號、自動編號、房間重編、批次編號、編號亂掉、重新排號、room numbering、renumber rooms、auto room numbering。"
---

# 房間自動編號 (Room Numbering)

執行前請先讀取 `domain/room-numbering-workflow.md` 了解排序規則與技術參數。
分群容差、排序規則、起編規則以 domain 為準。

> **Guard rail 提醒**：domain 文件中的 `number_rooms.js` 外部腳本是 legacy 執行路徑。
> 依 CLAUDE.md「Do Not Bypass MCP」規則，本 skill 以 MCP 工具實作同一套排序規則，
> 不呼叫外部腳本、不手寫 WebSocket JSON。

## Workflow

### 步驟 0：前置健檢（re-anchor）

1. `get_all_levels` 確認樓層命名含可識別前綴（如 `B1F`、`1F`、`R1F`）。
   前綴無法識別時，先向使用者確認樓層對應，不要猜。
2. `get_active_view` 錨定目前視圖（供後續抽查 zoom 使用）。

### 步驟 1：盤點房間

1. 逐樓層 `get_rooms_by_level` 取得房間清單（名稱、現有編號）。
2. 若回傳缺少座標，對每間房間 `get_element_info` 取 BoundingBox 中心點。
3. 例外處理：未圍合房間（面積 0 或無幾何）列入例外清單，不參與編號。

### 步驟 2：排序與分群（依 domain 技術參數）

1. 樓層排序：前綴 B < F < R。
2. 同一樓層內以 Y 座標降冪分排（由上至下），差距 3000mm 內視為同一排。
3. 排內以 X 座標升冪（由左至右）。
4. 每個樓層由 `X01` 起編（X = 樓層前綴）。

### 步驟 3：Dry-run 預覽（必經，不可跳過）

1. 輸出預覽表：`樓層 | 房間名稱 | 舊編號 → 新編號`。
2. 一併列出例外清單（未圍合、取不到座標）。
3. 停下來等使用者確認排序與前綴正確，才進入寫入階段。

### 步驟 4：正式寫入

1. 逐間 `modify_element_parameter` 寫入編號參數（「編號」或「Number」，先偵測實際參數名）。
2. 若寫入時出現重複編號衝突：改用兩段式寫入——先全部寫入 `TMP-` 前綴暫時編號，
   再第二輪寫入正式編號，避免交換編號時互撞。
3. 寫入過程逐筆記錄成功／失敗，失敗清單保留到報告。

### 步驟 5：驗證與回饋

1. 隨機抽 3–5 間 `get_room_info` 比對寫入結果。
2. 重點抽查轉角處與不規則隔間的編號順序（domain 第三階段要求），
   可用 `zoom_to_element` 定位、`override_element_graphics` 暫時上色輔助目視。
3. 目視確認完成後 `clear_element_override` 清除暫時上色。
4. 回報：成功筆數、例外清單、失敗清單。

## 工具

| 工具名稱 | 用途 |
|---------|------|
| `get_all_levels` | 確認樓層命名前綴 |
| `get_active_view` | 錨定目前視圖 |
| `get_rooms_by_level` | 逐樓層盤點房間 |
| `get_element_info` | 補抓房間 BoundingBox 中心座標 |
| `modify_element_parameter` | 寫入房間編號參數 |
| `get_room_info` | 寫入後抽樣驗證 |
| `zoom_to_element` | 抽查定位 |
| `override_element_graphics` | 抽查時暫時上色 |
| `clear_element_override` | 清除暫時上色 |

## Reference

詳見 `domain/room-numbering-workflow.md`（分群容差 3000mm、排序規則、X01 起編皆以該檔為準）。

設計模式：Sequential Workflow（盤點 → 排序 → dry-run → 寫入 → 驗證）。
