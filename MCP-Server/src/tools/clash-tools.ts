/**
 * MEP vs CSA 碰撞偵測工具 — mep, full Profile
 *
 * 工作流程（見 domain/mep-csa-clash-detection.md）：
 *   get_linked_models → query_linked_elements → detect_clashes
 *   → colorize_clashes → export_clash_report
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const clashTools: Tool[] = [
    {
        name: "get_linked_models",
        description: "列出當前專案中所有連結模型（RevitLinkInstance），含 LinkInstanceId、檔名、路徑、載入狀態、Transform 原點。用於碰撞偵測第一步：找到 MEP 連結的 LinkInstanceId。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "query_linked_elements",
        description: "查詢指定連結模型中的元素，支援品類篩選、參數過濾、自訂回傳欄位。座標會自動套用連結模型 Transform，確保與主模型對齊。",
        inputSchema: {
            type: "object",
            properties: {
                linkInstanceId: { type: "number", description: "連結模型實體 ID（來自 get_linked_models）" },
                category: { type: "string", description: "Revit 品類名稱，例如 Pipes、Ducts、CableTrays、Walls、StructuralFraming" },
                filters: {
                    type: "array",
                    description: "參數過濾條件",
                    items: {
                        type: "object",
                        properties: {
                            field: { type: "string", description: "參數名（例 System Type、Size）" },
                            operator: { type: "string", description: "equals / contains / not_equals / less_than / greater_than" },
                            value: { type: "string", description: "比對值" },
                        },
                    },
                },
                returnFields: {
                    type: "array",
                    items: { type: "string" },
                    description: "額外回傳的參數欄位名（選填）",
                },
                maxCount: { type: "number", description: "最大回傳數量", default: 500 },
            },
            required: ["linkInstanceId", "category"],
        },
    },
    {
        name: "get_element_geometry",
        description: "抽取元素的幾何資訊，支援主模型或連結模型元素。geometryType 可選 centerline（中心線）、boundingbox（範圍盒）、solid（實體統計）、all（全部）。連結模型的座標會套用 Transform。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "元素 ID" },
                linkInstanceId: { type: "number", description: "若為連結模型元素，需提供連結實體 ID（選填）" },
                geometryType: {
                    type: "string",
                    description: "幾何類型：centerline / boundingbox / solid / all",
                    default: "centerline",
                },
                applyTransform: { type: "boolean", description: "是否套用連結模型 Transform", default: true },
            },
            required: ["elementId"],
        },
    },
    {
        name: "detect_clashes",
        description: "執行 MEP 管線（連結模型）與 CSA 結構體（主模型）的碰撞偵測，使用 Curve-to-Solid 策略：MEP 抽中心線、CSA 保留實體、計算穿透線段。回傳每筆碰撞的入口/出口座標、貫穿長度、截面積、佔用體積，以及依系統/依 CSA 品類的統計摘要。",
        inputSchema: {
            type: "object",
            properties: {
                mepSource: {
                    type: "object",
                    description: "MEP 管線來源（通常來自連結模型）",
                    properties: {
                        linkInstanceId: { type: "number", description: "連結模型 ID；為 0 或省略時從主模型讀取" },
                        category: { type: "string", description: "單一品類名稱（例 Pipes）；與 categories 擇一" },
                        categories: {
                            type: "array",
                            items: { type: "string" },
                            description: "品類清單（例 [Pipes, Ducts, CableTrays]）；預設為這三類",
                        },
                        filters: {
                            type: "array",
                            description: "參數過濾（例 System Type contains 消防、Size greater_than 100）",
                            items: {
                                type: "object",
                                properties: {
                                    field: { type: "string" },
                                    operator: { type: "string" },
                                    value: { type: "string" },
                                },
                            },
                        },
                    },
                },
                csaSource: {
                    type: "object",
                    description: "CSA 結構來源（通常為主模型）",
                    properties: {
                        categories: {
                            type: "array",
                            items: { type: "string" },
                            description: "結構品類清單；預設 [Walls, Floors, StructuralFraming, StructuralColumns]",
                        },
                    },
                },
                options: {
                    type: "object",
                    description: "運算選項",
                    properties: {
                        useCoarseFilter: { type: "boolean", description: "是否啟用 BoundingBox 粗篩", default: true },
                        tolerance: { type: "number", description: "公差（mm）", default: 0 },
                        levelFilter: { type: "string", description: "樓層名稱過濾（選填）" },
                        maxResults: { type: "number", description: "最大回傳碰撞數", default: 1000 },
                    },
                },
            },
            required: ["mepSource"],
        },
    },
    {
        name: "colorize_clashes",
        description: "將 detect_clashes 的結果視覺化上色到 CSA 元素。colorScheme 可選 by_csa_category（柱紅/樑橘/板黃/牆藍）、by_system（依 MEP 系統）、by_severity（依貫穿深度嚴重度）。",
        inputSchema: {
            type: "object",
            properties: {
                clashData: {
                    type: "object",
                    description: "detect_clashes 的完整回傳物件（含 Clashes 陣列）",
                },
                colorScheme: {
                    type: "string",
                    description: "配色方案：by_csa_category / by_system / by_severity",
                    default: "by_csa_category",
                },
                viewId: { type: "number", description: "目標視圖 ID；省略則使用當前 active view" },
            },
            required: ["clashData"],
        },
    },
    {
        name: "export_clash_report",
        description: "匯出 detect_clashes 結果為報表。format 可選 csv、json、both。預設輸出到桌面，檔名含時間戳。",
        inputSchema: {
            type: "object",
            properties: {
                clashData: {
                    type: "object",
                    description: "detect_clashes 的完整回傳物件（含 Clashes 陣列）",
                },
                format: { type: "string", description: "輸出格式：csv / json / both", default: "csv" },
                outputPath: { type: "string", description: "自訂輸出路徑（不含副檔名）；省略則輸出到桌面" },
                reportTitle: { type: "string", description: "報表標題", default: "MEP-CSA 碰撞偵測報告" },
            },
            required: ["clashData"],
        },
    },
];
