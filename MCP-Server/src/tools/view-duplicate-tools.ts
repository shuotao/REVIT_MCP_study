import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * View duplication tools — reclaimed contributor impl (PR #82, Jacky820507).
 * C# handler: CommandExecutor.ViewDuplicate.cs -> DuplicateViewsWithDetailing
 */
export const viewDuplicateTools: Tool[] = [
    {
        name: "duplicate_views_with_detailing",
        description: "Duplicate one or more views with detailing (ViewDuplicateOption.WithDetailing keeps view-specific detailing/annotation), optionally renaming them and copying the crop region from the source view. Typical use: create the '高於6m牆標示' tall-partition markup views from a base plan.",
        inputSchema: {
            type: "object",
            properties: {
                sourceViewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Source view ElementId(s) to duplicate. Accepts aliases 'viewIds' or a single 'sourceViewId'. Required (the tool errors if none resolve).",
                },
                targetNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "Optional new names for the duplicated views, positionally matched to sourceViewIds. Accepts aliases 'newNames' / 'targetName'.",
                },
                suffix: {
                    type: "string",
                    description: "Name suffix appended when a target name is not supplied for a view.",
                    default: "高於6m牆標示(W)",
                },
                copyCropFromSource: {
                    type: "boolean",
                    description: "Copy the crop box / crop region from the source view onto the duplicate.",
                    default: true,
                },
                setActiveLast: {
                    type: "boolean",
                    description: "Activate the last duplicated view when finished.",
                    default: false,
                },
            },
        },
    },
];
