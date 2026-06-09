import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * DWG 欄位匯入工具：掃描 CAD 匯入/連結圖層、解析矩形柱、建立 Revit 結構柱或建築柱。
 *
 * 對應 C# 端 handler: MCP/Core/DwgColumnExecutor.cs
 * 對應 CommandExecutor.cs cases: get_dwg_column_layers / preview_dwg_columns / create_columns_from_dwg
 */
export const dwgColumnTools: Tool[] = [
    {
        name: "get_dwg_column_layers",
        description:
            "掃描目前 Revit 平面視圖中所有 CAD 匯入/連結的圖層名稱，" +
            "回傳圖層清單並自動推薦可能包含柱子的圖層。" +
            "使用前請確認 Revit 已開啟平面視圖且已匯入 CAD 檔案。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "preview_dwg_columns",
        description:
            "解析 CAD 指定圖層中的矩形幾何，預覽識別到的柱資訊（位置、寬度、深度、旋轉角）。" +
            "此工具不會建立任何 Revit 元素，僅回傳解析結果供確認。" +
            "建議在執行 create_columns_from_dwg 前先呼叫此工具確認數量與尺寸。",
        inputSchema: {
            type: "object",
            properties: {
                layerName: {
                    type: "string",
                    description: "CAD 圖層名稱，請從 get_dwg_column_layers 回傳的清單中選擇",
                },
            },
            required: ["layerName"],
        },
    },
    {
        name: "create_columns_from_dwg",
        description:
            "從 CAD 指定圖層自動建立 Revit 結構柱或建築柱。" +
            "會自動：辨識矩形輪廓、建立對應尺寸的族群類型、設定底頂樓層、套用旋轉角度。" +
            "執行前建議先呼叫 preview_dwg_columns 確認識別結果。" +
            "此操作會修改 Revit 模型，無法自動復原，請謹慎使用。",
        inputSchema: {
            type: "object",
            properties: {
                layerName: {
                    type: "string",
                    description: "CAD 圖層名稱，請從 get_dwg_column_layers 回傳的清單中選擇",
                },
                columnType: {
                    type: "string",
                    enum: ["structural", "architectural"],
                    default: "structural",
                    description:
                        "柱類型：'structural'（結構柱，OST_StructuralColumns）或 " +
                        "'architectural'（建築柱，OST_Columns）。預設為 structural",
                },
            },
            required: ["layerName"],
        },
    },
];
