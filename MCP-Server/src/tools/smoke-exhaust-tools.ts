/**
 * 排煙窗法規檢討工具 — mep, fire-safety Profile
 * 來源：PR#12 (@7alexhuang-ux)，經跨版本修正後整合
 * 法規：建技規§101① + 消防§188
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const smokeExhaustTools: Tool[] = [
    {
        name: "check_smoke_exhaust_windows",
        description: "排煙窗檢討：檢查天花板下 80cm 內可開啟窗面積是否 ≥ 區劃面積 2%。同時判定無窗居室。法源：建技規§101① + 消防§188。自動上色：綠=全開、黃=折減、紅=固定。",
        inputSchema: {
            type: "object",
            properties: {
                levelName: { type: "string", description: "樓層名稱" },
                ceilingHeightSource: { type: "string", enum: ["room_parameter", "ceiling_element"], description: "天花板高度來源", default: "room_parameter" },
                colorize: { type: "boolean", description: "是否自動上色窗戶", default: true },
                smokeZoneHeight: { type: "number", description: "有效帶高度（mm），預設 800", default: 800 },
                excludeKeywords: { type: "array", items: { type: "string" }, description: "非居室排除關鍵字" },
            },
            required: ["levelName"],
        },
    },
    {
        name: "check_floor_effective_openings",
        description: "無開口樓層判定：檢查樓層外牆有效開口面積是否 ≥ 樓地板面積 1/30。法源：消防§4 + §28③。",
        inputSchema: {
            type: "object",
            properties: {
                levelName: { type: "string", description: "樓層名稱" },
                colorize: { type: "boolean", description: "是否自動上色開口", default: true },
            },
            required: ["levelName"],
        },
    },
    {
        name: "create_section_view",
        description: "建立剖面視圖，面向指定牆面。可用於檢視窗戶與天花板的高度關係。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "目標牆的 Element ID" },
                viewName: { type: "string", description: "視圖名稱", default: "排煙檢討剖面" },
                offset: { type: "number", description: "剖面距牆偏移（mm），預設 1000", default: 1000 },
                scale: { type: "number", description: "比例尺（如 50 = 1:50），預設 50", default: 50 },
            },
            required: ["wallId"],
        },
    },
    {
        name: "create_detail_lines",
        description: "在視圖上繪製詳圖線（天花板線、有效帶範圍線等），可指定顏色和標籤。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標視圖的 Element ID" },
                lines: {
                    type: "array",
                    description: "線段陣列",
                    items: {
                        type: "object",
                        properties: {
                            startX: { type: "number", description: "起點 X（mm）" },
                            startY: { type: "number", description: "起點 Y（mm）" },
                            endX: { type: "number", description: "終點 X（mm）" },
                            endY: { type: "number", description: "終點 Y（mm）" },
                            color: { type: "object", properties: { r: { type: "number" }, g: { type: "number" }, b: { type: "number" } } },
                            lineStyle: { type: "string", description: "線條樣式（選填）" },
                            label: { type: "string", description: "標籤（選填）" },
                        },
                        required: ["startX", "startY", "endX", "endY"],
                    },
                },
            },
            required: ["viewId", "lines"],
        },
    },
    {
        name: "create_filled_region",
        description: "建立填充區域（如排煙有效帶色塊），可設定顏色和透明度。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標視圖的 Element ID" },
                points: {
                    type: "array",
                    description: "多邊形頂點（至少 3 個點，自動封閉）",
                    items: { type: "object", properties: { x: { type: "number" }, y: { type: "number" } }, required: ["x", "y"] },
                },
                color: { type: "object", properties: { r: { type: "number" }, g: { type: "number" }, b: { type: "number" } } },
                transparency: { type: "number", description: "透明度 0-100，預設 50", default: 50 },
                regionType: { type: "string", description: "填充區域類型名稱（選填）" },
            },
            required: ["viewId", "points"],
        },
    },
    {
        name: "create_text_note",
        description: "在視圖上建立文字標註。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標視圖的 Element ID" },
                x: { type: "number", description: "X 座標（mm）" },
                y: { type: "number", description: "Y 座標（mm）" },
                text: { type: "string", description: "文字內容" },
                textSize: { type: "number", description: "文字大小（mm），預設 2.5", default: 2.5 },
            },
            required: ["viewId", "x", "y", "text"],
        },
    },
    {
        name: "export_smoke_review_excel",
        description: "匯出排煙窗檢討 Excel 報告（.xlsx），含樓層總覽、房間明細、窗戶明細、改善建議四個工作表。",
        inputSchema: {
            type: "object",
            properties: {
                levelName: { type: "string", description: "樓層名稱" },
                ceilingHeightSource: { type: "string", enum: ["room_parameter", "ceiling_element"], default: "room_parameter" },
                outputPath: { type: "string", description: "輸出路徑（選填）" },
            },
            required: ["levelName"],
        },
    },
];
