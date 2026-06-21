# 元素顏色覆寫工具設計

## 工具名稱
`override_element_graphics`

## 功能說明
在指定視圖中覆寫元素的圖形顯示，包括填滿顏色、填滿圖樣、線條顏色等。適用於平面圖中標記不同狀態的牆體（如：合格/不合格、內牆/外牆等）。

---

## TypeScript 工具定義

```typescript
{
    name: "override_element_graphics",
    description: "在指定視圖中覆寫元素的圖形顯示（顏色、圖樣）。可用於標記牆體狀態或分類。",
    inputSchema: {
        type: "object",
        properties: {
            elementId: {
                type: "number",
                description: "要覆寫的元素 ID"
            },
            viewId: {
                type: "number",
                description: "視圖 ID（若不指定則使用當前視圖）"
            },
            surfaceFillColor: {
                type: "object",
                description: "表面填滿顏色 RGB (0-255)",
                properties: {
                    r: { type: "number", minimum: 0, maximum: 255 },
                    g: { type: "number", minimum: 0, maximum: 255 },
                    b: { type: "number", minimum: 0, maximum: 255 }
                }
            },
            surfacePatternId: {
                type: "number",
                description: "表面填充圖樣 ID（可選，-1 表示實心填滿）",
                default: -1
            },
            lineColor: {
                type: "object",
                description: "線條顏色 RGB（可選）",
                properties: {
                    r: { type: "number" },
                    g: { type: "number" },
                    b: { type: "number" }
                }
            },
            transparency: {
                type: "number",
                description: "透明度 (0-100)，0 為不透明",
                minimum: 0,
                maximum: 100,
                default: 0
            }
        },
        required: ["elementId"]
    }
}
```

---

## C# 實作 (CommandExecutor.cs)

```csharp
/// <summary>
/// 覆寫元素圖形顯示
/// </summary>
private object OverrideElementGraphics(JObject parameters)
{
    Document doc = _uiApp.ActiveUIDocument.Document;
    int elementId = parameters["elementId"].Value<int>();
    int? viewId = parameters["viewId"]?.Value<int>();
    
    // 取得視圖
    View view;
    if (viewId.HasValue)
    {
        view = doc.GetElement(new ElementId(viewId.Value)) as View;
        if (view == null)
            throw new Exception($"找不到視圖 ID: {viewId}");
    }
    else
    {
        view = _uiApp.ActiveUIDocument.ActiveView;
    }

    // 取得元素
    Element element = doc.GetElement(new ElementId(elementId));
    if (element == null)
        throw new Exception($"找不到元素 ID: {elementId}");

    using (Transaction trans = new Transaction(doc, "Override Element Graphics"))
    {
        trans.Start();

        // 建立覆寫設定
        OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();

        // 設定表面填滿顏色
        if (parameters["surfaceFillColor"] != null)
        {
            var colorObj = parameters["surfaceFillColor"];
            byte r = (byte)colorObj["r"].Value<int>();
            byte g = (byte)colorObj["g"].Value<int>();
            byte b = (byte)colorObj["b"].Value<int>();
            Color fillColor = new Color(r, g, b);
            overrideSettings.SetSurfaceForegroundPatternColor(fillColor);
        }

        // 設定填充圖樣
        int patternId = parameters["surfacePatternId"]?.Value<int>() ?? -1;
        if (patternId == -1)
        {
            // 使用實心填滿
            ElementId solidPatternId = GetSolidFillPatternId(doc);
            if (solidPatternId != null && solidPatternId != ElementId.InvalidElementId)
            {
                overrideSettings.SetSurfaceForegroundPatternId(solidPatternId);
                overrideSettings.SetSurfaceForegroundPatternVisible(true);
            }
        }
        else if (patternId > 0)
        {
            overrideSettings.SetSurfaceForegroundPatternId(new ElementId(patternId));
            overrideSettings.SetSurfaceForegroundPatternVisible(true);
        }

        // 設定線條顏色（可選）
        if (parameters["lineColor"] != null)
        {
            var lineColorObj = parameters["lineColor"];
            byte r = (byte)lineColorObj["r"].Value<int>();
            byte g = (byte)lineColorObj["g"].Value<int>();
            byte b = (byte)lineColorObj["b"].Value<int>();
            Color lineColor = new Color(r, g, b);
            overrideSettings.SetProjectionLineColor(lineColor);
        }

        // 設定透明度
        int transparency = parameters["transparency"]?.Value<int>() ?? 0;
        if (transparency > 0)
        {
            overrideSettings.SetSurfaceTransparency(transparency);
        }

        // 應用覆寫
        view.SetElementOverrides(new ElementId(elementId), overrideSettings);

        trans.Commit();

        return new
        {
            Success = true,
            ElementId = elementId,
            ViewId = view.Id.IntegerValue,
            ViewName = view.Name,
            Message = $"已成功覆寫元素 {elementId} 在視圖 '{view.Name}' 的圖形顯示"
        };
    }
}

/// <summary>
/// 取得實心填滿圖樣 ID
/// </summary>
private ElementId GetSolidFillPatternId(Document doc)
{
    // 嘗試找到名為 "Solid fill" 的圖樣
    FilteredElementCollector collector = new FilteredElementCollector(doc);
    var fillPatterns = collector
        .OfClass(typeof(FillPatternElement))
        .Cast<FillPatternElement>()
        .Where(fp => fp.GetFillPattern().IsSolidFill)
        .ToList();

    if (fillPatterns.Any())
    {
        return fillPatterns.First().Id;
    }

    return ElementId.InvalidElementId;
}
```

