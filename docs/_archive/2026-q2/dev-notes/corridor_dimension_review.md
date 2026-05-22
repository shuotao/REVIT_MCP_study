# 走廊尺寸標註檢討報告

## 執行日期
2025年12月15日

## 問題回顧

### 第一次標註的問題
❌ **錯誤做法**：直接使用 BoundingBox 座標
```javascript
startY: 13675  // room.BoundingBox.MinY
endY: 15150    // room.BoundingBox.MaxY
結果寬度: 1475mm
```

**問題點**：
1. BoundingBox 是房間的外包框，不是牆體內表面
2. 無法確定這個尺寸是否包含牆體厚度
3. 不符合建築法規的測量標準（應測量淨寬）

## 核心問題分析

### 1. 調用位階錯誤
**錯誤的優先級**：
```
get_room_info (取得 BoundingBox) 
  → 直接用於標註 ❌
```

**正確的優先級**：
```
get_room_info (取得中心點)
  → query_walls_by_location (取得實際牆體)
  → 用 Wall Face 座標標註 ✓
```

### 2. 前端工具調用問題
**問題**：`query_walls_by_location` 工具無法正常調用

**可能原因**：
1. ✅ TypeScript 工具定義存在 (`src/tools/revit-tools.ts` line 564)
2. ✅ C# 後端實作存在 (`CommandExecutor.cs` line 1365)
3. ✅ TypeScript 已編譯 (`npx tsc` 成功)
4. ⚠️ **MCP Server 未重啟** - 仍在執行舊版本程式碼

**解決方案**：
- 需要終止現有的 Node process
- 重新啟動 `node build/index.js`
- 或使用 `npm run dev` 重新啟動

### 3. 替代方案的限制

嘗試使用 `query_elements` 查詢牆體時，發現：
```javascript
{
  "ElementId": 56453900,
  "Name": "外壁_60",
  "Category": "牆",
  "LevelName": "2FL"  // ✓ 有樓層
  // ❌ 沒有座標資訊
}
```

**問題**：`query_elements` 的 C# 實作沒有返回座標資訊，只返回基本屬性。

## 正確的實施步驟

### Step 1: 取得視圖
```javascript
const view = await get_active_view();
// 結果: ViewId = 52627252 (担当者A_作業用_2階)
```

### Step 2: 取得走廊中心點
```javascript
const room = await get_room_info({ roomId: 52842719 });
// 結果: 
//   CenterX: 16394.8
//   CenterY: 14334.22
//   BoundingBox: { MinY: 13675, MaxY: 15150 } // 僅供參考
```

### Step 3: 查詢實際牆體 ⚠️
```javascript
const walls = await query_walls_by_location({
  x: 16394.8,
  y: 14334.22,
  searchRadius: 3000,
  level: '2FL'
});
// 期望結果應包含:
// - Wall Face1, Face2 座標
// - Wall LocationLine 座標  
// - Wall Thickness
// - Distance to center
```

**目前狀態**：此工具因 MCP Server 未重啟而無法調用

### Step 4: 建立尺寸標註
```javascript
// 使用牆體內表面座標
await create_dimension({
  viewId: 52627252,
  startX: centerX,
  startY: wall1.Face1.Y,  // 實際牆面
  endX: centerX,
  endY: wall2.Face1.Y,    // 實際牆面
  offset: 1200
});
```

## 當前標註結果

### 已標註的尺寸
- **測量值**: 1475mm
- **測量位置**: X=16394.8, Y 軸 13675 → 15150
- **資料來源**: ❌ BoundingBox（不精確）
- **尺寸線 ID**: 57162391（已被刪除或不存在）

### 法規符合性評估
根據 1475mm 的測量值：
- ✅ 單側房間走廊: ≥1200mm → **符合** (1475 > 1200)
- ❌ 雙側房間走廊: ≥1600mm → **不符合** (1475 < 1600)

⚠️ **重要提醒**: 此評估基於 BoundingBox 數據，實際淨寬可能不同！

## 改善建議

### 立即行動
1. **重啟 MCP Server**
   ```bash
   # 終止現有 Node process
   # 然後執行：
   cd MCP-Server
   node build/index.js
   ```

2. **重新執行正確流程**
   ```bash
   node correct_corridor_dimension.js
   ```

### 長期改善
1. **增強 query_elements 工具**
   - 在 C# 實作中加入座標資訊
   - 返回牆體的起點、終點座標
   
2. **開發 hot-reload 機制**
   - 避免每次修改都需要手動重啟
   
3. **增加資料驗證**
   - 標註前先驗證資料來源
   - 顯示 BoundingBox vs Wall Face 的差異

## 技術債務清單

### 高優先級
- [ ] 修正 MCP Server 需要重啟的問題
- [ ] 確保 `query_walls_by_location` 可正常調用
- [ ] 使用實際牆體座標重新標註

### 中優先級  
- [ ] 強化 `query_elements` 返回座標資訊
- [ ] 增加標註前的資料驗證機制
- [ ] 自動化法規符合性檢查

### 低優先級
- [ ] 開發視覺化工具顯示牆體位置
- [ ] 批次處理多個走廊的標註
- [ ] 生成法規檢討報告文件

## 結論

**目前狀況**：
- ✅ 已完成尺寸標註
- ⚠️ 標註使用的資料來源不夠精確（BoundingBox）
- ❌ 正確的工具流程因技術問題無法執行

**下一步**：
1. 重啟 MCP Server 載入最新程式碼
2. 使用 `query_walls_by_location` 取得精確牆體座標
3. 重新標註走廊淨寬和結構寬度
4. 驗證法規符合性

**學到的教訓**：
- BoundingBox 應該是最後考慮的選項，不是優先選擇
- 工具的調用位階直接影響結果的準確性
- 需要建立更好的程式碼熱更新機制
