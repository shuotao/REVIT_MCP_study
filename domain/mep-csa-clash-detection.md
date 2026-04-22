---
name: mep-csa-clash-detection
description: "MEP vs CSA 碰撞偵測流程 SOP：以 CSA 為主模型連結 MEP，使用 Curve-to-Solid 策略偵測管線與結構碰撞、視覺化結果並匯出報告。當使用者提到碰撞、干涉、clash、管線穿牆、套管、penetration 時觸發。"
metadata:
  version: "1.0"
  updated: "2026-04-22"
  created: "2026-03-13"
  contributors: []  # TODO: 月小聚補（檔案尚未 commit）
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - detect-clashes
  tags: [碰撞, 干涉, clash, MEP, 管線穿牆, 套管, 穿越, penetration]
---

# MEP vs CSA 碰撞偵測流程 SOP

## 架構說明

```
主模型 (CSA): 牆 / 樑 / 柱 / 板 (當前開啟的 .rvt)
連結模型 (MEP): 管 / 風管 / 電纜架 (Link 進主模型)

碰撞策略: Curve-to-Solid 降維
  MEP 管線 → 抽取中心線 (1D Curve)
  CSA 結構 → 保持實體 (3D Solid)
  碰撞 = Curve 穿過 Solid → 取得穿透線段
```

---

## Phase 1: 環境偵察

### Step 1.1: 確認連結模型

```
Tool: get_linked_models
目的: 列出所有連結模型，找到 MEP 連結的 LinkInstanceId
預期: 至少回傳 1 個連結模型，記錄其 LinkInstanceId
```

### Step 1.2: 偵察 MEP 品類與參數

```
Tool: query_linked_elements
參數:
  linkInstanceId: <MEP 連結 ID>
  category: "Pipes"
  maxCount: 5
目的: 確認能從連結模型讀取管線，檢驗回傳結構
```

### Step 1.3: 識別系統類型分佈

```
Tool: query_linked_elements
參數:
  linkInstanceId: <MEP 連結 ID>
  category: "Pipes"
  returnFields: ["System Type", "Size", "Outside Diameter"]
  maxCount: 50
目的: 了解 MEP 模型中有哪些系統和管徑範圍
```

### Step 1.4: 確認 CSA 結構品類

```
Tool: get_active_schema
目的: 確認當前模型中的 Walls / Floors / StructuralFraming / StructuralColumns 數量
```

---

## Phase 2: 範圍界定 (互動決策)

> AI 向使用者確認以下條件：

| 決策項目 | 說明 | 範例 |
|:---------|:-----|:-----|
| MEP 品類 | 要檢測哪些管線類型 | Pipes + Ducts + CableTrays |
| MEP 系統 | 要檢測哪些機電系統 | 消防, 冰水, 排水 |
| 管徑門檻 | 最小管徑過濾 | ≥ 100mm |
| CSA 對象 | 要碰撞哪些結構類型 | 牆 + 板 + 樑 + 柱 |
| 樓層範圍 | 檢測範圍 | 全部 / 1F-5F |

---

## Phase 3: 碰撞運算

### Step 3.1: 執行碰撞偵測

```
Tool: detect_clashes
參數:
  mepSource:
    linkInstanceId: <MEP 連結 ID>
    categories: ["Pipes", "Ducts", "CableTrays"]
    filters:
      - field: "System Type"
        operator: "contains"
        value: "消防"
      - field: "Size"
        operator: "greater_than"
        value: "100"
  csaSource:
    categories: ["Walls", "Floors", "StructuralFraming", "StructuralColumns"]
  options:
    useCoarseFilter: true
    maxResults: 1000

回傳: 碰撞清單，每筆含:
  - MEP: ElementId, 系統, 管徑, 類型
  - CSA: ElementId, 類別, 類型, 厚度
  - 交集: 入口座標, 出口座標, 貫穿長度, 方向向量, 截面積, 佔用體積
  - 統計: 依系統/依結構類型 分組統計
```

---

## Phase 4: 結果分析 (AI 彙整)

AI 分析 detect_clashes 的回傳結果，產出:

1. **統計摘要表**: 總碰撞數、涉及管線數、涉及結構數
2. **系統熱點分析**: 哪個系統碰撞最多
3. **結構風險分析**: 柱穿越 > 樑穿越 > 板穿越 > 牆穿越 (嚴重度排序)
4. **深度異常警告**: 貫穿深度 > 500mm 的碰撞需特別標示

---

## Phase 5: 視覺化與報告

### Step 5.1: 視覺化上色

```
Tool: colorize_clashes
參數:
  clashData: <Phase 3 的完整回傳結果>
  colorScheme: "by_csa_category"
    # 柱=紅(255,80,80)  樑=橘(255,165,0)
    # 板=黃(255,220,0)  牆=藍(70,130,255)

替代配色:
  "by_system"    → 依 MEP 系統著色
  "by_severity"  → 依嚴重程度(紅/橘/藍)
```

### Step 5.2: 匯出報表

```
Tool: export_clash_report
參數:
  clashData: <Phase 3 的完整回傳結果>
  format: "both"
  reportTitle: "消防系統碰撞偵測報告"

CSV 欄位:
  序號, 系統名稱, 管徑, 截面積, 結構類別, 結構類型, 厚度,
  貫穿長度, 佔用體積, 向量XYZ, 入口XYZ, 出口XYZ,
  MEP_ID, CSA_ID
```

### Step 5.3: 逐筆導覽 (互動)

```
Tool: select_element → 選取碰撞元素
Tool: zoom_to_element → 跳轉到碰撞位置
```

---

## 注意事項

1. **效能**: 大型模型 (500+ 管 × 200+ 牆) 建議開啟 BoundingBox 粗篩
2. **座標**: 連結模型的座標會自動套用 Transform 校正，BEP 規定必須用「原點到原點」
3. **排除範圍**: 此流程不包含管件 (Fittings) 碰撞、MEP 內部碰撞
4. **後續流程**: 碰撞報表可用於產出套管 (Sleeve) 放樣清單
