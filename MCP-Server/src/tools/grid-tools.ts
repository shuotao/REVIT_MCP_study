import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * 網格線（Grid）建立工具。
 *
 * 對應 C# 端 handler: MCP/Core/Commands/CommandExecutor.GridCreate.cs、CommandExecutor.GridFromDwg.cs
 * 對應 CommandExecutor.cs cases: create_grids / preview_dwg_grids / create_grids_from_dwg
 */
export const gridTools: Tool[] = [
    {
        name: "create_grids",
        description:
            "依座標（公釐）批次建立直線網格線（軸線），可逐條指定名稱（如 1、2、A、B）。" +
            "與既有網格線共線重疊者會略過（status=duplicate）；名稱已被其他網格線使用者不建立（status=name_conflict）。" +
            "Revit 預設把軸號圓圈放在終點端，需要軸號在哪一端就把該端當 end。" +
            "回傳逐筆 Results（status: created / duplicate / name_conflict / invalid / failed）。",
        inputSchema: {
            type: "object",
            properties: {
                grids: {
                    type: "array",
                    description: "要建立的網格線清單",
                    items: {
                        type: "object",
                        properties: {
                            name: { type: "string", description: "網格線名稱（選填，未填用 Revit 自動編號）" },
                            startX: { type: "number", description: "起點 X（公釐）" },
                            startY: { type: "number", description: "起點 Y（公釐）" },
                            endX: { type: "number", description: "終點 X（公釐）" },
                            endY: { type: "number", description: "終點 Y（公釐）" },
                        },
                        required: ["startX", "startY", "endX", "endY"],
                    },
                },
            },
            required: ["grids"],
        },
    },
    {
        name: "preview_dwg_grids",
        description:
            "讀取目前平面視圖中連結或匯入的 CAD（DWG/DXF）。" +
            "不給 layerName：列出含直線的圖層與各層直線數，並推薦可能的軸線圖層（grid/axis/軸/柱心…）。" +
            "給 layerName：把該圖層的直線（含 PolyLine、圖塊內一層）合併共線段後，預覽將建立的網格線與自動命名，不修改模型。" +
            "建議流程：preview_dwg_grids（無參數）→ preview_dwg_grids（layerName）→ 使用者確認 → create_grids_from_dwg。",
        inputSchema: {
            type: "object",
            properties: {
                layerName: { type: "string", description: "（選填）CAD 軸線圖層名稱。省略則只列出圖層清單" },
                minLengthMm: { type: "number", default: 3000, description: "合併後短於此長度（mm）的線不視為軸線，預設 3000" },
                extendMm: { type: "number", default: 0, description: "兩端各延伸長度（mm），預設 0" },
                verticalLabels: {
                    type: "string",
                    enum: ["numbers", "letters"],
                    default: "numbers",
                    description: "垂直向軸線（由左到右）用數字 1,2,3 或字母 A,B,C；水平向（由下到上）自動用另一種。字母跳過 I、O",
                },
                autoName: { type: "boolean", default: true, description: "是否自動命名；false 則用 Revit 預設編號" },
            },
        },
    },
    {
        name: "create_grids_from_dwg",
        description:
            "依 CAD 指定圖層的直線建立 Revit 網格線（軸線）。參數與 preview_dwg_grids 相同，請先用相同參數預覽並經使用者確認。" +
            "同一直線上被軸號圓圈打斷的線段會合併成一條；與既有網格線共線重疊者略過，名稱衝突者不建立。" +
            "垂直軸線由下往上、水平軸線由左往右建立（軸號圓圈在上方／右方端）。此操作會修改 Revit 模型。",
        inputSchema: {
            type: "object",
            properties: {
                layerName: { type: "string", description: "CAD 軸線圖層名稱" },
                minLengthMm: { type: "number", default: 3000, description: "合併後短於此長度（mm）的線不建立，預設 3000" },
                extendMm: { type: "number", default: 0, description: "兩端各延伸長度（mm），預設 0" },
                verticalLabels: {
                    type: "string",
                    enum: ["numbers", "letters"],
                    default: "numbers",
                    description: "垂直向軸線用數字或字母；水平向自動用另一種",
                },
                autoName: { type: "boolean", default: true, description: "是否自動命名" },
            },
            required: ["layerName"],
        },
    },
];
