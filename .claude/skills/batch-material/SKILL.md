---
name: batch-material
description: "批次材質修改（複製原材質模式）：為每個 Type 的原材質建立複本，只修改複本的 Appearance diffuse color，保留 Graphics 顏色與其他屬性。適用於渲染白模（Enscape/V-Ray 看到白色，平面圖切割圖案維持原材質）。也支援復原（assign_existing_material）。觸發條件：使用者提到材質、material、白色模型、white model、渲染、render、批次換材質、統一材質、復原材質、restore material。工具：get_types_by_category、batch_set_material、assign_existing_material。"
---

# 批次材質修改

## 核心設計：複製原材質模式

不是建立單一共用的白色材質，而是**為每個原材質獨立複製一份**：
- 原材質 `鋼 AISI 1015` → 複本 `鋼 AISI 1015_護眼白_MCP`
- 原材質 `混凝土 - 3000PSI` → 複本 `混凝土 - 3000PSI_護眼白_MCP`

**只修改複本的 Appearance Asset**（Enscape/V-Ray 渲染色）：
- Graphics 顏色保留原材質 → 平面圖切割填充仍顯示原材質圖案（鋼:斜線、混凝土:點狀）
- Revit Shaded/Realistic 3D 視圖也維持原材質
- **只有 Enscape/V-Ray 等外部渲染引擎才看到白色**

**複合構造（牆/樓板）只改 Layer 0**（最外層），其他層保留原材質。

## Prerequisites
- Revit 已開啟專案
- MCP Server 已連線

## Workflow

### 步驟 1：查詢類別與類型
使用 `get_types_by_category` 查詢使用者指定的類別：
- Walls（自動排除帷幕牆）
- StructuralFraming（梁）
- Columns（柱）
- Floors（樓板）

整理成表格展示每個 type 的名稱、族群、數量、目前材質。

### 步驟 2：使用者確認
將表格展示給使用者，詢問：
- 哪些 category 要修改？
- 哪些 type 要修改？（或全部、智慧模式排除未使用/玻璃/室外）
- 目標 Appearance 顏色？（預設白色 RGB 255,255,255）
- 複本 suffix 名稱？（預設 `White_MCP`）

**必須等使用者明確回覆後才進入步驟 3。**

### 步驟 3：執行修改
使用 `batch_set_material`：
```
batch_set_material({
    typeIds: [...],
    color: { r: 245, g: 245, b: 240 },
    materialName: "護眼白_MCP"    // 當作 suffix 用
})
```

### 步驟 4：確認結果
- 提示使用者到 **Enscape/V-Ray** 渲染檢查（這是唯一會變色的地方）
- Revit 平面圖、Shaded 3D 維持原外觀
- 告知 Ctrl+Z 可撤銷

### 復原步驟（Undo）
如果使用者想恢復原材質，使用 `assign_existing_material`：
```
assign_existing_material({
    typeIds: [...],
    materialName: "鋼 AISI 1015"    // 要恢復的既有材質名
})
```
復原時 `SetCompoundStructureAllLayers` 會把 CompoundStructure 全層都設回同一材質（不只是 Layer 0）。

## 注意事項

- **冪等保護**：對已套用 suffix 的 type 再跑一次會被跳過（不會建立 `xxx_White_MCP_White_MCP`）
- **原材質不受影響**：`Material.Duplicate` 建立新 Material，`UpdateAppearanceAsset` 會檢測 Appearance Asset 共用並自動複製
- **複合構造保留內層**：牆/樓板只改 Layer 0，隔熱層、結構層、內飾層的材質都維持
- **帷幕牆自動排除**：`get_types_by_category` 預設排除帷幕牆（有獨立流程 `/curtain-wall`）
- **Ctrl+Z 撤銷**：所有變更可在 Revit 內撤銷
