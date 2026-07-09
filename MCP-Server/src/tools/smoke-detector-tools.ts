/**
 * 消防偵煙探測器設置法規檢討工具
 * 法源：消防法施行細則附表五（偵煙式探測器設置標準）
 * Domain: domain/smoke-detector-check.md
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const smokeDetectorTools: Tool[] = [
    {
        name: "analyze_smoke_detectors",
        description: "偵煙探測器設置分析：掃描當前視圖的探測器、空調出風口（FCU/冷風機）、房間，回傳各探測器的距出風口距離、距牆/樑距離、距天花板距離，以及各房間的探測器數量與樑區格數。供 JS 端進行五項法規判定。",
        inputSchema: {
            type: "object",
            properties: {
                detectorKeywords: {
                    type: "array",
                    items: { type: "string" },
                    description: "偵煙探測器族群名稱關鍵字（預設：偵煙、smoke detector、探測器、感知器）",
                },
                outletKeywords: {
                    type: "array",
                    items: { type: "string" },
                    description: "出風口族群名稱關鍵字（預設：FCU、冷風機、AHU、送風口、出風口、散流器）",
                },
            },
            required: [],
        },
    },
    {
        name: "visualize_detector_results",
        description: "依照法規判定結果對偵煙探測器上色：綠=PASS、紅=FAIL、橙=WARN（出風口資料缺失）。",
        inputSchema: {
            type: "object",
            properties: {
                results: {
                    type: "array",
                    description: "判定結果陣列，每筆含 DetectorId、IsOk、IsWarn",
                    items: {
                        type: "object",
                        properties: {
                            DetectorId: { type: "number", description: "探測器 Element ID" },
                            IsOk:       { type: "boolean", description: "是否通過" },
                            IsWarn:     { type: "boolean", description: "是否為警告（非嚴格 FAIL）" },
                            Message:    { type: "string",  description: "違規說明" },
                        },
                        required: ["DetectorId", "IsOk"],
                    },
                },
            },
            required: ["results"],
        },
    },
];
