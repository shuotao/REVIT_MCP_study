import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const viewCropBoxTools: Tool[] = [
    {
        name: "align_view_cropbox_to_element",
        description: "將指定視圖對齊到目標元素的 BoundingBox。2D 使用 CropBox（只調整 XY，保留原 Z 深度與 Transform）；3D 為舊客戶端相容入口，會轉交 set_3d_section_box 並使用真正的 View3D Section Box，不操作 CropBox。3D 可分別設定 XY、上方與下方 padding。viewId 不指定時使用 active view。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "要對齊的目標元素 ID（例如 Detail Group 的 ID）" },
                viewId: { type: "number", description: "目標視圖 ID（選填）；不填則使用當前 active view" },
                padding_mm: { type: "number", description: "向外擴大的邊距（公釐），預設 0", default: 0 },
                padding_xy_mm: { type: "number", description: "3D Section Box 的水平外擴距離（mm）；省略時沿用 padding_mm" },
                padding_bottom_mm: { type: "number", description: "3D Section Box 向下外擴距離（mm）；省略時沿用 padding_mm" },
                padding_top_mm: { type: "number", description: "3D Section Box 向上外擴距離（mm）；省略時沿用 padding_mm" },
                includeSupportingBeams: { type: "boolean", description: "3D 時將與目標底面相接且 XY 相交的結構構架完整納入 Section Box", default: false },
                supportingBeamTolerance_mm: { type: "number", description: "判定樑頂貼近目標底面的容許值（mm），預設 300", default: 300 },
                upperStructureMode: { type: "string", enum: ["current_level_only", "include_upper_structure_without_slab"], description: "3D 上層結構路由。current_level_only（預設）：頂面＝目標頂 + padding_top_mm，上層結構全部切掉。include_upper_structure_without_slab：頂面延伸到最近上層樓板底（扣 upperSlabClearance_mm），上層樑可見但不含樓板本體，此時忽略 padding_top_mm；找不到上層樓板會報錯且不修改 Section Box", default: "current_level_only" },
                upperSlabClearance_mm: { type: "number", description: "include_upper_structure_without_slab 時，Section Box 頂面位於上層樓板底下方的距離（mm），預設 10", default: 10 },
            },
            required: ["elementId"],
        },
    },
    {
        name: "set_3d_section_box",
        description: "將非樣板 3D 視圖的真正 Section Box 對齊到指定元素的完整 XYZ BoundingBox，並關閉 CropBox 避免雙重裁切。可分別設定水平、上方與下方 padding，並可自動納入與目標底面相接的完整支承樑，可用 upperStructureMode 選擇只看當層，或延伸到上層樓板底以顯示上層樑但不含樓板。若只需清理由舊流程誤啟用的 3D CropBox，設 clearCropOnly=true，此時可省略 elementId。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標 3D 視圖 Element ID；省略時使用目前作用中的 3D 視圖" },
                elementId: { type: "number", description: "Section Box 要包覆的元素 ID；clearCropOnly=true 時可省略" },
                padding_mm: { type: "number", description: "Section Box 在 X/Y/Z 三方向向外擴大的距離（mm），預設 1000", default: 1000 },
                padding_xy_mm: { type: "number", description: "水平 X/Y 外擴距離（mm）；省略時沿用 padding_mm" },
                padding_bottom_mm: { type: "number", description: "底面向下外擴距離（mm）；省略時沿用 padding_mm" },
                padding_top_mm: { type: "number", description: "頂面向上外擴距離（mm）；省略時沿用 padding_mm" },
                includeSupportingBeams: { type: "boolean", description: "將與目標底面相接且 XY 相交的結構構架完整納入 Section Box；房間檢討建議 true", default: false },
                supportingBeamTolerance_mm: { type: "number", description: "判定樑頂貼近目標底面的容許值（mm），預設 300", default: 300 },
                upperStructureMode: { type: "string", enum: ["current_level_only", "include_upper_structure_without_slab"], description: "3D 上層結構路由。current_level_only（預設）：頂面＝目標頂 + padding_top_mm，上層結構全部切掉。include_upper_structure_without_slab：頂面延伸到最近上層樓板底（扣 upperSlabClearance_mm），上層樑可見但不含樓板本體，此時忽略 padding_top_mm；找不到上層樓板會報錯且不修改 Section Box", default: "current_level_only" },
                upperSlabClearance_mm: { type: "number", description: "include_upper_structure_without_slab 時，Section Box 頂面位於上層樓板底下方的距離（mm），預設 10", default: 10 },
                setActive: { type: "boolean", description: "完成後切換至目標 3D 視圖，預設 true", default: true },
                selectElement: { type: "boolean", description: "完成後選取目標元素，預設 true；clearCropOnly 時忽略", default: true },
                clearCropOnly: { type: "boolean", description: "只關閉 3D 視圖的 CropBox，不修改 Section Box；用於清理舊錯誤狀態", default: false },
            },
        },
    },
    {
        name: "set_section_box",
        title: "設定 3D 視圖剖面框（Section Box）",
        description: "直接設定非樣板 3D 視圖（正交或透視）的 Section Box，不需目標元素。二擇一：(A) minMm＋maxMm＝世界座標（內部原點，mm）軸向對齊的絕對範圍；(B) offsetsMm＝在「目前」Section Box 各面上的相對位移（正＝往外擴、負＝往內縮，可只給部分面；目前框有旋轉時在框自身座標系內位移）。enable 預設 true（開啟 Section Box）。與 set_3d_section_box 相同會關閉正交視圖的 CropBox 避免雙重裁切（透視視圖必須保留裁剪，不動）。每軸必須 min < max。單一交易「設定剖面框」，回傳前後的世界座標 min/max 與尺寸。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標 3D 視圖 Element ID；省略時使用目前作用中的 3D 視圖" },
                minMm: {
                    type: "object",
                    description: "絕對模式：世界座標最小角點（mm，內部原點）；需與 maxMm 同時提供",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y", "z"],
                },
                maxMm: {
                    type: "object",
                    description: "絕對模式：世界座標最大角點（mm，內部原點）；需與 minMm 同時提供",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y", "z"],
                },
                offsetsMm: {
                    type: "object",
                    description: "相對模式：各面位移（mm），正＝往外擴、負＝往內縮；可只給部分面。不可與 minMm/maxMm 同時使用",
                    properties: {
                        minX: { type: "number" }, maxX: { type: "number" },
                        minY: { type: "number" }, maxY: { type: "number" },
                        minZ: { type: "number" }, maxZ: { type: "number" },
                    },
                },
                enable: { type: "boolean", description: "設定後開啟 Section Box，預設 true；false 時只寫入範圍、關閉 Section Box", default: true },
            },
        },
    },
    {
        name: "shift_view_cropbox",
        description: "在 CropBox 自身座標系中平移視圖的 CropBox。dx 正值往右、dy 正值往上（單位公釐）。viewId 不指定時使用 active view。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "目標視圖 ID（選填）；不填則使用當前 active view" },
                dx_mm: { type: "number", description: "X 方向位移（公釐，正值往右）", default: 0 },
                dy_mm: { type: "number", description: "Y 方向位移（公釐，正值往上）", default: 0 },
            },
        },
    },
];
