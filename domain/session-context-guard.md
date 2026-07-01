---
name: session-context-guard
description: "互動式會話中的上下文守衛協議，定義 L1-passive / L2-active / L3-full 三級環境感知機制，防止 AI 在使用者切換視圖、樓層或專案後產生錯誤操作。當使用者提到視圖、樓層、階段、連結模型、上下文、Context Guard 時觸發。"
metadata:
  version: "1.0"
  updated: "2026-03-10"
  created: "2026-03-10"
  contributors:
    - "Admin"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [視圖, 樓層, 階段, 門, 連結模型, 上下文, Context Guard]
---

# 互動式會話上下文守衛 (Session Context Guard)

本文件定義一套 AI 在長時間 Revit 互動會話中，維持環境感知的分級協議，避免在使用者切換視圖、樓層或專案後產生錯誤操作。

---

## 設計原則

1. **最小成本原則**：L1 層級不發送任何查詢，僅靠既有資訊判斷
2. **漸進式確認協議**：L2/L3 才需動態查詢，由觸發條件逐級升級
3. **零假設安全模式**：無法確定當前環境時採用 L1，不主動執行操作
4. **跨版本相容**：適用所有 Revit 版本（2022~2026）的上下文機制

---

## 三級上下文機制

### L1：被動感知 (Passive / 從既有資訊推斷)

**適用時機**：工具回傳結果包含元素資訊

**規則**：
- 從工具回傳結果中提取視圖相關資訊如 `ViewName`、`LevelName`、`ViewType` 等
- 建立隱式上下文記錄追蹤變化
- **不發送查詢**：不額外呼叫任何工具
- **降級條件**：偵測到異常時升級至 L2

**成本**：零（不額外呼叫工具）

---

### L2：主動確認 (Active / 用工具驗證上下文)

**適用時機**：L1 偵測到異常或使用者提及上下文變更

**規則**：
- 主動呼叫 `get_active_view` 取得當前視圖完整資訊
- 比對上一次已知狀態，產生差異報告
- 向使用者確認變更

**回報格式**：
```
偵測到上下文變更：目前視圖為 {ViewName} ({ViewType})，樓層 {LevelName}。是否繼續在此環境下操作？
```

**成本**：1 次 `get_active_view` 呼叫

---

### L3：完整環境報告 (Full / 全面環境掃描)

**適用時機**：L2 偵測到重大變更或漸進式確認失敗

**規則**：
- 呼叫 `get_active_view` 取得視圖資訊
- 確認 Port 8964 連線狀態是否正常
- 產生完整 Context Report 報告
- 向使用者確認後才繼續

**回報格式**：
```
+----------------------------------------+
|  Session Context Report                |
|                                        |
|  Port 8964:   LISTENING                |
|  目前視圖:    {ViewName}               |
|  樓層:        {LevelName}              |
|  視圖類型:    {ViewType}               |
|  上次視圖:    {PrevView} ({Status})    |
|                                        |
|  取得方式：額外呼叫工具確認            |
+----------------------------------------+
```

**成本**：1 次 `get_active_view` + 1 次 `netstat` 確認

---

## 升級觸發條件表

### L1 升級至 L2 的觸發條件

| 觸發條件 | 偵測方式 | 建議動作 |
|------|---------|---------|
| 視圖/樓層變更 | 回傳資訊中 `LevelName` 與上次紀錄不同 | 送出確認 L3 或 L4 |
| 操作失敗 | 送出查詢的結果與預期落差過大 | 重新呼叫確認上下文 |
| 批量操作前 | 送出查詢前已知將影響多個 Element ID 或類似情況 | 「即將對 57 個元素執行...」 |
| 長時間暫停 | 距上一次工具呼叫已間隔較長時間 | 送出確認再繼續執行 |

### L2 升級至 L3 的觸發條件

| 觸發條件 | 偵測方式 | 建議動作 |
|------|---------|---------|
| Port 異常/中斷 | 回傳呼叫逾時或失敗 | 送出完整報告，確認 Revit 是否仍在執行 |
| 專案檔案變更 | `get_active_view` 回傳的專案與上次記錄不同 | 可能切換了 .rvt 檔案 |
| 連續失敗累積 | 距上一次成功呼叫已超過多次失敗（如 10 次以上） | 送出完整報告檢查連線 |
| 送出查詢觸發版本差異 | 距上一次已知使用 2025 但回傳的資料格式不符 | 可能切換至 2025 版本 |
| 回傳呼叫異常 | 呼叫工具回傳 `Success: false` 或連續逾時 | 可能需要中斷操作並重新建立連線 |

---

## 自動降級規則

