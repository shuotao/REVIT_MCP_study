import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const detailCopyTools: Tool[] = [
    {
        name: "copy_detail_items_to_views",
        description:
            "批次複製詳圖項目（DetailCurve、TextNote、FilledRegion、DetailComponent、Dimension）從來源視圖到一或多個目標視圖（同一專案內）。支援 DraftingView 與 model view。",
        inputSchema: {
            type: "object",
            properties: {
                sourceViewId: {
                    type: "number",
                    description: "來源視圖的 Element ID",
                },
                targetViewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標視圖的 Element ID 陣列",
                },
                elementCategories: {
                    type: "array",
                    items: {
                        type: "string",
                        enum: [
                            "DetailCurves",
                            "TextNotes",
                            "FilledRegions",
                            "DetailComponents",
                            "Dimensions",
                            "All",
                        ],
                    },
                    description:
                        '要複製的詳圖項目類別，預設 ["All"]。可指定子集如 ["DetailCurves", "Dimensions"]。Dimensions 包含 Linear/Aligned/Radial/Angular/ArcLength/SpotElevation 等所有尺寸標註類型。',
                    default: ["All"],
                },
                sourceElementIds: {
                    type: "array",
                    items: { type: "number" },
                    description:
                        "指定要複製的元素 ID 清單（選填）。省略則複製來源視圖中符合 elementCategories 的所有詳圖項目。",
                },
                offset: {
                    type: "object",
                    properties: {
                        x: {
                            type: "number",
                            description: "X 方向偏移量（公釐），預設 0",
                        },
                        y: {
                            type: "number",
                            description: "Y 方向偏移量（公釐），預設 0",
                        },
                    },
                    description:
                        "複製後的位置偏移（公釐）。預設 {x:0, y:0} 即原位置。",
                },
                preserveGroups: {
                    type: "boolean",
                    description:
                        "true=收集到的 group 成員會被替換成整個 group instance 一起複製，保留 detail group/model group 結構（推薦）；false=group 成員以個別元素複製，丟失 group 結構（舊行為）。注意：preserveGroups=true 時，若 group 內含其他類別的成員（不在 elementCategories 過濾範圍內），那些成員仍會隨 group 一起複製過去。",
                    default: true,
                },
                fallbackToIndividual: {
                    type: "boolean",
                    description:
                        "true=當批次複製失敗時，自動 fallback 為逐個元素重試，記錄哪些成功哪些失敗（強烈推薦）；false=批次失敗則整批 rollback，回傳 Failed 狀態。Dimension 跨 view 複製常因 host element 引用遺失導致整批 fail，這個參數能自動繞過問題元素只保留可複製的部分。回傳結構新增 Mode（Batch/Individual）、FailedCount、FailedElements、BatchErrorReason 欄位。",
                    default: true,
                },
                dryRun: {
                    type: "boolean",
                    description:
                        "若 true 則只回傳要複製的元素統計，不實際複製。",
                    default: false,
                },
            },
            required: ["sourceViewId", "targetViewIds"],
        },
    },
];
