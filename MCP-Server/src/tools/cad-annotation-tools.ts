import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * CAD 文字標注回填工具：讀 CAD 圖面上的編號文字（C1、B2、S1…），
 * 依空間位置配對「使用者已建好」的柱/梁/樓板，批次寫入「備註」等實例參數。
 * 不建立幾何，適合「自己建模、不想手填備註編號」的工作流。
 *
 * 對應 C# 端 handler: MCP/Core/CadAnnotationExecutor.cs
 * 對應 CommandExecutor.cs cases: preview_comments_from_cad / backfill_comments_from_cad
 */

const sharedProperties = {
    textLayerName: {
        type: "string",
        description:
            "CAD 圖面上編號文字（C1、B2、S1 等）所在的圖層名稱。" +
            "可先用 get_dwg_column_layers 列出視圖內所有 CAD 圖層再選擇。" +
            "CAD 必須以「連結 (Link CAD)」方式加入；DXF 可直接讀，DWG 需已安裝 ODA File Converter。",
    },
    category: {
        type: "string",
        enum: ["column", "beam", "floor"],
        description:
            "要回填的元件類別：column（結構柱＋建築柱）、beam（結構構架）、floor（樓板）。",
    },
    maxDistanceMm: {
        type: "number",
        default: 1500,
        description:
            "（選填）文字與元件的配對容差（mm），預設 1500。" +
            "文字超出此距離不配對；柱梁編號通常標在構件旁，樓板編號標在版內（版內視為距離 0）。",
    },
    parameterName: {
        type: "string",
        description:
            "（選填）要寫入的實例參數名稱。不指定時寫入內建「備註」(Comments) 參數，語系無關，建議留空。",
    },
} as const;

export const cadAnnotationTools: Tool[] = [
    {
        name: "preview_comments_from_cad",
        description:
            "【乾跑預覽】讀取 CAD 文字圖層的編號（C1、B2、S1…），依空間位置配對目前視圖中已建好的柱/梁/樓板，" +
            "回傳「元件 ↔ 編號」對照表、配不到的孤兒文字、沒有編號的元件清單與偵測到的 CAD 單位。" +
            "此工具不修改模型。執行 backfill_comments_from_cad 前必須先呼叫本工具，" +
            "把對照表摘要（配對數、單位、警告）複述給使用者確認後才能寫入。",
        inputSchema: {
            type: "object",
            properties: { ...sharedProperties },
            required: ["textLayerName", "category"],
        },
    },
    {
        name: "backfill_comments_from_cad",
        description:
            "從 CAD 文字圖層批次回填柱/梁/樓板的「備註」編號：配對邏輯與 preview_comments_from_cad 相同，" +
            "並在單一 Transaction 內批次寫入參數（Revit 可一鍵復原）。" +
            "預設不覆蓋已有值的備註（overwrite=false 時略過並回報）。" +
            "執行前必須先跑 preview_comments_from_cad 並取得使用者確認，不可跳過預覽直接寫入。",
        inputSchema: {
            type: "object",
            properties: {
                ...sharedProperties,
                overwrite: {
                    type: "boolean",
                    default: false,
                    description:
                        "（選填）元件備註已有值時是否覆蓋，預設 false（略過並在結果中回報略過數量）。",
                },
            },
            required: ["textLayerName", "category"],
        },
    },
];
