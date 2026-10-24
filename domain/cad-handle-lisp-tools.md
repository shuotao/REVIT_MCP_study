---
name: cad-handle-lisp-tools
description: "AutoCAD 物件 Handle（處理碼）批次讀取與回選 LISP 小工具 SOP：GETHAND 選取物件後依類型分類列出 Handle 並複製到剪貼簿，SELHAND 貼上 Handle 清單回選物件。用於 autocad-mcp 與 Revit MCP 搭配時，以 Handle 取代截圖／文字描述來精準指定 CAD 物件、加速 MCP 溝通。觸發於 CAD handle、處理碼、GETHAND、SELHAND、autocad-mcp 選取物件、DWG 物件定位。"
metadata:
  version: "1.0"
  updated: "2026-10-24"
  references:
    - "https://github.com/laytonluo/cad-handle-tools"
  related:
    - dwg-column-import.md
    - cad-block-point-placement.md
  referenced_by: []
  tags: [AutoCAD, LISP, Handle, 處理碼, autocad-mcp, GETHAND, SELHAND, DWG]
---

# CAD Handle 讀取／回選 LISP 工具 SOP

與 AI／MCP 協作處理 CAD 圖面時，「請處理我現在選的那幾個物件」很難傳遞（autocad-mcp 每次呼叫都可能清掉 AutoCAD 目前的選取集）。**Handle（處理碼）是物件在圖檔內的穩定識別碼**，以文字傳遞最快、最準。本 SOP 提供兩支 LISP 把「選取集 ⇄ Handle 清單」互轉。

工具原始碼：<https://github.com/laytonluo/cad-handle-tools>（`GetHand.lsp`、`SelHand.LSP`，UTF-8；繁中版 AutoCAD 若提示亂碼請轉存 Big5）。

## 工具

| 檔案 | 指令 | 功能 |
|------|------|------|
| `GetHand.lsp` | `GETHAND` | 優先使用預選物件，否則提示框選；依物件類型分類列出 Handle，顯示結果對話窗並複製到 Windows 剪貼簿 |
| `SelHand.LSP` | `SELHAND` | 提示貼上 Handle 清單（空白、逗號、句點、分號、`\|`、Tab 皆可分隔），以 `handent` 轉回物件並設為目前選取集；找不到的 Handle 會列出並計數 |

## 流程

1. AutoCAD 以 `APPLOAD` 載入兩支 LISP。
2. 在 CAD 選取要處理的物件（柱線、圖塊、文字等），執行 `GETHAND`。
3. 從剪貼簿貼給 AI：「這些是柱輪廓的 Handle：…」，MCP 端即可用 Handle 逐一查詢／修改，不必重新描述或截圖。
4. 反向：AI 回報一批 Handle（例如檢核出的問題物件）→ 在 CAD 執行 `SELHAND` 貼上，立即選取並縮放檢視。

## 注意事項

- Handle 僅在**同一個 DWG 檔案內**唯一；跨檔案、或 Wblock／重新匯出後可能改變，貼給 MCP 時要同時說明來源圖檔。
- Handle 為十六進位字串（如 `1A3F`），不分大小寫。
- `SELHAND` 使用 `sssetfirst`，選取集在命令結束後仍保留；但 autocad-mcp 後續呼叫可能清除選取，需要 MCP 操作時請把 Handle 清單一併帶入，不要依賴即時選取。
- 兩支 LISP 為作者在自己的環境實測可用；AutoCAD 版本差異（LT 不支援 LISP）未逐一驗證。
