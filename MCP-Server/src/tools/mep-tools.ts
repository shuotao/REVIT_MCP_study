/**
 * MEP 管線工具 — mep Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const mepTools: Tool[] = [
    {
        name: "get_connector_info",
        description: "取得 MEP 元素（管、風管、線管等）的接頭（Connector）資訊，包含座標、連接狀態、形狀等。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要查詢的 MEP 元素 ID" },
            },
            required: ["elementId"],
        },
    },
    {
        name: "add_pipe_cap",
        description: "在管件的未連線端安裝管帽或法蘭。自動尋找開放的接頭並連接。",
        inputSchema: {
            type: "object",
            properties: {
                pipeId: { type: "number", description: "管件的元素 ID" },
                familyName: { type: "string", description: "要安裝的管帽/法蘭族群名稱" },
            },
            required: ["pipeId", "familyName"],
        },
    },
    {
        name: "export_families",
        description: "把專案中已載入的可編輯族群另存為 .rfa 檔到指定資料夾,建立可重用元件庫。預設匯出管配件(OST_PipeFitting)與管附件(OST_PipeAccessory)。自動依類別建立子資料夾;subFolderBySeries=true 時再依族群名稱系列(CIP/DWV/碳鋼.../)細分。略過系統族群、現地(in-place)與不可編輯族群。",
        inputSchema: {
            type: "object",
            properties: {
                outputFolder: { type: "string", description: "輸出根資料夾絕對路徑,例如 C:\\Users\\xxx\\Desktop\\MEP管元件庫。不存在會自動建立。" },
                categories: {
                    type: "array",
                    items: { type: "string" },
                    description: "要匯出的 BuiltInCategory 名稱清單(如 OST_PipeFitting、OST_PipeAccessory)。省略則預設這兩類。",
                },
                subFolderBySeries: { type: "boolean", description: "是否在類別資料夾下再依族群名稱系列建立子資料夾(預設 false,只依類別分層)。" },
                overwrite: { type: "boolean", description: "目標 .rfa 已存在時是否覆寫(預設 true)。" },
            },
            required: ["outputFolder"],
        },
    },
];
