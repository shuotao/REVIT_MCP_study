import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const openingTypeTools: Tool[] = [
    {
        name: "create_opening_types",
        description:
            "批次建立門/窗『型別』（FamilySymbol）：複製指定的種子型別（seed）→ 命名 → 設定寬×高。輸入寬高單位為公分（cm），內部自動換算為 Revit 英尺。**只建立型別，絕不在模型中放置任何門窗實體**。可重複執行（idempotent）：同一族群中已存在同名型別則略過。適合把 CAD 門窗表的『編號→寬×高』一次生成整套型別，供後續 door-window-legend-tools 產生門窗圖例使用。寬或高任一參數設定不到即回滾該筆並列入 failed（不留尺寸錯一半的型別）。回傳 created / skipped / failed 三個清單。",
        inputSchema: {
            type: "object",
            properties: {
                items: {
                    type: "array",
                    description: "要建立的型別清單，每筆對應一個門或窗型別。",
                    items: {
                        type: "object",
                        properties: {
                            typeName: {
                                type: "string",
                                description: "新型別名稱（如 D1f、W11）。",
                            },
                            category: {
                                type: "string",
                                enum: ["door", "window"],
                                description: "門或窗：door=門（OST_Doors）、window=窗（OST_Windows）。",
                            },
                            seedFamilyName: {
                                type: "string",
                                description: "種子族群名稱（要複製的來源 Family）。可先用 list_family_symbols 查詢可用族群。",
                            },
                            seedTypeName: {
                                type: "string",
                                description: "種子型別名稱（該 Family 下要複製的來源 Type）。",
                            },
                            widthCm: {
                                type: "number",
                                description: "寬度，單位公分（cm）。",
                            },
                            heightCm: {
                                type: "number",
                                description: "高度，單位公分（cm）。",
                            },
                        },
                        required: [
                            "typeName",
                            "category",
                            "seedFamilyName",
                            "seedTypeName",
                            "widthCm",
                            "heightCm",
                        ],
                    },
                },
            },
            required: ["items"],
        },
    },
];
