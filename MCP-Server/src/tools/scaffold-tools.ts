import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const scaffoldTools: Tool[] = [
    {
        name: "calculate_selected_detail_line_perimeter",
        description: "Fast scaffold takeoff mode. Sum curve lengths from the current Revit selection, including detail lines, detail component family geometry, and filled region boundaries. Use this when the user manually traces the scaffold outline.",
        inputSchema: {
            type: "object",
            properties: {
                includeFamilyGeometry: {
                    type: "boolean",
                    description: "Read measurable curve geometry from selected detail component family instances.",
                    default: true,
                },
                includeFilledRegions: {
                    type: "boolean",
                    description: "Include selected filled region boundary curves.",
                    default: true,
                },
                minCurveLengthMm: {
                    type: "number",
                    description: "Ignore tiny curve fragments shorter than this length.",
                    default: 1,
                },
                scaffoldHeightMm: {
                    type: "number",
                    description: "Optional scaffold height. When provided, the tool also returns perimeter x height area in square meters.",
                },
            },
        },
    },
    {
        name: "calculate_exterior_wall_scaffold_perimeter",
        description: "Reliable scaffold takeoff mode. Automatically detects exterior/perimeter walls from wall centerline geometry using ray exposure tests, excludes interior walls, sums the included wall lengths, optionally selects the result in Revit, and reports the classification evidence.",
        inputSchema: {
            type: "object",
            properties: {
                levelName: {
                    type: "string",
                    description: "Optional Revit level name. When omitted, the active view filter is used if activeViewOnly is true.",
                },
                activeViewOnly: {
                    type: "boolean",
                    description: "Only analyze walls visible in the active view.",
                    default: true,
                },
                includeCurtainWalls: {
                    type: "boolean",
                    description: "Include curtain walls in the exterior perimeter calculation.",
                    default: true,
                },
                selectResult: {
                    type: "boolean",
                    description: "Select detected exterior walls in Revit after calculation so the user can review the result.",
                    default: true,
                },
                includeExcludedWalls: {
                    type: "boolean",
                    description: "Return every excluded wall instead of only the highest-scoring sample.",
                    default: false,
                },
                sampleCount: {
                    type: "number",
                    description: "Number of sample stations tested on each wall. Valid range is clamped to 3-9.",
                    default: 3,
                },
                minimumExposedRatio: {
                    type: "number",
                    description: "Minimum ratio of open-side samples required to classify a wall as exterior.",
                    default: 0.33,
                },
                rayAngleDegrees: {
                    type: "number",
                    description: "Fan angle on both sides of each wall normal for finding blocking walls.",
                    default: 25,
                },
                searchDepthMm: {
                    type: "number",
                    description: "Optional ray search depth. Defaults to twice the analyzed wall bounding diagonal.",
                },
                minWallLengthMm: {
                    type: "number",
                    description: "Ignore very short walls below this length.",
                    default: 300,
                },
                minWallHeightMm: {
                    type: "number",
                    description: "Ignore walls whose effective height is below this value. Effective height uses unconnected height first, then top/base constraints, then bounding box height.",
                    default: 2000,
                },
                endpointToleranceMm: {
                    type: "number",
                    description: "Tolerance used only for reporting connected exterior wall groups.",
                    default: 750,
                },
                includePerimeterBridgeWalls: {
                    type: "boolean",
                    description: "Add short exterior or retaining walls whose endpoints reconnect to the detected exterior wall network. This catches perimeter walls hidden by adjacent walls in ray tests.",
                    default: true,
                },
                bridgeEndpointToleranceMm: {
                    type: "number",
                    description: "Endpoint/network tolerance for perimeter bridge wall recovery.",
                    default: 750,
                },
                maxBridgeWallLengthMm: {
                    type: "number",
                    description: "Maximum wall length eligible for perimeter bridge recovery.",
                    default: 12000,
                },
                scaffoldHeightMm: {
                    type: "number",
                    description: "Optional scaffold height. When provided, the tool also returns perimeter x height area in square meters.",
                },
            },
        },
    },
    {
        name: "calculate_room_scaffold_perimeters",
        description: "Indoor scaffold takeoff mode. Reads placed Revit Rooms and classifies them into general scaffold, interior finish scaffold, or excluded rooms. General scaffold for non-stair/elevator rooms is measured by perimeter x height. Interior finish scaffold for stair/elevator rooms is measured by length x width x height.",
        inputSchema: {
            type: "object",
            properties: {
                level: {
                    type: "string",
                    description: "Optional level name or unambiguous partial level name. When omitted, all placed rooms are considered.",
                },
                levelName: {
                    type: "string",
                    description: "Alias of level.",
                },
                roomIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Optional explicit Room ElementId list. Overrides level filtering.",
                },
                includeUnnamed: {
                    type: "boolean",
                    description: "Whether unnamed placed rooms should be included.",
                    default: true,
                },
                includeRoomDetails: {
                    type: "boolean",
                    description: "Return per-room audit rows.",
                    default: true,
                },
                finishKeywords: {
                    type: "array",
                    items: { type: "string" },
                    description: "Room name/number keywords classified as interior finish scaffold. Defaults include safety stairs, accessible stairs, stairs, elevators, freight elevators, lifts, and passenger elevators.",
                },
                excludeKeywords: {
                    type: "array",
                    items: { type: "string" },
                    description: "Room name/number keywords excluded from general scaffold. Defaults include outdoor platforms, terraces, balconies, pipe shafts, and water tanks.",
                },
                excludeLevels: {
                    type: "array",
                    items: { type: "string" },
                    description: "Level suffixes/names excluded from the room scaffold takeoff. Defaults to FN and TF.",
                },
                minBoundarySegmentLengthMm: {
                    type: "number",
                    description: "Ignore tiny room boundary fragments shorter than this length.",
                    default: 1,
                },
                scaffoldHeightMm: {
                    type: "number",
                    description: "Optional scaffold height. For interior finish scaffold, this is the height used in length x width x height. If omitted, the tool attempts to use room bounding-box height.",
                },
            },
        },
    },
];
