---
name: room-height-limit
description: "Revit Room 上限高度（Upper Limit / Limit Offset）SOP：四個高度參數（Base Level / Base Offset / Upper Limit / Limit Offset）語意、房間高度計算公式、mm↔ft（304.8）換算、批次設定房高的常見誤區。觸發：房間高度、房高、room height、upper limit、limit offset、批次房高、統一房高、ceiling height。"
metadata:
  version: "1.0"
  updated: "2026-04-21"
  created: "2026-04-21"
  contributors:
    - "lesleyliuke"
  references: []  # TODO: 月小聚補法規建議值出處
  related: []
  referenced_by:
    - batch-room-height
  tags: [房間, room, 高度, height, upper-limit, limit-offset, 房高, 批次]
---

# 房間上限高度（Upper Limit / Limit Offset）

## 觸發時機
使用者提到：房間高度、房高、room height、upper limit、limit offset、batch room height、批次房高、統一房高、ceiling height、room top。

## Revit Room 高度相關參數

Revit 的 Room 是體積元素，其上下邊界由以下四個參數決定：

| 參數 | BuiltInParameter | 型別 | 單位 | 說明 |
|------|-----------------|------|------|------|
| **Base Level** | `ROOM_LEVEL_ID` (隱含) | ElementId | — | Room 的放置樓層（= `Room.LevelId`，讀取專用） |
| **Base Offset** | `ROOM_LOWER_OFFSET` | Double | ft | Room 底部相對 Base Level 的偏移（通常 0） |
| **Upper Limit** | `ROOM_UPPER_LEVEL` | ElementId | — | Room 頂部參考的樓層 |
| **Limit Offset** | `ROOM_UPPER_OFFSET` | Double | ft | Room 頂部相對 Upper Limit 的偏移 |

> 內部單位為英尺；與 mm 的換算採用 `304.8` 常數。

### 房間高度計算公式

```
RoomHeight = (UpperLevel.Elevation - BaseLevel.Elevation) + UpperOffset - BaseOffset
```

若 Upper Limit = Base Level，則：
```
RoomHeight = UpperOffset - BaseOffset ≈ UpperOffset (mm)
```

**→ 直觀用法**：把 Upper Limit 設成 Base Level、Limit Offset = 目標房高 mm，就能「一個參數控制房高」。

## 為什麼改 Room 上限 ≠ 改樓層

- 改的是 Room 實例參數，**不動 Level 的 Elevation**
- 不影響樓板、牆、梁、柱的任何幾何
- 只影響 Room 的三維邊界體積

### 連帶影響

| 項目 | 影響 |
|------|------|
| **Volume**（體積） | 直接變動 |
| **排煙有效帶判定** | 天花板下 80cm 的計算會跟著變（見 `smoke-exhaust-review.md`） |
| **Room 表面積** | 部分工具的牆/天花板面積統計可能改變 |
| **Area**（平面面積） | 不變（Area 只看平面邊界） |
| **樓板/天花板實體** | 完全不變 |

## 法規建議高度（台灣建技規）

| 用途 | 最小淨高 | 來源 |
|------|---------|------|
| 居室 | 2.1 m | 建技規 §36 |
| 浴室/廚房 | 2.0 m | 建技規 §36 |
| 樓梯平台/走廊 | 2.1 m | 建技規 §33 |
| 停車位 | 2.1 m | 建技規 §60-1 |

> **提醒**：這是淨高最低值；實際 Room 的 Limit Offset 需考慮天花板厚度與梁底深度，通常設定為結構樓高（例 3.0~3.6 m），再由天花板元素壓低可見淨高。

## 常見誤區

1. **把 Limit Offset 設成 0**：Room 高度變成 `UpperLevel - BaseLevel`，若 Upper Level 也是 Base Level，Room 會崩潰（高度 0，體積 0）
2. **負值 Offset**：Revit 會接受但產生下凹/上翻的異常 Room
3. **Upper Limit 指到低於 Base Level 的樓層**：Room 高度為負值，後續所有面積/體積都會錯
4. **套用到未封閉 Room（Area = 0）**：Revit 接受寫入但這些 Room 本就不參與統計；工具會先過濾

## 批次修改流程（由 `/batch-room-height` Skill 編排）

1. `get_all_levels` → 確認可選樓層範圍
2. `get_rooms_by_level` → 盤點 Room（Name / Number / Area）
3. 依 Name 或 Department 分組展示
4. **使用者確認**：每組目標 heightMm、是否指定 Upper Level、是否限定樓層
5. `batch_set_room_height` → 單一 Transaction 寫入 `ROOM_UPPER_LEVEL` + `ROOM_UPPER_OFFSET`
6. 結果回報 + 提示 Ctrl+Z 整批還原

## Group 內 Room 的處理策略

建築 BIM 常把「單元模組」（住宅房型、辦公間模組、停車場模組）做成 Model Group 以便複製到多個樓層。這類 Room 屬於 Group 成員，**從 Group 外直接寫參數會觸發警告**：
> "A group has been changed outside group edit mode. The change is being allowed because there is only one instance of the type."

多 instance 時 Revit 會拒絕或產生不一致。

### 正確做法：EditGroup 模式

```csharp
// 找出所有目標 Room 分桶到各自的 GroupType
// 每個 GroupType 挑一個代表 instance
foreach (var (typeId, representative) in groupTypesToEdit)
{
    doc.EditGroup(representative);
    try {
        foreach (var room in roomsInThisTypeInstance)
            SetRoomHeight(room, ...);
    }
    finally { doc.EndEditGroup(); }
}
```

**關鍵觀念**：
- 只需編輯**一個** instance 的 Room，同 GroupType 的其他 instance 會自動同步
- `room.GroupId == ElementId.InvalidElementId` 代表 Room 不在任何 Group → 可直接改
- 若一次要改多個 GroupType，外層包 `Transaction` 可維持單次 Ctrl+Z

### 保底：WarningSwallower

即使走 EditGroup，Area=0 Room 或其他邊界狀況仍可能觸發警告。Transaction 上註冊：
```csharp
var opts = trans.GetFailureHandlingOptions();
opts.SetFailuresPreprocessor(new WarningSwallower());
trans.SetFailureHandlingOptions(opts);
```
讓所有 Warning 自動吞掉（Error 仍會讓 Transaction 回滾）。

### Area = 0 Room（未封閉）

Room 的幾何邊界未封閉時 `Room.Area == 0`，但其 `ROOM_UPPER_LEVEL` / `ROOM_UPPER_OFFSET` 參數仍有值、仍需批次統一。**不要用 `Area > 0` 過濾**，否則會漏掉這類 Room。

## 相關檔案
- Skill：`.claude/skills/batch-room-height/SKILL.md`
- MCP Tool：`batch_set_room_height`（定義於 `MCP-Server/src/tools/room-tools.ts`）
- C# 實作：`MCP/Core/Commands/CommandExecutor.RoomModification.cs`
- 警告處理器：`MCP/Core/WarningSwallower.cs`（實作 `IFailuresPreprocessor`）
- 參數讀取範例：`MCP/Core/Commands/CommandExecutor.SmokeExhaust.cs`（排煙檢討讀 Upper Level/Offset 計算天花板高度）
- 經驗記錄：`domain/lessons.md` [L-013]
