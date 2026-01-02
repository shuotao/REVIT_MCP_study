# 走廊防火分析與標註策略 (Corridor Analysis Protocol)

> **最後更新**: 2026-01-02
> **目的**: 建立一套標準化的流程，用於自動偵測 Revit 中的走廊元素、分析淨寬是否符合法規並自動建立標註。

## 📋 核心邏輯
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

## ⚠️ 注意事項
- **座標系**: 標註的位置線必須位於元素內部的中心，否則標註可能無法顯示或對齊。
- **視圖相容性**: 標註必須建立在平面視圖 (FloorPlan) 中。
