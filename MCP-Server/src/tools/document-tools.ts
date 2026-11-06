import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const documentTools: Tool[] = [
    {
        name: "list_open_documents",
        description: "列出目前 Revit 進程中所有非連結文件，適合在需要確認可操作的文件、作用文件或儲存目標前使用。",
        inputSchema: {
            type: "object",
            properties: {}
        }
    },
    {
        name: "open_document",
        description: "開啟指定的本機 Revit 文件並設為作用文件；可選擇從中央模型卸離並保留工作集。若文件已開啟，Revit API 無法用程式切換作用視窗，需使用者手動點選該文件視窗。",
        inputSchema: {
            type: "object",
            properties: {
                filePath: {
                    type: "string",
                    description: "要開啟的本機 .rvt 文件完整路徑。"
                },
                detachFromCentral: {
                    type: "boolean",
                    description: "是否從中央模型卸離並保留工作集；預設為 false。"
                }
            },
            required: ["filePath"]
        }
    },
    {
        name: "save_document",
        description: "儲存作用中的 Revit 文件，或依文件標題儲存已開啟的指定文件。此工具不支援另存新檔，唯讀文件無法儲存。",
        inputSchema: {
            type: "object",
            properties: {
                documentTitle: {
                    type: "string",
                    description: "已開啟文件的標題；省略時儲存作用文件。"
                }
            }
        }
    },
    {
        name: "reload_links",
        description: "重載作用文件中的 Revit 連結模型。可用連結類型 ID 或名稱篩選；兩者都省略時重載全部連結。個別連結失敗不會中斷其他連結的重載。",
        inputSchema: {
            type: "object",
            properties: {
                linkTypeIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "要重載的 RevitLinkType ElementId 陣列。"
                },
                linkNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "要重載的 RevitLinkType 名稱陣列。"
                }
            }
        }
    },
    {
        name: "close_document",
        description: "關閉目前 Revit 進程中已開啟的非連結文件（依標題比對）。常見情境：某個連結檔被獨立開啟以編輯，編輯完須先關閉才能讓宿主文件正常載入該連結。文件為目前作用文件時，Revit API 無法程式關閉作用文件，會回傳明確錯誤，需使用者先切換或開啟其他文件；連結文件與家族文件不受此限（家族文件可關閉，連結文件會被拒絕，須改由宿主文件的連結管理處理）。文件已修改時，預設拒絕關閉，除非指定 save 先儲存，或 discardChanges 放棄變更。",
        inputSchema: {
            type: "object",
            properties: {
                documentTitle: {
                    type: "string",
                    description: "要關閉的已開啟文件標題（比對 Document.Title）。"
                },
                save: {
                    type: "boolean",
                    description: "關閉前是否先儲存變更；預設為 false。"
                },
                discardChanges: {
                    type: "boolean",
                    description: "文件已修改且未指定 save 時，設為 true 以放棄變更強制關閉；預設為 false。"
                }
            },
            required: ["documentTitle"]
        }
    }
];
