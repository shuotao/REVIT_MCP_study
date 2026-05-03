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
    }
];
