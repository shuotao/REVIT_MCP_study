import { Tool } from "@modelcontextprotocol/sdk/types.js";

const rgbSchema = (label: string) => ({
    type: "object" as const,
    description: `${label} RGB (0-255)`,
    properties: { r: { type: "number" }, g: { type: "number" }, b: { type: "number" } }
});

const patternSchema = (label: string) => ({
    type: "string" as const,
    description: `${label}填充樣式名稱（先用 list_fill_patterns 查；'solid' = 實心填滿；'none' = 清除該層）`
});

export const materialGraphicsTools: Tool[] = [
    {
        name: "get_material_graphics",
        description: "讀取材質「圖形」頁籤的設定：是否使用彩現外觀著色、著色顏色、透明度，以及表面／切割的前景與背景填充樣式與顏色。唯讀。作用在目前作用中的文件（要讀連結檔的材質，須先把該連結檔開成作用文件）。觸發條件：使用者問材質顏色、著色顏色、材質填充圖案、材質 hatch、為什麼這個材質在平面圖是這個顏色。",
        inputSchema: {
            type: "object",
            properties: {
                materialId: { type: "number", description: "材質 ElementId（與 materialName 擇一，優先使用 materialId）" },
                materialName: { type: "string", description: "材質名稱（完全比對，不分大小寫）" }
            },
            required: []
        }
    },
    {
        name: "set_material_graphics",
        description: "修改材質「圖形」頁籤：著色顏色（shadingColor）、透明度、表面／切割的前景與背景填充樣式與顏色。改的是材質本身，所以所有用到這個材質的元件、所有視圖都會一起變——這是與 create_view_filter／V/G 覆寫（只作用單一視圖）的關鍵差異。給了 shadingColor 會自動關閉「使用彩現外觀」，否則顏色不會生效。只給要改的欄位即可；dryRun=true 只驗證輸入不寫入。回傳 Before / After（After 為寫入後回讀）。作用在目前作用中的文件：連結檔的材質要在該連結檔內改（改完存檔，主檔重新載入連結）。典型用途：地形草地顏色調淺、給草地材質加 hatch。觸發條件：使用者提到改材質顏色、材質太深/太淺、材質加 hatch、材質表面填充、所有視圖都要生效。",
        inputSchema: {
            type: "object",
            properties: {
                materialId: { type: "number", description: "材質 ElementId（與 materialName 擇一，優先使用 materialId）" },
                materialName: { type: "string", description: "材質名稱（完全比對，不分大小寫）" },
                shadingColor: rgbSchema("著色顏色（著色／一致色彩視覺型式下的表面顏色）"),
                useRenderAppearance: { type: "boolean", description: "是否使用彩現外觀著色。省略時：有給 shadingColor 就自動設為 false" },
                transparency: { type: "number", description: "著色透明度 0-100", minimum: 0, maximum: 100 },
                surfaceForegroundPattern: patternSchema("表面前景"),
                surfaceForegroundColor: rgbSchema("表面前景顏色"),
                surfaceBackgroundPattern: patternSchema("表面背景"),
                surfaceBackgroundColor: rgbSchema("表面背景顏色"),
                cutForegroundPattern: patternSchema("切割前景"),
                cutForegroundColor: rgbSchema("切割前景顏色"),
                cutBackgroundPattern: patternSchema("切割背景"),
                cutBackgroundColor: rgbSchema("切割背景顏色"),
                dryRun: { type: "boolean", description: "true = 只驗證輸入、回報目前設定，不修改材質", default: false }
            },
            required: []
        }
    },
    {
        name: "relink_material_images",
        description: "遞迴搜尋新的材料影像資料夾，依完整檔名（含副檔名、不分大小寫）重新連結目前作用中 Revit 文件內遺失的 Appearance Asset 貼圖。呼叫前必須取得使用者提供的資料夾路徑；若使用者尚未提供，請先要求使用者貼上新的材料影像根資料夾路徑，不要猜測或自行挑選。有效連結保持不變；同名多檔只回報衝突，不自動選擇。dryRun=true 只預覽，不修改模型。",
        inputSchema: {
            type: "object",
            properties: {
                folderPath: {
                    type: "string",
                    description: "執行 Revit Add-in 的 Windows 電腦可存取之材料影像根資料夾絕對路徑；會遞迴搜尋所有子資料夾。"
                },
                dryRun: {
                    type: "boolean",
                    description: "true = 完整掃描並回報預計重連結果，但不修改模型。",
                    default: false
                }
            },
            required: ["folderPath"]
        }
    }
];
