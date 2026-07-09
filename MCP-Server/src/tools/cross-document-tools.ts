import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const crossDocumentTools: Tool[] = [
    {
        name: "read_source_file_sheets",
        description: "開啟來源 .rvt 檔案並讀取所有圖紙 (sheets)、視埠 (viewports)、視圖 (views) 與 ScheduleSheetInstance 的 metadata。同時偵測與目標檔（目前開啟的專案）的衝突。回傳結構化 JSON 供 AI agent 預覽與規劃後續 copy_sheets_from_file 操作。不修改任何檔案。",
        inputSchema: {
            type: "object",
            properties: {
                sourceFilePath: {
                    type: "string",
                    description: "來源 .rvt 檔案的絕對路徑"
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "要讀取的圖紙編號清單。省略則讀取全部 sheets。"
                },
                keepOpen: {
                    type: "boolean",
                    description: "讀取完成後是否保持來源檔開啟（供後續 copy_sheets_from_file 使用，避免重複開啟）。預設 true。",
                    default: true
                }
            },
            required: ["sourceFilePath"]
        }
    },
    {
        name: "copy_sheets_from_file",
        description: "從來源 .rvt 檔案複製圖紙到目前開啟的專案。讀取來源的 sheet/viewport/view metadata，在目標專案中重建 sheets、匹配或建立 views、放置 viewports 並同步設定。View 依四級分類處理：Tier 1（FloorPlan/CeilingPlan）可自動建立；Tier 2（Section/3D/DraftingView）可部分建立；Tier 3（Legend/Schedule）只匹配不建立；Tier 4（Elevation/Callout/AreaPlan）回報 manual_action_required。衝突項目必須在 conflictResolution 中指定處理方式。",
        inputSchema: {
            type: "object",
            properties: {
                sourceFilePath: {
                    type: "string",
                    description: "來源 .rvt 檔案的絕對路徑"
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "要複製的圖紙編號清單。省略則複製全部 sheets。"
                },
                viewMatchStrategy: {
                    type: "string",
                    enum: ["match_only", "match_or_create"],
                    description: "view 匹配策略。match_only: 只使用目標檔已存在的同名 view；match_or_create: 優先匹配，找不到時建立新 view（僅 Tier 1-2）。預設 match_or_create。",
                    default: "match_or_create"
                },
                conflictResolution: {
                    type: "object",
                    description: "衝突解決設定。當 read_source_file_sheets 偵測到衝突時必須提供。",
                    properties: {
                        sheets: {
                            type: "object",
                            description: "Sheet 衝突解決。Key=圖紙編號, Value='skip'(跳過) 或 'rename'(自動加後綴)",
                            additionalProperties: {
                                type: "string",
                                enum: ["skip", "rename"]
                            }
                        },
                        views: {
                            type: "object",
                            description: "View 衝突解決。Key=view 名稱, Value='use_existing'(使用現有) / 'overwrite'(刪除重建) / 'rename'(新 view 加後綴)",
                            additionalProperties: {
                                type: "string",
                                enum: ["use_existing", "overwrite", "rename"]
                            }
                        },
                        schedules: {
                            type: "object",
                            description: "Schedule 衝突解決。Key=schedule 名稱, Value='use_existing'(使用現有) / 'skip'(不放置)",
                            additionalProperties: {
                                type: "string",
                                enum: ["use_existing", "skip"]
                            }
                        },
                        typeConflicts: {
                            type: "string",
                            enum: ["use_destination", "use_source"],
                            description: "CopyElements 時遇到同名 type 的全域處理策略。use_destination: 使用目標檔的 type（安全預設）；use_source: 使用來源檔的 type。",
                            default: "use_destination"
                        }
                    }
                },
                copyDraftingContents: {
                    type: "boolean",
                    description: "是否複製 DraftingView 的內容（detail lines, text notes 等）。預設 true。",
                    default: true
                },
                copySheetCustomParameters: {
                    type: "boolean",
                    description: "是否複製 sheet 上的 custom parameters（如 sheet folder/圖集、Discipline、繪圖人、Issue Date 等）。同名 + 同 StorageType 的 writable parameter 會被複製，ElementId 類型跳過（跨文件 ID 不可移植）。預設 true。",
                    default: true
                },
                syncProperties: {
                    type: "object",
                    description: "控制同步哪些 view 屬性到目標。全部預設 true。",
                    properties: {
                        scale: { type: "boolean", description: "同步 Scale", default: true },
                        cropBox: { type: "boolean", description: "同步 CropBox", default: true },
                        viewTemplate: { type: "boolean", description: "同步 View Template（優先名稱匹配，若目標檔不存在則自動從來源複製）", default: true },
                        detailLevel: { type: "boolean", description: "同步 Detail Level", default: true },
                        displayStyle: { type: "boolean", description: "同步 Display Style", default: true }
                    }
                },
                closeAfterCopy: {
                    type: "boolean",
                    description: "完成後是否關閉來源檔案。預設 true。",
                    default: true
                }
            },
            required: ["sourceFilePath"]
        }
    },
    {
        name: "sync_sheet_parameters_from_source",
        description: "補丁工具：為 target 中已存在的 sheets 從 source 同號 sheet 補齊 custom parameters（例如修補先前 copy_sheets_from_file 沒帶到的 sheet folder/圖集 / Discipline / 繪圖人 等）。**不會重建 sheet、不會動 viewport**，只比對同 SheetNumber 並複製 custom parameters。同名 + 同 StorageType 的 writable parameter 才會被複製，ElementId 類型跳過。觸發條件：使用者提到 sheet folder 缺失、補圖集、修圖紙分組、sheet 沒歸入 folder、補 sheet metadata、sync sheet parameter、batch update sheet parameter。",
        inputSchema: {
            type: "object",
            properties: {
                sourceFilePath: {
                    type: "string",
                    description: "來源 .rvt 檔案的絕對路徑"
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "限定要補的 sheet 編號清單。省略 = 所有 source 中能在 target 找到同號的 sheet。"
                },
                closeAfterSync: {
                    type: "boolean",
                    description: "完成後是否關閉來源檔案。預設 true。",
                    default: true
                }
            },
            required: ["sourceFilePath"]
        }
    }
];
