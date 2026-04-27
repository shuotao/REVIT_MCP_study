# GEP 視圖樣版整併計畫

**專案**: GEP_ARCH_Template_R24_v4
**日期**: 2026-01-09
**狀態**: 待執行

---

## 📋 整併原則

### 1. 詳細等級
- **統一使用 Fine**（所有 GEP_ 系列）

### 2. 視覺樣式策略
| 樣版系列 | 視覺樣式 | 用途 |
|----------|----------|------|
| GEP_Drawing- (出圖) | HLR | 傳統線架構出圖 |
| GEP_Drawing- Presentation | ShadingWithEdges | 簡報用著色圖 |
| GEP_Modeling- | ShadingWithEdges | 建模預覽 |
| GEP_Review- | HLR | 審查檢討 |

### 3. Views - Working 篩選器
| 樣版系列 | 套用 | 行為 |
|----------|------|------|
| GEP_Modeling- | ✅ 套用 | 顯示工作中元素 |
| GEP_Drawing- | ✅ 套用（反向） | 隱藏工作中元素 |
| GEP_Review- | ❌ 不套用 | 顯示全部 |

### 4. 梁位視圖策略
- **刪除 GEP-Beam Plan**（180 個隱藏類別）
- **改用篩選器策略**優化 GEP_Review- Plan_Lower Beam

---

## 🔄 整併對照表

### 保留的 GEP_ 新版樣版

| 編號 | 樣版名稱 | 視圖類型 | 詳細等級 | 視覺樣式 |
|------|----------|----------|----------|----------|
| 1 | GEP_Drawing- 1F Plan | FloorPlan | Fine | HLR |
| 2 | GEP_Drawing- 1F Site Plan | FloorPlan | Fine | HLR |
| 3 | GEP_Drawing- Colored Area Floor Plan | FloorPlan | Fine | HLR |
| 4 | GEP_Drawing- FS Plan | FloorPlan | Fine | HLR |
| 5 | GEP_Drawing- Roof Site Plan | FloorPlan | Fine | HLR |
| 6 | GEP_Drawing- Typical Plan | FloorPlan | Fine | HLR |
| 7 | GEP_Drawing- Section | Section | Fine | HLR |
| 8 | GEP_Drawing- Elevation | Elevation | Fine | HLR |
| 9 | **GEP_Drawing- Elevation Presentation** 🆕 | Elevation | Fine | ShadingWithEdges |
| 10 | GEP_Modeling- Plan | FloorPlan | Fine | HLR |
| 11 | GEP_Modeling- Floor Finish Plan | FloorPlan | Fine | HLR |
| 12 | GEP_Modeling- 1F Site Plan | FloorPlan | Fine | ShadingWithEdges |
| 13 | GEP_Modeling- Reflected Ceiling Plan | CeilingPlan | Fine | HLR |
| 14 | GEP_Modeling- Elevation | Elevation | Fine | ShadingWithEdges |
| 15 | GEP_Modeling- Section | Section | Fine | HLR |
| 16 | GEP_Modeling- 3D View | ThreeD | Fine | ShadingWithEdges |
| 17 | GEP_Review- Plan_Upper Beam | CeilingPlan | Fine | HLR |
| 18 | GEP_Review- Plan_Lower Beam | FloorPlan | Fine | HLR |

### 從 GEP- 舊版整併的樣版

| 舊版名稱 | 動作 | 新版名稱 | 備註 |
|----------|------|----------|------|
| GEP-Reflected Ceiling Plan | 🗑️ 刪除 | - | 使用 GEP_Modeling- RCP |
| GEP-Elevation | 🗑️ 刪除 | - | 使用 GEP_Drawing- Elevation |
| GEP-SD-Elevation | ✏️ 重命名 | GEP_Drawing- SD Elevation | 保留 SD 用途 |
| GEP-Section | 🗑️ 刪除 | - | 使用 GEP_Drawing- Section |
| GEP-Floor Plan | 🗑️ 刪除 | - | 使用 GEP_Modeling- Plan |
| GEP-Floor Finished Plan | 🗑️ 刪除 | - | 使用 GEP_Modeling- Floor Finish Plan |
| GEP-Door Schedule | ✏️ 重命名 | GEP_Drawing- Door Schedule | 保留功能 |
| GEP-Area Schedule Plan | ✏️ 重命名 | GEP_Drawing- Area Schedule | 保留功能 |
| GEP-Beam Plan | 🗑️ 刪除 | - | 功能整併至 Review 系列 |
| GEP-Furniture Layout Plan | ✏️ 重命名 | GEP_Drawing- Furniture Layout | 保留功能 |
| GEP-Room Color Plan | ✏️ 重命名 | GEP_Drawing- Room Color | 保留功能 |
| GEP-Wall Finished Plan | ✏️ 重命名 | GEP_Drawing- Wall Finish | 保留功能 |
| GEP-3D | ✏️ 重命名 | GEP_Modeling- 3D Wireframe | HLR 線架構 3D |
| GEP-3D Structural | ✏️ 重命名 | GEP_Review- 3D Structural | 結構審查專用 |
| GEP-3D-View | ✏️ 重命名 | GEP_Drawing- 3D Realistic | 簡報渲染用 |

---

## 📐 新增/調整的樣版

### 🆕 GEP_Drawing- Elevation Presentation

**用途**: 簡報用立面圖（著色）

