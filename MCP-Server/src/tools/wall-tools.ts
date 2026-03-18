/**
 * 牆/門窗/結構工具 — architect, structural Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const wallTools: Tool[] = [
    {
        name: "create_wall",
        description: "在 Revit 中建立一面牆。需要指定起點、終點座標和高度。",
        inputSchema: {
            type: "object",
            properties: {
                startX: { type: "number", description: "起點 X 座標（公釐）" },
                startY: { type: "number", description: "起點 Y 座標（公釐）" },
                endX: { type: "number", description: "終點 X 座標（公釐）" },
                endY: { type: "number", description: "終點 Y 座標（公釐）" },
                height: { type: "number", description: "牆高度（公釐）", default: 3000 },
                wallType: { type: "string", description: "牆類型名稱（選填）" },
            },
            required: ["startX", "startY", "endX", "endY"],
        },
    },
    {
        name: "create_floor",
        description: "在 Revit 中建立樓板。需要指定邊界點座標。",
        inputSchema: {
            type: "object",
            properties: {
                points: { type: "array", description: "樓板邊界點陣列", items: { type: "object", properties: { x: { type: "number" }, y: { type: "number" } } } },
                levelName: { type: "string", description: "樓層名稱", default: "Level 1" },
                floorType: { type: "string", description: "樓板類型名稱（選填）" },
            },
            required: ["points"],
        },
    },
    {
        name: "create_door",
        description: "在指定的牆上建立門。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "要放置門的牆 ID" },
                locationX: { type: "number", description: "門在牆上的位置 X 座標（公釐）" },
                locationY: { type: "number", description: "門在牆上的位置 Y 座標（公釐）" },
                doorType: { type: "string", description: "門類型名稱（選填）" },
            },
            required: ["wallId", "locationX", "locationY"],
        },
    },
    {
        name: "create_window",
        description: "在指定的牆上建立窗。",
        inputSchema: {
            type: "object",
            properties: {
                wallId: { type: "number", description: "要放置窗的牆 ID" },
                locationX: { type: "number", description: "窗在牆上的位置 X 座標（公釐）" },
                locationY: { type: "number", description: "窗在牆上的位置 Y 座標（公釐）" },
                windowType: { type: "string", description: "窗類型名稱（選填）" },
            },
            required: ["wallId", "locationX", "locationY"],
        },
    },
    {
        name: "get_wall_info",
        description: "取得牆的詳細資訊，包含厚度、長度、高度、位置線座標等。",
        inputSchema: {
            type: "object",
            properties: { wallId: { type: "number", description: "牆的 Element ID" } },
            required: ["wallId"],
        },
    },
    {
        name: "create_dimension",
        description: "在指定視圖中建立尺寸標註。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: { type: "number", description: "要建立標註的視圖 ID" },
                startX: { type: "number", description: "起點 X 座標（公釐）" },
                startY: { type: "number", description: "起點 Y 座標（公釐）" },
                endX: { type: "number", description: "終點 X 座標（公釐）" },
                endY: { type: "number", description: "終點 Y 座標（公釐）" },
                offset: { type: "number", description: "標註線偏移距離（公釐）", default: 500 },
            },
            required: ["viewId", "startX", "startY", "endX", "endY"],
        },
    },
    {
        name: "query_walls_by_location",
        description: "查詢指定座標附近的牆體，回傳牆厚度、位置線與牆面座標。",
        inputSchema: {
            type: "object",
            properties: {
                x: { type: "number", description: "搜尋中心 X 座標" },
                y: { type: "number", description: "搜尋中心 Y 座標" },
                searchRadius: { type: "number", description: "搜尋半徑 (mm)" },
                level: { type: "string", description: "樓層名稱 (選填)" },
            },
            required: ["x", "y", "searchRadius"],
        },
    },
    {
        name: "unjoin_wall_joins",
        description: "取消牆體與柱子等元素的幾何接合關係。常用於元素上色前的前置作業。",
        inputSchema: {
            type: "object",
            properties: {
                wallIds: { type: "array", items: { type: "number" }, description: "要取消接合的牆體 Element ID 列表" },
                viewId: { type: "number", description: "視圖 ID" },
            },
        },
    },
    {
        name: "rejoin_wall_joins",
        description: "恢復先前由 unjoin_wall_joins 取消的牆體接合關係。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_all_grids",
        description: "取得專案中所有網格線（Grid）的資訊。",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "get_column_types",
        description: "取得專案中所有可用的柱類型。",
        inputSchema: {
            type: "object",
            properties: { material: { type: "string", description: "篩選材質（選填）" } },
        },
    },
    {
        name: "create_column",
        description: "在指定位置建立柱子。",
        inputSchema: {
            type: "object",
            properties: {
                x: { type: "number", description: "X 座標（公釐）" },
                y: { type: "number", description: "Y 座標（公釐）" },
                bottomLevel: { type: "string", description: "底部樓層名稱", default: "Level 1" },
                topLevel: { type: "string", description: "頂部樓層名稱（選填）" },
                columnType: { type: "string", description: "柱類型名稱（選填）" },
            },
            required: ["x", "y"],
        },
    },
    {
        name: "get_furniture_types",
        description: "取得專案中已載入的家具類型清單。",
        inputSchema: {
            type: "object",
            properties: { category: { type: "string", description: "家具類別篩選（選填）" } },
        },
    },
    {
        name: "place_furniture",
        description: "在指定位置放置家具實例。",
        inputSchema: {
            type: "object",
            properties: {
                x: { type: "number", description: "X 座標（公釐）" },
                y: { type: "number", description: "Y 座標（公釐）" },
                furnitureType: { type: "string", description: "家具類型名稱" },
                level: { type: "string", description: "樓層名稱", default: "Level 1" },
                rotation: { type: "number", description: "旋轉角度（度）", default: 0 },
            },
            required: ["x", "y", "furnitureType"],
        },
    },
];
