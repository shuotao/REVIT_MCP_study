import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const viewCaptureTools: Tool[] = [
    {
        name: "capture_view_image",
        description: "把指定視圖（省略 = 目前作用中的視圖）匯出成點陣圖並直接回傳影像，讓 AI 看得見 Revit 畫面，不必再請使用者手動截圖。用於：確認一項圖形設定實際長什麼樣、判斷 hatch 密度與配色、排查「明明設了卻看不到」的問題、驗收批次操作的結果。只讀模型、只寫系統暫存檔（預設用完即刪），不異動 Revit 模型。明細表（Schedule）與瀏覽器類視圖無法匯出會直接報錯；視圖樣板本身也不行。影像過大時會自動降一階解析度重匯並在 Warnings 說明。觸發條件：使用者提到截圖、擷取畫面、看一下畫面、這樣對不對、幫我確認圖面、screenshot、capture view、或需要目視排查顯示問題。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "目標視圖的 ElementId（省略 = 目前作用中的視圖）"
                },
                pixelSize: {
                    type: "number",
                    description: "輸出影像的邊長像素（依 fitDirection 決定是寬或高），範圍 200-4000，預設 1600。想看細節就調高，但超過 4MB 會自動降階。",
                    minimum: 200,
                    maximum: 4000
                },
                format: {
                    type: "string",
                    enum: ["png", "jpg"],
                    description: "影像格式。png 對線稿壓縮效果好（預設）；jpg 檔案較小，適合著色／擬真視圖。",
                    default: "png"
                },
                fitDirection: {
                    type: "string",
                    enum: ["horizontal", "vertical"],
                    description: "pixelSize 套用在哪個方向。horizontal = 指定寬度（預設）；vertical = 指定高度。",
                    default: "horizontal"
                },
                keepFile: {
                    type: "boolean",
                    description: "true = 保留暫存圖檔並在 SavedPath 回傳路徑；false（預設）= 回傳後即刪除",
                    default: false
                }
            },
            required: []
        }
    }
];
