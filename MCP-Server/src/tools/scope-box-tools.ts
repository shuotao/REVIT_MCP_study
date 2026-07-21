import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const scopeBoxTools: Tool[] = [
    {
        name: "set_scope_box_for_views",
        description: "批次將 ScopeBox 套用到一組 views（透過設定 view 的 VIEWER_VOLUME_OF_INTEREST_CROP 參數）。被套用後 view 的 CropBox 會自動跟隨 ScopeBox 的範圍，以後 ScopeBox 移動或調整大小時 view 會跟著更新。三選一指定目標 views，優先序：viewIds（最精確）> viewNames > viewNameContains。可選 viewTypeFilter 進一步篩選。View template、不支援 ScopeBox 的 view（Schedule, Legend 等）會自動跳過並記錄在 Skipped 中。觸發條件：使用者提到 scope box、範圍框、套用 scope box、批次設定 scope box、scopebox to views。",
        inputSchema: {
            type: "object",
            properties: {
                scopeBoxName: {
                    type: "string",
                    description: "目標 ScopeBox 的名稱（須與 Revit 中 ScopeBox 名稱完全一致；找不到時會列出所有可用名稱）"
                },
                viewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標 view ElementId 清單（最精確的識別方式）"
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
                    description: "限定 view type, 例: ['FloorPlan', 'CeilingPlan']"
                }
            },
            required: ["scopeBoxName"]
        }
    }
];
