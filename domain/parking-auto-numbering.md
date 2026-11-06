---
name: parking-auto-numbering
description: "停車格自動編號 SOP：自動化對 Revit 停車格（汽車、機車、大客車）進行分類排序與編號，寫入「備註」參數，取代人工手動輸入的繁瑣流程。當使用者提到停車自動編號、parking numbering、停車備註、車位編碼、renumber_parking_spaces 時觸發。要在圖面上放編號文字而不是寫參數的，看 sequence-numbering.md。"
metadata:
  version: "2.0"
  updated: "2026-10-23"
  created: "2026-04-02"
  contributors:
    - "unknown"
  references: []  # 跳 4 是圖面慣例不是法規；車位數量與尺寸的法源在 parking-space-review.md / parking-clearance-check.md
  related:
    - parking-space-review.md
    - parking-clearance-check.md
    - sequence-numbering.md
    - room-numbering-workflow.md
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [Revit, Automation, Parking, MCP, 停車自動編號, parking numbering, renumber_parking_spaces]
---

# 停車格自動編號標準作業程序 (Auto Numbering SOP)

## 0. 2026-09-07 修訂：本文件曾經指向一個不存在的腳本

v1.0 的作業流程寫的是一支 Node 編號腳本（帶 `--dry-run` 等旗標的命令列）。**那個腳本從來沒有進過版控**（`git log --all -- "*number_parking*"` 在任何分支都是空的）。它存在於某個貢獻者的機器上，SOP 進了 repo，程式沒有。

任何照 v1.0 操作的人都會卡在第一步。這次修訂把方法落地成真正的 MCP 工具 `renumber_parking_spaces`，本節保留下來當作紀錄：**一份寫得出執行細節的 SOP，不等於那個東西存在**。

對應工具：`renumber_parking_spaces`
實作：`MCP/Core/Commands/CommandExecutor.Numbering.cs`

## 1. 目的

自動化地對 Revit 停車格進行分類排序與編號，寫入例證參數（預設「備註」），取代人工手動輸入，確保數據一致性。

## 2. 適用對象

- 汽車停車格 (car)
- 機車停車格 (motorcycle)
- 大客車停車格 (bus)

分類依**族群名稱＋類型名稱**的關鍵字比對（不分大小寫），預設：

| 類別 | 預設關鍵字 | 參數 |
|---|---|---|
| motorcycle | 機車、motor、scooter | `motorcycleKeywords` |
| bus | 大客車、巴士、bus、coach | `busKeywords` |
| car | 以上皆不符者 | — |

**car 是 fallback**，所以命名不規則的車位會被歸成汽車位。dry-run 時務必核對 `CategoryCounts` 與你自己數的數量。

## 3. 前置準備

- Revit 專案已開啟，MCP 服務已啟動並連線（`localhost:8964`）。
- 停車格是 `OST_Parking` 類別的元件，且在目標樓層。
- 停車格具備要寫入的例證參數（預設「備註」），且該參數是**文字型別**、非唯讀。

## 4. 作業流程

1. **Dry-run（預設值就是 dry-run）**：直接呼叫 `renumber_parking_spaces`，`dryRun` 預設 `true`，不會寫入任何東西。
2. **核對三件事**：
   - `CategoryCounts` 的各類數量與你的認知相符（**這一步是防「比對了 0 個項目」的關卡**）
   - `StartPoints` 中每一類的 `StartElementId` 與 `StartSource`（「使用者指定」還是「自動判定」）合理
   - **抽查相鄰車位是否連續**，特別是使用者截圖或指出的問題區域；不要只看前 10 筆
3. **正式執行**：確認無誤後帶 `dryRun: false` 再呼叫一次。

## 5. 排序規則

1. 分類（car / motorcycle / bus）→ 各類別**獨立**排序與起號
2. Y 座標由上到下分排，容差 `yToleranceMm`
3. 同排內 X 座標由左到右

排序用的是 `CommandExecutor.RoomRenumber.cs` 的 `OrderRoomsTopDownLeftRight` —— 與房間重編號、圖面順序編號同一份實作。分排錨點取該排第一個元件的 Y，不是跟前一筆比較，避免容差鏈式漂移。

**分群容差預設 1500 mm**，適用標準停車格寬度，確保同一排車位被正確分到同一組。

### 為何不提供順時鐘繞行模式

停車場多排或多島配置時，以全場中心點計算的順時鐘排序會在同一排中間切斷序列，造成相鄰車位出現尾號接頭號。一般平面車位編碼要的就是「同一排相鄰車位連續」，也就是 Y 分排 + X 排序。真的需要沿車道繞行的路徑編號時，目前的做法是用 `elementIds` 自己指定順序，交給 `create_sequence_numbers`（`order: given`）。

## 6. 起點控制

