import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * CAD 連結工具：將指定 DWG/DXF 檔案連結到指定 Revit 視圖。
 *
 * 對應 C# 端 handler: MCP/Core/CadLinkExecutor.cs
 * 對應 Revit API 2026: Document.Link(string, DWGImportOptions, View, out ElementId)
 */
export const cadLinkTools: Tool[] = [
    {
        name: "link_cad_to_view",
        description:
            "將指定的 CAD 檔案（.dwg / .dxf）連結（Link，非 Import）到 Revit 中的指定視圖。" +
            "預設 thisViewOnly=true，連結僅在該視圖可見；3D 視圖不支援 thisViewOnly。" +
            "視圖可用 viewId（最精確）或 viewName（會自動 fallback：精確 → 包含；多筆會回傳候選清單）。" +
            "此操作會修改 Revit 模型，且 Revit 不允許程式化卸除連結，請謹慎使用。",
        inputSchema: {
            type: "object",
            properties: {
                filePath: {
                    type: "string",
                    description: "CAD 檔案絕對路徑（.dwg 或 .dxf）",
                },
                viewId: {
                    type: "number",
                    description: "目標視圖的 Element ID（與 viewName 二擇一）",
                },
                viewName: {
                    type: "string",
                    description:
                        "目標視圖名稱（與 viewId 二擇一）。" +
                        "找不到完全相符時會自動 fallback 到包含搜尋；多筆相符會回傳候選清單。",
                },
                thisViewOnly: {
                    type: "boolean",
                    default: true,
                    description:
                        "true=連結僅在指定視圖可見（最常見的「連結到指定視圖」語意）；" +
                        "false=全模型可見，View 僅用於 Level 參考。3D 視圖必須設 false。",
                },
                placement: {
                    type: "string",
                    enum: ["Origin", "Centered", "Site", "Shared", "DefaultLocation"],
                    default: "Origin",
                    description:
                        "對齊模式。Origin=自動原點對原點；Centered=置中；Site=站點；Shared=共用座標。",
                },
                unit: {
                    type: "string",
                    enum: [
                        "Default",
                        "Foot",
                        "Inch",
                        "Meter",
                        "Decimeter",
                        "Centimeter",
                        "Millimeter",
                        "Custom",
                    ],
                    default: "Default",
                    description: "DWG 單位。Default=使用 DWG 內定單位。",
                },
                colorMode: {
                    type: "string",
                    enum: ["BlackAndWhite", "Preserved", "InvertColors"],
                    default: "BlackAndWhite",
                    description: "顏色模式。BlackAndWhite 為 CAD 底圖最常見設定。",
                },
                visibleLayersOnly: {
                    type: "boolean",
                    default: false,
                    description: "true=只匯入 DWG 中可見的圖層。",
                },
                orientToView: {
                    type: "boolean",
                    default: false,
                    description: "true=方向對齊視圖。需要 thisViewOnly=false。",
                },
                autoCorrectAlmostVHLines: {
                    type: "boolean",
                    default: true,
                    description: "自動將近似水平/垂直的線校正為精準水平/垂直。",
                },
                customScale: {
                    type: "number",
                    description: "自訂縮放係數（>0），會覆蓋 unit。不填或 ≤0 則沿用 unit。",
                },
                referencePoint: {
                    type: "object",
                    description: "插入點（公釐，會自動轉換為 Revit 內部單位英尺）。",
                    properties: {
                        x: { type: "number" },
                        y: { type: "number" },
                        z: { type: "number" },
                    },
                },
            },
            required: ["filePath"],
        },
    },
    {
        name: "link_cads_by_floor",
        description:
            "批次將多個 CAD 檔連結到對應樓層的平面視圖，並把每個 CAD 平移對齊到指定的 Revit Grid 交點。" +
            "自動從 CAD 檔名抽樓層代碼（支援 FL1/FL2、B1/B1F、RF、1F/2F、地下1樓、3樓 等格式）→ 找名稱含該代碼的 FloorPlan。" +
            "對齊方式：Link 後做 ElementTransformUtils.MoveElement，translation = (Revit Grid 交點 − CAD 錨點)。" +
            "強制 Placement=Origin、不旋轉、不支援自訂 referencePoint / orientToView / customScale（會干擾對齊）。" +
            "全部在單一 Transaction 內，失敗會 commit 已成功的部分並回報每筆狀態。",
        inputSchema: {
            type: "object",
            properties: {
                items: {
                    type: "array",
                    description: "要連結的 CAD 清單，每筆指定檔案與在 CAD 內的對齊錨點座標。",
                    items: {
                        type: "object",
                        properties: {
                            filePath: {
                                type: "string",
                                description: "CAD 檔案絕對路徑（.dwg / .dxf）。檔名須含可辨識樓層代碼。",
                            },
                            cadAnchorPoint: {
                                type: "object",
                                description: "CAD 內對應 Grid 交點的座標（公釐）。",
                                properties: {
                                    x: { type: "number" },
                                    y: { type: "number" },
                                },
                                required: ["x", "y"],
                            },
                        },
                        required: ["filePath", "cadAnchorPoint"],
                    },
                },
                gridLabelX: {
                    type: "string",
                    description: "Revit Grid 標籤（X 方向那條），例如「X1」或「A」。",
                },
                gridLabelY: {
                    type: "string",
                    description: "Revit Grid 標籤（Y 方向那條），例如「Y1」或「1」。",
                },
                thisViewOnly: {
                    type: "boolean",
                    default: true,
                    description: "true=連結只在對應 FloorPlan 可見。",
                },
                unit: {
                    type: "string",
                    enum: ["Default", "Foot", "Inch", "Meter", "Decimeter", "Centimeter", "Millimeter", "Custom"],
                    default: "Default",
                },
                colorMode: {
                    type: "string",
                    enum: ["BlackAndWhite", "Preserved", "InvertColors"],
                    default: "BlackAndWhite",
                },
                visibleLayersOnly: {
                    type: "boolean",
                    default: false,
                },
                autoCorrectAlmostVHLines: {
                    type: "boolean",
                    default: true,
                },
            },
            required: ["items", "gridLabelX", "gridLabelY"],
        },
    },
];
