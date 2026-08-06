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
        name: "get_mep_segments_and_sizes",
        description: "一次盤點整個專案的 MEP Segment 與 Size 目錄（唯讀）。對應 Manage → MEP Settings 裡 Segments and Sizes 對話框的內容：每個管段（PipeSegment＝材質 × Schedule，如 Copper - K、PVC - Sch 40）各一份尺寸表，含 nominal / inner / outer 直徑與 Used in Size Lists、Used in Sizing 兩個勾選狀態；可一併回傳風管（Round / Rectangular / Oval）尺寸表（風管只有 nominal，Revit 的 duct inner/outer 是佔位值故不輸出）。這些資訊 Schedule 與 System Browser 都撈不到。所有尺寸以 mm 回傳，供台灣 CNS 尺寸對帳使用。全量 dump 可達數百筆，先用 summaryOnly=true 看全貌，再用 segmentName 鑽單一管段。",
        inputSchema: {
            type: "object",
            properties: {
                summaryOnly: { type: "boolean", description: "只回傳每個管段/風管形狀的統計（尺寸筆數、勾選筆數），不逐筆列出尺寸，預設 false。全案盤點建議先用這個。" },
                segmentName: { type: "string", description: "只回傳名稱含此字串的管段（不分大小寫），例如 'Copper'、'PVC'、'Copper - K'。給了這個參數時 includeDuct 預設變 false。" },
                includeDuct: { type: "boolean", description: "是否一併回傳風管尺寸表（Round / Rectangular / Oval）。預設 true；但有給 segmentName 時預設 false。" },
                usedOnly: { type: "boolean", description: "只列出有勾選 Used in Size Lists 或 Used in Sizing 的尺寸，預設 false（全列）。" },
            },
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
