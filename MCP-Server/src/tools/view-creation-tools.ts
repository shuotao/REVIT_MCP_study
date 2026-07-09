import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const viewCreationTools: Tool[] = [
    {
        name: "create_floor_plans_from_template",
        description: "以指定的 FloorPlan 視圖作為範本，在多個 Level 上批次建立新的樓層平面視圖。複製範本的 ViewFamilyType、View Template、Phase、Phase Filter。適用於補齊缺漏的正式建築圖系列（例：A- 前綴的樓層平面圖）。已存在同名 view 時會跳過並在回傳的 Skipped 清單中記錄原因。",
        inputSchema: {
            type: "object",
            properties: {
                templateViewId: {
                    type: "number",
                    description: "要複製設定的範本 FloorPlan 視圖 ElementId（例：A-十層平面圖）"
                },
                creations: {
                    type: "array",
                    description: "要建立的視圖清單；每一項指定 Level 名稱與新視圖名稱",
                    items: {
                        type: "object",
                        properties: {
                            levelName: {
                                type: "string",
                                description: "目標樓層名稱，需與專案 Level 名稱完全一致（例：11FL）"
                            },
                            newName: {
                                type: "string",
                                description: "新視圖名稱；若已存在同名 view 則會跳過"
                            }
                        },
                        required: ["levelName", "newName"]
                    }
                },
                applyViewTemplate: {
                    type: "boolean",
                    description: "是否套用範本的 View Template。預設 true。",
                    default: true
                }
            },
            required: ["templateViewId", "creations"]
        }
    },
    {
        name: "batch_apply_view_template",
        description: "批次將指定的 ViewTemplate 套用到多個 views。支援透過圖紙（sheets）、view ID、view 名稱或名稱子字串來選取目標 views。可搭配 viewTypeFilter 篩選特定視圖類型。dryRun 模式可預覽變更內容而不實際修改。觸發條件：使用者提到批次修改 view template、套用視圖樣版、sheet 中的 view 改 template。",
        inputSchema: {
            type: "object",
            properties: {
                viewTemplateId: {
                    type: "number",
                    description: "目標 ViewTemplate 的 ElementId（與 viewTemplateName 二擇一）"
                },
                viewTemplateName: {
                    type: "string",
                    description: "目標 ViewTemplate 的名稱（與 viewTemplateId 二擇一；精確匹配）"
                },
                sheetIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "圖紙 ElementId 清單 — 套用到這些圖紙上所有 viewport 對應的 views"
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "圖紙編號清單（如 ['A101', 'A102']）— 套用到這些圖紙上所有 viewport 對應的 views"
                },
                viewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標 view ElementId 清單（精確指定）"
                },
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "目標 view 名稱清單（精確匹配）"
                },
                viewNameContains: {
                    type: "string",
                    description: "view 名稱包含此字串（substring match，case-insensitive）"
                },
                viewTypeFilter: {
                    type: "array",
                    items: { type: "string" },
                    description: "限定 view type，例: ['FloorPlan', 'CeilingPlan', 'Section', 'Elevation', 'ThreeD']"
                },
                dryRun: {
                    type: "boolean",
                    description: "設為 true 時僅預覽變更清單，不實際修改。預設 false"
                },
                skipIfSameTemplate: {
                    type: "boolean",
                    description: "若 view 已使用相同 template 則跳過。預設 true"
                }
            },
            required: []
        }
    }
];
