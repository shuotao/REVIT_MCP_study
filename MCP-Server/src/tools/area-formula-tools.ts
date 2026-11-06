/**
 * 送照樓板面積計算式工具 — architect Profile
 *
 * 方法定義於 domain/permit-floor-area-formula.md。
 * 本工具「消費使用者已切分好的區域／房間」，不自行分解多邊形：
 * 每個來源邊界必須已經是矩形或三角形，否則會被列入 Undecomposed 並跳過。
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const areaFormulaTools: Tool[] = [
    {
        name: "create_floor_area_formula",
        description:
            "產出建照送審用的樓板面積計算式文字（矩形 長×寬、三角形 底×高÷2），並可寫成圖面上的 TextNote。來源是使用者已用區域邊界線／房間分隔線切分好的封閉區域，本工具不自行分解多邊形；無法判定為矩形或三角形的區域、以及含內環（開口、管道間、樓梯間等鏤空）的區域，都會列入 Undecomposed 回報而不編入計算式。每一項都會拿「四捨五入後的邊長乘積」與「實際幾何面積」對帳，差異超過容差即列入 Mismatches，且 dryRun=false 時預設會擋下寫入。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "目標視圖 ElementId。省略時使用目前作用中的視圖（建議在建地平面視圖作業）。",
                },
                sourceIds: {
                    type: "array",
                    items: { type: "number" },
                    description:
                        "指定要列入計算式的 Area／Room ElementId，順序即計算式列出順序。省略時取該視圖內所有已放置的 Area（若無 Area 則取 Room），並依上到下、左到右排序。",
                },
                sourceType: {
                    type: "string",
                    enum: ["auto", "area", "room"],
                    description: "sourceIds 省略時的自動收集對象。auto（預設）＝先找 Area，找不到才找 Room。",
                    default: "auto",
                },
                boundaryLocation: {
                    type: "string",
                    enum: ["center", "finish", "coreCenter", "coreBoundary"],
                    description:
                        "邊界取線位置。送照樓地板面積慣例算到牆心，故預設 center；若你的區域邊界線本身就是分割線（非牆），此參數不影響結果。",
                    default: "center",
                },
                label: {
                    type: "string",
                    description: "計算式抬頭文字，例如「一樓樓地板面積」。省略則不加抬頭。",
                },
                itemPrefix: {
                    type: "string",
                    description: "每一項的編號前綴，例如 A 會產生 A1、A2。留空則不編號，直接列算式。",
                    default: "",
                },
                decimals: {
                    type: "number",
                    description: "邊長與面積的小數位數（四捨五入）。送審慣例為 2。",
                    default: 2,
                },
                x: {
                    type: "number",
                    description: "TextNote 放置點 X（mm，專案座標）。dryRun=false 時必填。",
                },
                y: {
                    type: "number",
                    description: "TextNote 放置點 Y（mm，專案座標）。dryRun=false 時必填。",
                },
                textTypeName: {
                    type: "string",
                    description: "指定 TextNoteType 名稱。省略時使用專案中第一個文字類型，回傳值會列出實際用到哪一個。",
                },
                areaUnitLabel: {
                    type: "string",
                    description: "合計列的面積單位文字。字型缺字時可改成 m2。",
                    default: "㎡",
                },
                angleToleranceDeg: {
                    type: "number",
                    description: "判定直角的角度容差（度）。四頂點且四角皆在容差內才視為矩形。",
                    default: 1.0,
                },
                areaToleranceM2: {
                    type: "number",
                    description:
                        "計算式乘積與實際幾何面積的允許差異（m²）。超過即列入 Mismatches；四捨五入本身就會產生微小差異，故不可設為 0。",
                    default: 0.05,
                },
                dryRun: {
                    type: "boolean",
                    description: "true（預設）＝只回傳算式與對帳結果，不在圖面建立 TextNote。確認對帳全過後再以 false 執行（需同時給 x/y）。",
                    default: true,
                },
                allowMismatches: {
                    type: "boolean",
                    description:
                        "false（預設）＝只要有任何一項對帳不符（Mismatches 非空），dryRun=false 就丟例外、不建立 TextNote。確認過那些差異是可接受的才帶 true 強制寫入。",
                    default: false,
                },
            },
            required: [],
        },
    },
];
