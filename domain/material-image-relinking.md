---
name: material-image-relinking
description: "Revit 材料外觀影像重連 SOP：依完整檔名遞迴尋找遺失的 Appearance Asset 貼圖，處理有效路徑、唯一候選、同名衝突、共用 Asset 與逐 Asset rollback。當使用者提到材料影像、材質貼圖、missing texture、relink material images 或 Appearance Asset 圖檔遺失時使用。"
metadata:
  version: "1.0"
  updated: "2026-10-13"
  references: []
  related:
    - tool-capability-boundary.md
  referenced_by: []
  tags: [material image, appearance asset, relink, 貼圖, 材料影像]
---

# 材料影像重連 / Material Image Relinking

## 目的

使用 `relink_material_images`，將目前作用中 Revit 文件內已遺失的 Material Appearance Asset 影像，重新指向使用者提供的影像資料夾。工具只依完整檔名比對，不依資料夾名稱、檔案大小或相似度猜測。

## 操作順序

1. 先用 `get_active_view` 確認目前作用文件；不得沿用前一輪的文件資訊。
2. 若使用者尚未提供新影像資料夾，先要求使用者貼上執行 Revit Add-in 的 Windows 電腦可存取之絕對路徑或 UNC 路徑。
3. 需要先檢查結果時，以 `dryRun: true` 呼叫；確認後再以 `dryRun: false` 或省略 `dryRun` 正式執行。
4. 執行後檢查 `Summary`，並逐項回報 `Unresolved`、`Conflicts`、`WriteFailures`、`AssetFailures` 與 `FolderErrors`。

## 資料夾與檔名規則

- 將 `folderPath` 正規化為 Windows 絕對路徑，並遞迴掃描所有非 reparse-point 子資料夾。
- 比對鍵為完整檔名，包含副檔名，採 Windows 不分大小寫語意；`brick.JPG` 可匹配 `brick.jpg`，`texture.png` 不可匹配 `texture.jpg`。
- 無法讀取的子資料夾列入 `FolderErrors`，其餘資料夾繼續掃描。
- 原值是有效絕對檔案路徑時直接保留。
- 原值是相對路徑時，以 RVT 所在資料夾解析；未儲存文件沒有可用的 RVT 基準路徑，因此不能判為有效相對路徑。

## 判定表

| 狀態 | 條件 | 動作 |
|---|---|---|
| `empty` | Bitmap 值為空 | 忽略，不列入修復或錯誤 |
| `valid` | 原絕對路徑或 RVT 相對路徑仍存在 | 保留原值，列入 `Valid` |
| `unique` | 原路徑失效，資料夾索引恰有一個同檔名候選 | `dryRun` 列入預計修復；正式執行寫入新路徑 |
| `unresolved` | 找不到同檔名候選 | 保留原值，列入 `Unresolved` |
| `conflict` | 找到兩個以上同檔名候選 | 不猜測、不寫入，列出全部候選路徑 |

## Appearance Asset 與交易規則

- 依 Appearance Asset ElementId 分組；多個 Material 共用同一 Asset 時只處理一次，結果列出全部材料名稱。
- 遞迴走訪 Asset properties 與 connected properties，處理 Diffuse、Bump、Cutout 等使用 Unified Bitmap 的巢狀貼圖；深度超過 64 層視為該 Asset 失敗。
- 正式執行使用一個外層 Transaction，每個 Asset 使用獨立 SubTransaction 與 `AppearanceAssetEditScope`。
- 新路徑必須通過 `AssetPropertyString.IsValidValue` 才可寫入。
- 單一 Asset 發生例外時，只 rollback 該 Asset 並列入 `AssetFailures`；其他 Asset 繼續處理。外層 Transaction 發生致命錯誤時全部 rollback。
- `dryRun: true` 必須產生與正式執行相同的分類。為了讓 `AssetPropertyString.IsValidValue` 在合法的 editable Asset 上執行，可以建立暫時 Transaction、SubTransaction 與 `AppearanceAssetEditScope`，但不得設定 Bitmap 值，且每個 Asset 與外層 Transaction 都必須 rollback，確保模型零變更。

## 非目標

- 不修改 Revit 的 Additional Render Appearance Paths。
- 不自動處理同名衝突，也不以檔案大小、日期或資料夾相似度選檔。
- 不自動儲存文件、不同步中央檔、不重新載入連結模型。
- 不處理連結模型內的材料；需要處理時，先把該模型開成作用文件。

## 驗收

- `Summary.LinksRepaired` 必須等於 `Repaired` 筆數。
- `Summary.WriteFailures` 必須等於 `WriteFailures` 筆數；`Summary.AssetFailures` 必須等於 `AssetFailures` 筆數，兩種失敗不得混算。
- `dryRun` 與正式執行在同一文件及資料夾狀態下，修復候選、未找到與衝突分類必須一致。
- 工具完成不代表模型已儲存；是否儲存仍由使用者決定。