| 條件 | 動作 |
|------|------|
| 連續 3 次正常操作後 | L2 降至 L1（恢復被動感知模式） |
| L3 確認報告無異常 | L3 降至 L1（全面正常後回歸） |
| 送出查詢與預期一致後 | 直接維持 L1（被動追蹤即可） |

---

## 紀錄格式

每次上下文變更時，AI 應在內部追蹤以下欄位：

| 欄位 | 說明 |
|------|------|
| `timestamp` | 變更偵測時間 |
| `prev_view` | 上一次視圖名稱 |
| `curr_view` | 目前視圖名稱 |
| `guard_level` | 目前守衛等級 (L1/L2/L3) |
| `trigger` | 觸發升級/降級的原因 |

---

## Active State Re-Anchoring（2026-05-22 補，憲法級延伸）

L1/L2/L3 三級守衛偏「被動偵測」（L1）+「異常時主動」（L2/L3）。但實務上有更尖銳的場景——**使用者隨意切換視圖時，AI 沒有任何「異常」可偵測，只是在 stale snapshot 上繼續做 claim**。本節定義對應的雙向協議。

### AI 端強化規則

**任何引用 view-state / level-state / active-context 的 claim 之前，必須在「claim 時點」呼叫 `get_active_view` 重新確認**。不能依賴 session 較早的 read 結果。

| 場景 | 正確做法 | 反模式（5/22 dry-run 實證）|
|------|----------|----------------------------|
| 跨多輪對話後執行 view-anchored 操作（如 `override_element_graphics`、`create_section_view`）| **先 re-anchor**：`get_active_view` 確認當前視圖 → 顯式傳該 viewId | 用 session 前段抄下的 ViewId，但使用者已切過視圖 |
| 引用「當前樓層」做 level-anchored 查詢 | **先 re-anchor**：`get_active_view` 取 LevelName → 用此 LevelName | 假設 LevelName 跟 session 開頭一致 |
| 報告「我看到 X」的 claim | 確認 X 是當前 turn 的 tool response 提供 | 引用 session 前段的 tool response 當「現在的事實」 |

### 雙向 Context Sync 協議（人端責任）

Context 同步**不是單方面 AI 責任**，使用者也有對應責任：

| 操作 | 使用者責任 | AI 對應行為 |
|------|------------|-------------|
| 切換視圖 / 樓層 | (a) 主動告知；或 (b) 不告知但接受 AI 會 re-anchor | 收到下一個 prompt 時自動 re-anchor |
| 編輯模型（移牆、改參數）後接著查詢 | 主動說「我剛改了 X，再查」 | 重要查詢前主動問「自上次查詢後模型有編輯嗎？」|
| 切換到非 FloorPlan 視圖（3D / Section）後再要求 level-scoped 操作 | 意識到視圖類型可能跟操作預設衝突 | re-anchor 後若 ViewType 不對，主動 surface |
| 模型重新載入 / 切換 .rvt 檔 | **必須告知**（隱式偵測會晚一拍）| 收到「新檔載入」訊號後強制升 L3 全面確認 |

### Tool Call Data Honesty 的關係

Tool Call Data Honesty（CLAUDE.md MUST 級規則）規定「數據必須來自當前 turn 的 tool response」——這條跟 Active State Re-Anchoring 是**強化關係**：

- **Data Honesty 管「**數據從哪來**」**（不可 LM 先驗）
- **Active Re-Anchoring 管「**狀態何時刷新**」**（不可用過期 snapshot）

兩者交集場景：「AI 報告：『您在 1F 主平面，所以我幫你查 1FL 房間』」——若 1F 主平面是 5 輪對話前抄下的，這個 claim 同時違反兩條（snapshot 過期 + 數據不是當前 turn）。

### 違規範例（2026-05-22 dry-run）

使用者刻意在 session 中段切換視圖（1F → 6F → 2F → ...），且不告知 AI。AI 第一次假設視圖仍在 1F、回了一份基於 1F 的 claim。**真正的根因不是「視圖變了」，而是「AI 沒在 claim 時重新驗證」**。修正後：每個 level-anchored 操作前都呼叫 `get_active_view` re-anchor，從此再切視圖 AI 都能正確跟上。

### 5/23 demo 講者觀察點

讓使用者在 Step 1 後刻意切視圖（不告知）→ 觀察 AI 在 Step 2 時是否會 re-anchor。會的 AI 對應 Active Re-Anchoring 合規；不會的 AI 會繼續用 Step 0/1 抄下的 LevelName，產出跟使用者眼前畫面脫鉤的報告——**這就是 Bilateral Context Sync 失敗的活體範例**。
