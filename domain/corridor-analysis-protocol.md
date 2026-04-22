---
name: corridor-analysis-protocol
description: "走廊防火分析與標註標準流程 (SOP)：自動偵測 Revit 走廊元素、計算淨寬、比對法規並建立標註。當使用者提到走廊、廊道、寬度分析、逃生通道時觸發。"
metadata:
  version: "1.0"
  updated: "2026-03-10"
  created: "2026-01-02"
  contributors:
    - "Admin"
    - "Gemini"
    - "shuotao"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - fire-safety-check
  tags: [走廊, corridor, 廊道, 寬度分析, 防火分析, 比對法規]
---

# 走廊防火分析與標註策略 (Corridor Analysis Protocol)

> **目的**: 建立一套標準化的流程，用於自動偵測 Revit 中的走廊元素、分析淨寬是否符合法規並自動建立標註。

##  核心邏輯
1. **識別 (Identification)**:
   - 篩選房間名稱包含: `走廊`, `Corridor`, `廊道`, `通道`, `廊下` (日文), `廊`。
2. **定位 (Localization)**:
   - 取得房間的 `BoundingBox`。
   - 根據 BoundingBox 的最大、最小座標計算估計長寬。
3. **分析 (Analysis)**:
   - 檢查寬度是否符合建築技術規則（1.2m 與 1.6m 閥值）。
4. **標註 (Annotation)**:
   - 使用 `create_dimension` 在房間 BoundingBox 的中心線上建立標註。
   - 必須指定與房間一致的樓層 (`LevelId`) 並選擇正確的視圖。

## 🛠️ 成功工具組合範例
```javascript
// 取得當前樓層走廊
const rooms = await call('get_rooms_by_level', { levelId: currentLevelId });
const corridor = rooms.find(r => r.name.includes('廊下'));

// 根據 BoundingBox 中心點建立尺寸標註線
const centerStart = { x: min.x, y: (min.y + max.y) / 2, z: 0 };
const centerEnd = { x: max.x, y: (min.y + max.y) / 2, z: 0 };

await call('create_dimension', {
    elements: [corridor.id],
    type: 'Linear',
    viewId: activeViewId,
    line: { start: centerStart, end: centerEnd }
});
```

##  注意事項
- **座標系**: 標註的位置線必須位於元素內部的中心,否則標註可能無法顯示或對齊。
- **視圖相容性**: 標註必須建立在平面視圖 (FloorPlan) 中。

## 🧮 寬度計算方法

### 方法 1: Boundary Segments (推薦)
- **適用**: 任意角度的走廊 (包含斜向走廊)
- **原理**: 計算最長的兩條平行線段之間的垂直距離
- **精確度**: ±10mm
- **演算法**:
  1. 取得房間的所有邊界線段
  2. 找出角度差 < 5° 的平行線段對
  3. 選擇平均長度最長的平行線段對 (走廊兩側牆)
  4. 計算兩條平行線段之間的垂直距離

### 方法 2: BoundingBox (降級)
- **適用**: 僅限垂直/水平走廊
- **原理**: 取 X/Y 方向較小值
- **精確度**: 斜向走廊會嚴重錯誤 (可能誤差 10 倍以上)
- **限制**: 對於斜向走廊,BoundingBox 是軸對齊矩形,包含大量空白區域

### 自動降級邏輯
1. 優先嘗試 Boundary Segments
2. 如果無法取得 (房間未封閉、未放置等) → 使用 BoundingBox
3. 結果中標註使用的方法:
   - `method: "boundary_accurate"` - 精確計算
   - `method: "bbox_estimate"` - 估算 (不精確)

```javascript
import { calculateCorridorWidth } from '../src/utils/corridor-geometry.js';

const result = calculateCorridorWidth(
    roomData.BoundarySegments, 
    roomData.BoundingBox
);

console.log(`寬度: ${result.width.toFixed(1)} mm`);
console.log(`方法: ${result.method}`);
```

## 🔀 多區段走廊分析 (Segment-First Algorithm)

### 適用場景
- L 型走廊 / T 型走廊 / 十字型走廊
- 任何有分支或轉折的走廊
- 單一房間 ID 涵蓋多個幾何區段的情況

### 核心演算法: 線段優先分析 (Segment-First Analysis)

為了適應各種複雜的走廊形狀,我們採用「線段優先」的分析策略,而非傳統的圖形切割。

#### 步驟 1: 線段預處理與篩選
對房間邊界的所有線段進行遍歷:
1. **平形線偵測**: 為每條線段尋找與其平行的所有其他線段。
2. **寬度計算**: 計算到最近平行線段的垂直距離 (Width)。
3. **共線排除 (Collinear Filtering)**:
   - 若平行線段距離極近 (例如 < 100mm),視為同一側牆壁的連續分段或開口,予以排除。
   - 確保找到的是「面對面」的牆壁。
4. **長寬比過濾 (Aspect Ratio Filtering)**:
   - 計算 `Ratio = 線段長度 / 寬度`
   - 若 `Ratio < 1.0`, 視為走廊的短邊 (末端或轉角), 標記為無效。
   - 若 `Ratio >= 1.0`, 視為走廊的長邊 (主要邊界), 保留分析。

#### 步驟 2: 區段配對與建立
將篩選後的有效長邊進行配對:
1. 找出互相平行的有效長邊對。
2. 檢查兩線段在投影方向上是否有重疊 (Projection Overlap)。
3. 將配對成功的線段定義為一個「走廊區段 (Corridor Segment)」。

#### 步驟 3: 合規性檢查
對每個區段進行與法規的對比分析:
1. **寬度檢查**: 是否滿足最小淨寬 (例如 1200mm)。
2. **瓶頸標示**: 標記不合格的區段位置。

### 優點
- **無需參數**: 自動適應任何尺度的走廊,無需人工設定閾值。
- **抗干擾**: 能有效處理牆壁分段、柱子突出、門窗開口等幾何干擾。
- **幾何直觀**: 直接基於走廊「細長形」的本質特徵進行分析。

### 使用範例 (SDK)

```javascript
import { analyzeMultiSegmentCorridor } from '../src/utils/corridor-geometry.js';

const result = analyzeMultiSegmentCorridor(
    roomData.BoundarySegments,
    1200  // 最小寬度 1.2m
);

console.log(`區段數量: ${result.totalSegments}`);
console.log(`最小寬度: ${result.minWidth} mm`);
console.log(`整體結果: ${result.allPass ? 'PASS' : 'FAIL'}`);

// 顯示不合格區段
if (!result.allPass) {
    result.failedSegments.forEach(seg => {
        console.log(`[FAIL] 區段 ${seg.segmentIndex + 1}`);
        console.log(`  - 寬度: ${seg.width} mm`);
        console.log(`  - 長度: ${seg.length} mm`);
        console.log(`  - 中心: (${seg.centerPoint.x}, ${seg.centerPoint.y})`);
    });
}
```