---

## 使用範例

### 範例 1：標記違規牆體為紅色

```javascript
// 先取得當前視圖
const view = await get_active_view();

// 查詢需要標記的牆
const walls = await query_elements({
    category: "Walls",
    viewId: view.Id
});

// 將第一面牆標記為紅色
await override_element_graphics({
    elementId: walls.Elements[0].ElementId,
    viewId: view.Id,
    surfaceFillColor: { r: 255, g: 0, b: 0 },    // 紅色
    transparency: 30                               // 30% 透明
});
```

### 範例 2：根據參數值改變顏色

```javascript
// 取得所有牆
const walls = await query_elements({ category: "Walls" });
const view = await get_active_view();

// 逐一檢查並上色
for (const wall of walls.Elements) {
    const info = await get_element_info({ elementId: wall.ElementId });
    
    // 假設有個參數叫 "防火時效"
    const fireRating = info.Parameters.find(p => p.Name === "防火時效");
    
    let color;
    if (fireRating && fireRating.Value === "2小時") {
        color = { r: 0, g: 255, b: 0 };  // 綠色 - 合格
    } else if (fireRating && fireRating.Value === "1小時") {
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

### 範例 3：批次覆寫工具（進階）

```typescript
// 可進一步設計批次工具
{
    name: "override_elements_by_filter",
    description: "批次覆寫符合條件的元素圖形",
    inputSchema: {
        type: "object",
        properties: {
            category: {
                type: "string",
                description: "元素類別（如 'Walls'）"
            },
            parameterName: {
                type: "string",
                description: "要檢查的參數名稱"
            },
            colorRules: {
                type: "array",
                description: "顏色規則陣列",
                items: {
                    type: "object",
                    properties: {
                        parameterValue: { type: "string" },
                        color: {
                            type: "object",
                            properties: {
                                r: { type: "number" },
                                g: { type: "number" },
                                b: { type: "number" }
                            }
                        }
                    }
                }
            }
        }
    }
}
```

---

## 注意事項

### 1. 視圖專用性
- **覆寫只影響指定的視圖**，不同視圖可以有不同的覆寫設定
- 如果要在所有平面圖中顯示，需要對每個視圖分別設定

### 2. 圖樣ID取得
- 實心填滿的 PatternId 在不同專案可能不同
- 建議作法：用 `GetSolidFillPatternId()` 動態查詢

### 3. 顏色系統
- Revit API 使用 RGB 0-255
- 透明度是 0-100（0 = 完全不透明，100 = 完全透明）

### 4. 重置覆寫
需要額外的工具來清除覆寫：

```csharp
/// <summary>
/// 清除元素圖形覆寫
/// </summary>
private object ClearElementOverride(JObject parameters)
{
    Document doc = _uiApp.ActiveUIDocument.Document;
    int elementId = parameters["elementId"].Value<int>();
    int? viewId = parameters["viewId"]?.Value<int>();
    
    View view = viewId.HasValue 
        ? doc.GetElement(new ElementId(viewId.Value)) as View
        : _uiApp.ActiveUIDocument.ActiveView;

    using (Transaction trans = new Transaction(doc, "Clear Override"))
    {
        trans.Start();
        view.SetElementOverrides(
            new ElementId(elementId), 
            new OverrideGraphicSettings()  // 空設定 = 重置
        );
        trans.Commit();
    }

    return new { Success = true };
}
```

---

## 快速實作清單

要在您的專案中實作這個功能，需要：

✅ **Step 1**: 在 `revit-tools.ts` 中註冊工具定義  
✅ **Step 2**: 在 `CommandExecutor.cs` 的 `ExecuteCommand` switch 中新增 case  
✅ **Step 3**: 實作 `OverrideElementGraphics` 方法  
✅ **Step 4**: 實作輔助方法 `GetSolidFillPatternId`  
✅ **Step 5**: 重新編譯並部署

