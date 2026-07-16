import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * Filled-region / fill-pattern tools — reclaimed contributor impl (PR #79/#81, Jacky820507).
 * C# handlers: CommandExecutor.FillPatterns.cs (create_rc_filled_region, batch_create_rc_filled_region,
 * convert_drafting_to_model_pattern, auto_convert_rotated_viewport_patterns) and
 * CommandExecutor.RoomFilledRegions.cs (create_room_filled_regions).
 * See domain/rc-filled-region-workflow.md and domain/revit-fill-pattern-conversion.md.
 */
export const fillRegionTools: Tool[] = [
    {
        name: "create_rc_filled_region",
        description: "Create RC (reinforced-concrete) filled-region stickers in the active view by detecting RC element outlines, drawn with an invisible line style so only the hatch shows.",
        inputSchema: {
            type: "object",
            properties: {
                filledRegionTypeName: {
                    type: "string",
                    description: "Name of the FilledRegionType to use for the stickers.",
                    default: "深灰色",
                },
            },
        },
    },
    {
        name: "batch_create_rc_filled_region",
        description: "Batch-create RC filled-region stickers across multiple views, or across every placed viewport on the given sheets.",
        inputSchema: {
            type: "object",
            properties: {
                viewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Target view ElementIds to process.",
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "Sheet numbers whose placed viewports will each be processed.",
                },
                filledRegionTypeName: {
                    type: "string",
                    description: "Name of the FilledRegionType to use for the stickers.",
                    default: "深灰色",
                },
            },
        },
    },
    {
        name: "convert_drafting_to_model_pattern",
        description: "Convert drafting fill patterns to model fill patterns in the document so hatching stays fixed to the model rather than the sheet. Takes no parameters.",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "auto_convert_rotated_viewport_patterns",
        description: "Automatically fix fill patterns on rotated viewports so hatch orientation follows the rotation correctly. Takes no parameters.",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "create_room_filled_regions",
        description: "Create filled regions covering rooms in a view (e.g. to colour/mark rooms), optionally clearing previously created markers and setting colour and transparency.",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "Target view ElementId. Defaults to the active view when omitted.",
                },
                roomIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Optional explicit Room ElementId list. When omitted, rooms in the view are used.",
                },
                clearExisting: {
                    type: "boolean",
                    description: "Remove previously created room filled regions (matched by marker) before creating new ones.",
                },
                marker: {
                    type: "string",
                    description: "Marker/comment tag written onto created filled regions so they can be cleared later.",
                },
                filledRegionTypeName: {
                    type: "string",
                    description: "Name of the FilledRegionType to use.",
                },
                transparency: {
                    type: "number",
                    description: "Fill transparency, 0-100.",
                },
                color: {
                    type: "object",
                    description: "Fill colour as RGB components (0-255).",
                    properties: {
                        r: { type: "number" },
                        g: { type: "number" },
                        b: { type: "number" },
                    },
                },
            },
        },
    },
];
