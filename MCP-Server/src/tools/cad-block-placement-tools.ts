import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * CAD 圖塊插入點批次放置 Revit 點位族群（灑水頭/閥件等）。
 *
 * 對應 C# 端 handler: MCP/Core/CadBlockPlacementExecutor.cs
 * 對應 CommandExecutor.cs cases: get_dwg_block_instances / preview_family_instances_from_dwg_blocks / create_family_instances_from_dwg_blocks
 * 對應 domain SOP: domain/cad-block-point-placement.md
 *
 * v1 只支援 Linked DWG、僅 OneLevelBased（non-hosted、level-based、point-placement）FamilySymbol。
 */
export const cadBlockPlacementTools: Tool[] = [
    {
        name: "get_dwg_block_instances",
        description:
            "掃描目前 Revit 平面視圖中已連結（Linked，非 Imported）的 CAD DWG，" +
            "列出可辨識的 Block（INSERT）名稱、每種數量、插入點與旋轉角範例。" +
            "唯讀操作，不建立任何 Revit 元素。使用前請確認 DWG 已用「連結」方式匯入。",
        inputSchema: {
            type: "object",
            properties: {
                importInstanceUniqueId: {
                    type: "string",
                    description:
                        "（選填）指定要掃描的 ImportInstance UniqueId；未指定時取視圖內第一個 Linked ImportInstance",
                },
            },
        },
    },
    {
        name: "preview_family_instances_from_dwg_blocks",
        description:
            "對指定 Block 的每個插入點做座標鏈健檢（Block insertion point → Block transform → " +
            "ImportInstance TotalTransform），回傳每點狀態：ready / duplicate_existing / duplicate_in_batch / " +
            "unsupported_family / untrustworthy_transform，並攤開完整座標鏈供核對。" +
            "唯讀操作，不建立任何 Revit 元素。transform 不可信時只回傳警告，不做任何猜測性修正。" +
            "familySymbolId 與 levelId 必須明確指定，本工具不自動選擇。",
        inputSchema: {
            type: "object",
            properties: {
                importInstanceUniqueId: {
                    type: "string",
                    description: "（選填）指定要掃描的 ImportInstance UniqueId，比照 get_dwg_block_instances",
                },
                blockName: {
                    type: "string",
                    description: "從 get_dwg_block_instances 回傳清單中選擇的 Block 名稱，只處理此名稱的插入點",
                },
                familySymbolId: {
                    type: "string",
                    description: "目標 FamilySymbol 的 ElementId（字串）。必須是 non-hosted、level-based、OneLevelBased 族群",
                },
                levelId: {
                    type: "string",
                    description: "目標 Level 的 ElementId（字串）。Level 必須已存在，本工具不自動建立",
                },
                offsetMm: {
                    type: "number",
                    default: 0,
                    description: "相對於 levelId 對應樓層的垂直偏移，單位為 mm（不是 Revit 內部的 feet）",
                },
                duplicateToleranceMm: {
                    type: "number",
                    description: "（選填）重複判定容差，單位 mm；未指定時預設 10mm",
                },
            },
            required: ["familySymbolId", "levelId"],
        },
    },
    {
        name: "create_family_instances_from_dwg_blocks",
        description:
            "以與 preview_family_instances_from_dwg_blocks 完全相同的參數重新掃描來源後建立 FamilyInstance——" +
            "不信任任何先前呼叫的快取結果。主 Transaction + 逐筆 SubTransaction，單筆失敗不影響其他已成功項目。" +
            "duplicate 項目預設會被阻擋，只有明確傳入 skipDuplicates=true 才會略過（不得由 AI 自行推定使用者已核准）。" +
            "unsupported_family／untrustworthy_transform 一律不建立。回傳每筆結果（created/failed/skipped）與建立後" +
            "獨立查詢驗證的 verifiedExists。此操作會修改 Revit 模型，無法自動復原，請先呼叫 preview 確認再執行。",
        inputSchema: {
            type: "object",
            properties: {
                importInstanceUniqueId: {
                    type: "string",
                    description: "（選填）比照 preview，必須與該次 preview 使用的值一致",
                },
                blockName: {
                    type: "string",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                familySymbolId: {
                    type: "string",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                levelId: {
                    type: "string",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                offsetMm: {
                    type: "number",
                    default: 0,
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                duplicateToleranceMm: {
                    type: "number",
                    description: "比照 preview，必須與該次 preview 使用的值一致",
                },
                skipDuplicates: {
                    type: "boolean",
                    default: false,
                    description:
                        "使用者明確核准後才可設為 true，用來略過 duplicate_existing／duplicate_in_batch 項目。" +
                        "AI 不得在使用者未表達核准的情況下自行設定此參數為 true",
                },
            },
            required: ["familySymbolId", "levelId"],
        },
    },
];
