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
            "若指定 familyName，會從該族群的現有類型中依尺寸比對，直接使用原有柱名稱（如 C1、C2）；" +
            "未指定時自動挑選最適族群並按尺寸建立新類型。" +
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
                familyName: {
                    type: "string",
                    description:
                        "（選填）指定使用的族群名稱，例如「B1_RC柱-矩形」。" +
                        "指定後會從該族群現有類型中依尺寸比對，保留 C1/C2/C3 等原有柱名稱；" +
                        "尺寸無對應時才建立新類型。不指定則自動挑選最適族群。",
                },
                textLayerName: {
                    type: "string",
                    description:
                        "（選填）CAD 圖面上標注柱名稱（C1、C2 等）文字所在的圖層名稱。" +
                        "指定後會讀取該圖層的 TEXT/MTEXT 文字，依空間距離將最近的標注對應到每根柱，" +
                        "再以標注文字（如 C1）在 familyName 族群中尋找對應類型。" +
                        "DXF 格式可直接讀取；DWG 格式需安裝 ODA File Converter，" +
                        "未安裝時會回傳 labelReadStatus=no_oda 並附上安裝說明，柱仍會建立但不對應名稱。",
                },
            },
            required: ["layerName"],
        },
    },
];
