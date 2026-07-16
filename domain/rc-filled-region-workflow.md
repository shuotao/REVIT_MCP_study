---
name: rc-filled-region-workflow
description: "RC 結構剖切面填滿區域自動化 SOP：依圖紙視埠批次收集牆、柱、樓版、樑剖切面，建立深灰色 FilledRegion，並以幾何指紋支援智慧更新。"
metadata:
  version: "1.0"
  updated: "2026-04-13"
  created: "2026-04-13"
  contributors:
    - "Jacky820507"
  references: []
  related: ["finish-legend-creation.md", "tool-capability-boundary.md"]
  referenced_by: []
  tags: [Revit, RC, FilledRegion, Section, Fingerprint, 智慧更新]
---

# RC 結構填滿貼紙 自動化工作流程

## 📌 使用場景
剖面圖與大樣詳圖中，需要將所有 RC 結構（牆、柱、樓版、樑）的剖切面自動繪製為深灰色填滿區域，並在模型修正後能自動偵測變更、精準更新，不需手動重繪。

---

## 🔧 系統架構

```
JS 腳本 (batch_rc_filled.cjs)
    │── 傳送 sheetNumbers (圖紙編號)
    ▼
C# CommandExecutor (BatchCreateRCFilledRegions)
    │── 解析圖紙 → 視埠 → 視圖
    │── 對每個視圖調用 Internal_SmartCreateRCFilledRegionInView
    ▼
智慧更新引擎
    ├── RC_CollectCutFaces() → 收集剖切面幾何
    ├── RC_ComputeFingerprint() → 計算指紋
    ├── RC_GetExistingAutoRegions() → 查找 RC_AUTO 標記的舊物件
    └── 比對 → 跳過 / 刪舊建新
```

---

## 📋 標準執行步驟

### 步驟 1：確認填滿樣式
確認 Revit 專案中存在名為 **`深灰色`** 的 FilledRegionType（或修改腳本中的 `filledRegionTypeName`）。

### 步驟 2：設定目標圖紙
編輯 `batch_rc_filled.cjs`，在 `targetSheetNumbers` 中填入需要處理的圖紙編號：
```javascript
const targetSheetNumbers = ['ARA-D04002', 'ARA-D04003'];
```

### 步驟 3：執行腳本
```bash
node MCP-Server/scratch/batch_rc_filled.cjs
```

### 步驟 4：讀取報告
```
⏭️ [視圖名稱]: 無變更，跳過         ← 模型未改動
🔄 [視圖名稱]: 刪除 X → 建立 Y      ← 幾何有變動，已更新
✨ [視圖名稱]: 刪除 0 → 建立 N      ← 全新建立
```

---

## ⚠️ 首次使用注意（舊版貼紙清理）

若視圖中已存在**舊版產出（無 `RC_AUTO` 標記）**的深灰色貼紙，需先手動清理：
1. 在 Revit 中開啟對應剖面視圖。
2. 選取舊的深灰色 FilledRegion（可用「選取所有同類型」）。
3. 確認 Properties → Comments 為空白，刪除之。
4. 再執行腳本，即可產生帶有 `RC_AUTO` 標記的新貼紙。

---

## 🔑 關鍵決策記錄

| 問題 | 解決方案 |
|---|---|
| 如何識別 RC 元素？ | 類型名稱 (toLowerCase) 包含 `rc`、`混凝土`、`concrete` |
| 如何確保只取剖切到的面？ | `PlanarFace.FaceNormal.DotProduct(viewDir) > 0.98` + 距離 < 0.1ft |
| 如何保護手動繪製的貼紙？ | `Comments` 標記為 `RC_AUTO`，系統只清理有標記的物件 |
| 如何偵測幾何變更？ | 重心 + 面積 Shoelace 指紋，精度 0.3mm |
| 為何腳本卡住不回應？ | JS 的 RequestId 用 number，C# 回傳 string，型別不同導致 Promise 永遠不 resolve |

---

## 📂 相關檔案
- **C# 核心**：`MCP/Core/Commands/CommandExecutor.FillPatterns.cs`
- **入口腳本**：`MCP-Server/scratch/batch_rc_filled.cjs`
- **Skill**：`.agent/skills/revit-rc-filled-region/SKILL.md`
