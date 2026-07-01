import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * DWG 結構樑匯入工具：掃描 CAD 匯入/連結圖層、解析正交雙線段、建立 Revit 結構樑。
 *
 * 對應 C# 端 handler: MCP/Core/DwgBeamExecutor.cs
 * 對應 CommandExecutor.cs cases: get_dwg_beam_layers / preview_dwg_beams / create_beams_from_dwg
 */
export const dwgBeamTools: Tool[] = [
    {
        name: "get_dwg_beam_layers",
        description:
            "掃描目前 Revit 平面視圖中所有 CAD 匯入/連結的圖層名稱，" +
            "回傳圖層清單並自動推薦可能包含樑、標註文字的圖層。" +
            "使用前請確認 Revit 已開啟平面視圖且已匯入 CAD 檔案。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "preview_dwg_beams",
        description:
            "解析 CAD 指定圖層中的雙線幾何，預覽識別到的樑中心線資訊（區分 X 向與 Y 向）。" +
            "此工具不會建立任何 Revit 元素，僅回傳解析結果供確認數量。" +
            "建議在執行 create_beams_from_dwg 前先呼叫此工具確認數量。",
        inputSchema: {
            type: "object",
            properties: {
                layerName: {
                    type: "string",
                    description: "CAD 圖層名稱，請從 get_dwg_beam_layers 回傳的清單中選擇樑輪廓圖層",
                },
            },
            required: ["layerName"],
        },
    },
    {
        name: "create_beams_from_dwg",
        description:
            "從 CAD 指定圖層建立 Revit 結構樑（必須搭配文字標注圖層）。\n" +
            "重要：建議按類型分批執行（先大樑、再次樑、再地樑），\n" +
            "每批只處理一個線條圖層與對應的文字圖層。\n" +
            "使用 beamRole 參數標示目前處理的批次。",
        inputSchema: {
            type: "object",
            properties: {
                layerName: {
                    type: "string",
                    description: "CAD 樑輪廓線條的圖層名稱（必填）",
                },
                familyName: {
                    type: "string",
                    description: "指定使用的族群名稱（必填），例如「2_RC樑-矩形」。",
                },
                typeName: {
                    type: "string",
                    description:
                        "（選填）指定族群類型名稱（例如「60x80」）。" +
                        "有填此參數時為「快速模式」，該批次所有樑都用這個型別建立，忽略文字圖層。" +
                        "沒填時為「名稱對應模式」，依文字標注比對類型。",
                },
                textLayerNameX: {
                    type: "string",
                    description:
                        "（名稱對應模式必填其一）CAD 圖面上 X 軸向標注樑名稱（例如水平向的 B1）的文字圖層。" +
                        "文字僅會與 X 軸向的中心線進行配對，避免干擾。",
                },
                textLayerNameY: {
                    type: "string",
                    description:
                        "（名稱對應模式必填其一）CAD 圖面上 Y 軸向標注樑名稱（例如垂直向的 B1）的文字圖層。" +
                        "文字僅會與 Y 軸向的中心線進行配對，避免干擾。",
                },
                beamRole: {
                    type: "string",
                    description: "（選填）標示此批次處理的樑角色，例如「大樑」、「次樑」、「地樑」。用於回報顯示。",
                },
            },
            required: ["layerName", "familyName"],
        },
    },
];