- **使用者指定起點**：傳入 `startElementId`。該元件所屬類別的排序序列會**旋轉**到它開頭（不是截斷），其餘車位相對順序不變，它取得該類別的起始編號。
- **自動判定起點**：未指定時取排序後第一個元件，`StartPoints[].StartSource` 會標明「自動判定」。正式寫入前需確認此起點合理。
- **起點找不到就報錯**：指定的 `startElementId` 若不在任何處理中的類別裡（不是停車格、不在目標樓層、或被 `only` 過濾掉），工具會丟出例外而**不是靜默沿用預設起點** —— 靜默沿用會讓整批編號從錯的地方開始，而且看起來一切正常。

## 7. 編號格式

- 起始編號：`carStart` / `motorcycleStart` / `busStart`，預設皆為 1。
- `prefix`：例如 `A` 產生 `A1`、`A2`。
- `skipFour`：跳過任何**含數字 4** 的號碼（4、14、24、40–49…），不只個位數。台灣圖面慣例，非法規要求。

## 7.5 排除既有註記（2026-09-09）

停車格的「備註」欄常常已經有東西 —— 實測某案 B7FL 的 295 個車位中有 14 個寫著「變更」。
直接編號會把那些註記蓋掉，而且蓋掉之後沒有任何痕跡。兩個參數處理這件事：

| 參數 | 依據 | 適用 |
|---|---|---|
| `excludeValues` | 目標參數的**現值** | 保護註記，例如 `["變更"]`。下次再跑一樣有效，不必維護會過期的 ElementId 清單 |
| `excludeElementIds` | ElementId | 一次性手動排除 |

兩條規則命中任一即排除，**id 規則優先**（回傳的 `Reason` 會指名是哪一條）。

比對是 **Ordinal 精確比對**：不 trim、不忽略大小寫。「變更 」（尾隨空白）與「變更」是不同的值。

**現值為空的車位永遠不會被 `excludeValues` 掃到**，即使清單裡放了空字串。沒有這個守衛的話，
還沒編過號的車位（`OldValue` 為 null，也就是絕大多數）會被整批排除，而結果看起來仍然是綠的。

### excludeMode：被排除的車位要不要佔號

```
現況         1  2  3 ... 27 [28=變更] 29 ...
skip（預設） 1  2  3 ... 27  (不編號)  28 ...   號碼補上來，總數變少
reserve      1  2  3 ... 27  (不編號)  29 ...   留空號，其餘車位號碼不位移
```

- `skip`：被排除者完全不進排序。適合「這格根本不該算在這批編號裡」。
- `reserve`：仍進排序、仍佔一個號碼，只是不寫入。適合「這格是實體車位，只是先不動它」——
  圖面上號碼與實體位置的對應不會整批位移。

回傳的 `Excluded` 陣列列出每一個被排除的車位與理由，`Spaces[].Reserved` 標明哪些是佔號未寫入，
`WritableCount` 是實際會被寫入的數量（與 `Count` 不同，reserve 模式下兩者會差）。

## 8. 寫入保證

- 單一 Transaction，任一格寫入失敗**整批回滾**。
- `Reserved` 的車位在寫入迴圈直接跳過，原值原封不動。
- 寫入前逐格檢查參數存在、非唯讀、型別為文字，三者任一不符即中止整批。
- `Parameter.Set` 回傳 `false`（值被 Revit 拒絕，例如受群組或公式約束）時視為失敗並回滾 —— 不檢查回傳值會 commit 出一批靜默沒改到的車位，全綠但實際沒動。
- Transaction 經由 `TransactionHelper.Begin` 建立，已註冊 `SilentFailuresPreprocessor`，會自動吞掉「已在群組編輯模式之外變更群組。變更之所以被允許，是因為此類型只有一個實體。」這類 Warning 對話框。

## 9. 常見問題與處理

| 症狀 | 原因 | 處理 |
|---|---|---|
| 某類別數量是 0 | 關鍵字沒對到，全被歸成 car | 調整 `motorcycleKeywords` / `busKeywords` |
| 座標提取失敗、車位進 `Skipped` | 元件無 `LocationPoint` 也無 BoundingBox | 確認元件有有效實體幾何 |
| 報「沒有名為 '備註' 的參數」 | 族群未帶該例證參數 | 改 `parameterName`，或在族群中新增 |
| 報參數不是文字型別 | 參數被設成整數／數值 | 改用文字型參數 |
| 同排編號不連續 | `yToleranceMm` 太小 | 調大後重跑 dry-run |
| 未指定 levelName 就報錯 | 作用中視圖沒有關聯樓層 | 明確指定 `levelName` |
| 改完 DLL 但行為沒變 | 未重新編譯部署或未重啟 Revit | `Release.R23` 重建、部署後重啟 Revit |
