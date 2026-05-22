# 元素圖形覆寫工具 - 使用範例

## 工具說明
已成功新增兩個工具用於改變平面圖中元素的填滿顏色：
- `override_element_graphics`: 覆寫元素圖形顯示
- `clear_element_override`: 清除元素圖形覆寫

---

## 使用範例

### 範例 1：將單一牆體標記為紅色

```javascript
// 步驟 1：取得當前視圖
const view = await get_active_view();

// 步驟 2：查詢牆體
const walls = await query_elements({
    category: "Walls",
    viewId: view.Id
});

// 步驟 3：將第一面牆標記為紅色（表示不合格）
await override_element_graphics({
    elementId: walls.Elements[0].ElementId,
    viewId: view.Id,
    surfaceFillColor: { r: 255, g: 0, b: 0 },  // 紅色
    transparency: 30                             // 30% 透明度
});
```

### 範例 2：根據參數值批次上色

```javascript
// 取得所有牆
const walls = await query_elements({ category: "Walls" });
const view = await get_active_view();

// 根據防火時效參數上色
for (const wall of walls.Elements) {
    const info = await get_element_info({ elementId: wall.ElementId });
    
    const fireRating = info.Parameters.find(p => p.Name === "防火時效");
    
    let color;
    if (fireRating?.Value === "2小時") {
        color = { r: 0, g: 255, b: 0 };  // 綠色 - 合格
    } else if (fireRating?.Value === "1小時") {
        color = { r: 255, g: 165, b: 0 };  // 橘色 - 警告
    } else {
        color = { r: 255, g: 0, b: 0 };  // 紅色 - 不合格
    }
    
    await override_element_graphics({
        elementId: wall.ElementId,
        viewId: view.Id,
        surfaceFillColor: color,
        transparency: 40
    });
}
```

### 範例 3：標記走廊寬度檢查結果

```javascript
// 檢查走廊寬度並上色標記
const rooms = await get_rooms_by_level({ level: "2F" });
const view = await get_active_view();

for (const room of rooms.Rooms) {
    if (room.Name.includes("走廊")) {
        // 假設已經計算出走廊寬度
        const width = parseFloat(room.Parameters.find(p => p.Name === "寬度")?.Value);
        
        let color;
        if (width >= 1200) {
            color = { r: 0, g: 255, b: 0 };   // 綠色 - 合格（≥1200mm）
        } else {
            color = { r: 255, g: 0, b: 0 };   // 紅色 - 不合格
        }
        
        await override_element_graphics({
            elementId: room.ElementId,
            viewId: view.Id,
            surfaceFillColor: color,
            transparency: 20,
            lineColor: color  // 邊線也用同樣顏色
        });
    }
}
```

### 範例 4：清除所有覆寫

```javascript
// 清除單個元素
await clear_element_override({
    elementId: 12345,
    viewId: view.Id
});

// 批次清除多個元素
const wallIds = [12345, 12346, 12347];
await clear_element_override({
    elementIds: wallIds,
    viewId: view.Id
});

// 清除當前視圖的所有牆體覆寫
const walls = await query_elements({ category: "Walls" });
const wallIds = walls.Elements.map(w => w.ElementId);
await clear_element_override({
    elementIds: wallIds
});
```

### 範例 5：分類顯示內外牆

```javascript
const walls = await query_elements({ category: "Walls" });
const view = await get_active_view();

for (const wall of walls.Elements) {
    const info = await get_element_info({ elementId: wall.ElementId });
    
    // 根據牆的 Function 參數判斷
    const wallFunction = info.Parameters.find(p => p.Name === "Function");
    
    let color;
    let transparency;
    
    if (wallFunction?.Value === "Exterior") {
        // 外牆：黃色，低透明度（重要）
        color = { r: 255, g: 255, b: 0 };
        transparency = 10;
    } else if (wallFunction?.Value === "Interior") {
        // 內牆：藍色，高透明度（次要）
        color = { r: 0, g: 150, b: 255 };
        transparency = 50;
    } else {
        // 未分類：紫色
        color = { r: 200, g: 0, b: 200 };
        transparency = 30;
    }
    
    await override_element_graphics({
        elementId: wall.ElementId,
        viewId: view.Id,
        surfaceFillColor: color,
        transparency: transparency
    });
}
```

---

## 顏色建議

### 建築檢討
- 🟢 綠色 `{r: 0, g: 255, b: 0}` - 合格
- 🟡 黃色 `{r: 255, g: 255, b: 0}` - 警告
- 🟠 橘色 `{r: 255, g: 165, b: 0}` - 需檢查
- 🔴 紅色 `{r: 255, g: 0, b: 0}` - 不合格
- 🟣 紫色 `{r: 200, g: 0, b: 200}` - 未分類

### 元素分類
- 🔵 藍色 `{r: 0, g: 150, b: 255}` - 內牆
- 🟡 黃色 `{r: 255, g: 255, b: 0}` - 外牆
- 🟢 綠色 `{r: 0, g: 255, b: 0}` - 共同壁
- 🟤 褐色 `{r: 165, g: 42, b: 42}` - 分戶牆

### 透明度建議
- `0-20`: 強調重點（如違規項目）
- `30-50`: 一般標記
- `60-80`: 次要資訊
- `90-100`: 僅顯示邊界

---

## 重要提醒

1. **視圖專用性**：覆寫只影響指定的視圖，不同視圖可以有不同的覆寫設定
2. **不影響實際屬性**：這只是視覺化覆寫，不會改變元素本身的屬性
3. **記得清除覆寫**：完成檢討後記得使用 `clear_element_override` 清除
4. **組合使用其他工具**：可以配合 `select_element`、`zoom_to_element` 等工具使用

---

## 部署狀態

✅ TypeScript 工具定義已新增  
✅ C# 實作已完成  
✅ 已編譯並部署到 Revit 2024  
✅ 可以開始使用！

請先：
1. 重新啟動 Revit 2024（載入新的 DLL）
2. 啟動 MCP 服務
3. 使用上述範例開始測試
