/**
 * 帷幕牆 + 立面面板工具 — architect Profile
 * 來源：PR#11 (@7alexhuang-ux)，經跨版本修正後整合
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const curtainWallTools: Tool[] = [
    {
        name: "get_curtain_wall_info",
        description: "取得帷幕牆詳細資訊，包含 Grid 排列、面板尺寸、面板類型分佈等。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "帷幕牆的 Element ID（選填，若不指定則使用目前選取的元素）" },
            },
        },
    },
    {
        name: "get_curtain_panel_types",
        description: "取得專案中所有可用的帷幕牆面板類型。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "create_curtain_panel_type",
        description: "建立新的帷幕牆面板類型，可指定顏色（HEX）和透明度。",
        inputSchema: {
            type: "object",
            properties: {
                typeName: { type: "string", description: "新類型的名稱" },
                color: { type: "string", description: "面板顏色（HEX 格式，如 '#5C4033'）" },
                baseTypeId: { type: "number", description: "基礎類型 ID（選填）" },
            },
            required: ["typeName", "color"],
        },
    },
    {
        name: "apply_panel_pattern",
        description: "將面板排列模式套用到帷幕牆。需要類型映射表和排列矩陣。",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "帷幕牆的 Element ID" },
                typeMapping: { type: "object", description: "類型映射表（字母→面板類型 ID）" },
                matrix: {
                    type: "array",
                    description: "面板排列矩陣，由上到下、由左到右",
                    items: { type: "array", items: { type: "string" } },
                },
            },
            required: ["elementId", "typeMapping", "matrix"],
        },
    },
    {
        name: "create_facade_panel",
        description: "建立單片立面面板（DirectShape）。支援 5 種幾何：curved_panel、beveled_opening、angled_panel、rounded_opening、flat_panel。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "參考牆的 Element ID" },
                geometryType: {
                    type: "string",
                    enum: ["curved_panel", "beveled_opening", "angled_panel", "rounded_opening", "flat_panel"],
                    description: "幾何類型",
                },
                positionAlongWall: { type: "number", description: "沿牆位置（mm）" },
                positionZ: { type: "number", description: "底部 Z 高度（mm）" },
                width: { type: "number", description: "寬度（mm），預設 800" },
                height: { type: "number", description: "高度（mm），預設 3400" },
                depth: { type: "number", description: "弧深/凹入深度（mm），預設 150" },
                thickness: { type: "number", description: "板厚（mm），預設 30" },
                offset: { type: "number", description: "距牆偏移（mm），預設 200" },
                color: { type: "string", description: "顏色（HEX）" },
                name: { type: "string", description: "面板名稱" },
                curveType: { type: "string", enum: ["concave", "convex"], description: "[curved_panel] 曲線類型" },
                tiltAngle: { type: "number", description: "[angled_panel] 傾斜角度（度）" },
                tiltAxis: { type: "string", enum: ["horizontal", "vertical"], description: "[angled_panel] 傾斜軸" },
                bevelDirection: { type: "string", enum: ["center", "up", "down", "left", "right"], description: "[beveled_opening] 斜切方向" },
                bevelDepth: { type: "number", description: "[beveled_opening] 斜切深度（mm）" },
                openingWidth: { type: "number", description: "[opening] 開口寬度（mm）" },
                openingHeight: { type: "number", description: "[opening] 開口高度（mm）" },
                cornerRadius: { type: "number", description: "[rounded_opening] 圓角半徑（mm）" },
                openingShape: { type: "string", enum: ["rounded_rect", "arch", "stadium", "rect"], description: "[rounded_opening] 開口形狀" },
            },
        },
    },
    {
        name: "create_facade_from_analysis",
        description: "根據分析結果批次建立整面立面。在牆面前方批次建立多片 DirectShape 面板，支援多種面板類型和排列模式。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "目標牆的 Element ID" },
                facadeLayers: {
                    type: "object",
                    description: "立面層級定義",
                    properties: {
                        outer: {
                            type: "object",
                            description: "外層面板定義",
                            properties: {
                                offset: { type: "number", description: "距牆偏移（mm），預設 200" },
                                panelTypes: {
                                    type: "array",
                                    description: "面板類型定義陣列",
                                    items: {
                                        type: "object",
                                        properties: {
                                            id: { type: "string", description: "類型代號（如 'A'）" },
                                            name: { type: "string", description: "類型名稱" },
                                            width: { type: "number", description: "寬度（mm）" },
                                            height: { type: "number", description: "高度（mm）" },
                                            depth: { type: "number", description: "弧深（mm）" },
                                            thickness: { type: "number", description: "板厚（mm）" },
                                            curveType: { type: "string", enum: ["concave", "convex"] },
                                            color: { type: "string", description: "顏色（HEX）" },
                                            geometryType: { type: "string", enum: ["curved_panel", "beveled_opening", "angled_panel", "rounded_opening", "flat_panel"] },
                                        },
                                        required: ["id", "width", "color"],
                                    },
                                },
                                pattern: { type: "array", description: "排列矩陣（如 ['ABABAB', 'BABABA']）", items: { type: "string" } },
                                gap: { type: "number", description: "間距（mm），預設 20" },
                                horizontalBandHeight: { type: "number", description: "分隔帶高度（mm）" },
                                floorHeight: { type: "number", description: "層高（mm），預設 3600" },
                            },
                            required: ["panelTypes", "pattern"],
                        },
                    },
                    required: ["outer"],
                },
            },
            required: ["facadeLayers"],
        },
    },
];