| 設定 | 值 |
|------|------|
| 詳細等級 | Fine |
| 視覺樣式 | ShadingWithEdges |
| 比例尺 | 1:100 |
| 篩選器 | Views - Working (隱藏工作元素) |

**來源**: 複製自 GEP_Drawing- Elevation，修改視覺樣式

---

### 🔧 GEP_Review- Plan_Lower Beam（優化）

**優化策略**: 改用「篩選器」取代大量隱藏類別

#### 現況 vs 優化後

| 項目 | 現況 | 優化後 |
|------|------|--------|
| 隱藏類別數 | 9 | 3-5 |
| 篩選器 | 0 | 2 |

#### 建議新增的篩選器

1. **Structural Elements Only**
   - 規則: Category = Structural Framing, Structural Columns, Structural Foundations
   - 行為: 強調顯示（粗線/顏色）

2. **Non-Structural Hidden**
   - 規則: Category ≠ Structural categories
   - 行為: 半透明或淡化

#### 建議保留隱藏的類別

| 類別 | 原因 |
|------|------|
| Mass | 量體不需要 |
| Topography | 地形不需要 |
| Parts | 零件不需要 |

#### 建議取消隱藏的類別

| 類別 | 原因 |
|------|------|
| Floors | 需要看樓板與梁的關係 |
| ~~Furniture~~ | 透過篩選器淡化即可 |
| ~~Casework~~ | 透過篩選器淡化即可 |

---

## 📊 整併後樣版清單

### GEP_Drawing- 系列 (出圖用) - 共 14 個

| 編號 | 名稱 | 視圖類型 | 視覺樣式 |
|------|------|----------|----------|
| 1 | GEP_Drawing- 1F Plan | FloorPlan | HLR |
| 2 | GEP_Drawing- 1F Site Plan | FloorPlan | HLR |
| 3 | GEP_Drawing- Colored Area Floor Plan | FloorPlan | HLR |
| 4 | GEP_Drawing- FS Plan | FloorPlan | HLR |
| 5 | GEP_Drawing- Roof Site Plan | FloorPlan | HLR |
| 6 | GEP_Drawing- Typical Plan | FloorPlan | HLR |
| 7 | GEP_Drawing- Section | Section | HLR |
| 8 | GEP_Drawing- Elevation | Elevation | HLR |
| 9 | GEP_Drawing- Elevation Presentation 🆕 | Elevation | ShadingWithEdges |
| 10 | GEP_Drawing- SD Elevation | Elevation | HLR |
| 11 | GEP_Drawing- Door Schedule | Elevation | HLR |
| 12 | GEP_Drawing- Area Schedule | FloorPlan | HLR |
| 13 | GEP_Drawing- Furniture Layout | FloorPlan | HLR |
| 14 | GEP_Drawing- Room Color | FloorPlan | HLR |
| 15 | GEP_Drawing- Wall Finish | FloorPlan | HLR |
| 16 | GEP_Drawing- 3D Realistic | ThreeD | RealisticWithEdges |

### GEP_Modeling- 系列 (建模用) - 共 8 個

| 編號 | 名稱 | 視圖類型 | 視覺樣式 |
|------|------|----------|----------|
| 1 | GEP_Modeling- Plan | FloorPlan | HLR |
| 2 | GEP_Modeling- Floor Finish Plan | FloorPlan | HLR |
| 3 | GEP_Modeling- 1F Site Plan | FloorPlan | ShadingWithEdges |
| 4 | GEP_Modeling- Reflected Ceiling Plan | CeilingPlan | HLR |
| 5 | GEP_Modeling- Elevation | Elevation | ShadingWithEdges |
| 6 | GEP_Modeling- Section | Section | HLR |
| 7 | GEP_Modeling- 3D View | ThreeD | ShadingWithEdges |
| 8 | GEP_Modeling- 3D Wireframe | ThreeD | HLR |

### GEP_Review- 系列 (審查用) - 共 4 個

| 編號 | 名稱 | 視圖類型 | 視覺樣式 |
|------|------|----------|----------|
| 1 | GEP_Review- Plan_Upper Beam | CeilingPlan | HLR |
| 2 | GEP_Review- Plan_Lower Beam | FloorPlan | HLR |
| 3 | GEP_Review- 3D Structural | ThreeD | ShadingWithEdges |

---

## ✅ 執行步驟

### Phase 1: 調整現有 GEP_ 樣版

- [ ] 統一所有 GEP_ 樣版的詳細等級為 Fine
- [ ] 套用 Views - Working 篩選器規則
- [ ] 建立 GEP_Drawing- Elevation Presentation

### Phase 2: 整併 GEP- 舊版

- [ ] 重命名需保留的 GEP- 樣版
- [ ] 刪除重複的 GEP- 樣版
- [ ] 驗證設定是否正確

### Phase 3: 優化 Review 系列

- [ ] 建立「Structural Elements Only」篩選器
- [ ] 優化 GEP_Review- Plan_Lower Beam 設定
- [ ] 刪除 GEP-Beam Plan

### Phase 4: 清理與驗證

- [ ] 刪除所有舊版 GEP- 樣版
- [ ] 重新產生視圖樣版報告
- [ ] 驗證所有視圖樣版功能正常

---

## 📝 備註

1. 整併前請先**備份 Revit 專案檔**
2. 建議在**非工作時段**執行整併
3. 整併後需**通知團隊成員**樣版變更

---

**最後更新**: 2026-01-09 08:28
