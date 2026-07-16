import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * IFC structural sync tool — reclaimed contributor impl (PR #82, Jacky820507).
 * C# handler: CommandExecutor.IfcStructuralSync.cs -> SyncIfcStructuralToNative.
 * See domain/ifc-structural-native-sync.md.
 */
export const ifcStructuralSyncTools: Tool[] = [
    {
        name: "sync_ifc_structural_to_native",
        description: "Convert structural framing (beams) and columns from a linked IFC model into native Revit structural framing/column instances, matching detected section sizes to family types. Supports dry-run preview, batched creation, section-based column base-type selection (steel / SHS / RC), and optional alignment of column tops to the floor bottom above.",
        inputSchema: {
            type: "object",
            properties: {
                linkInstanceId: {
                    type: "number",
                    description: "ElementId of the linked IFC model instance to read structural elements from.",
                },
                apply: {
                    type: "boolean",
                    description: "Actually create the native elements. When false the tool behaves as a preview.",
                },
                dryRun: {
                    type: "boolean",
                    description: "Preview only; analyse and report without modifying the model.",
                },
                replaceExisting: {
                    type: "boolean",
                    description: "Replace native elements previously created by a prior sync.",
                },
                includeFraming: {
                    type: "boolean",
                    description: "Include structural framing (beams) in the sync.",
                },
                includeColumns: {
                    type: "boolean",
                    description: "Include structural columns in the sync.",
                },
                maxFraming: {
                    type: "number",
                    description: "Cap the number of framing members processed (omit / 0 for no cap).",
                },
                maxColumns: {
                    type: "number",
                    description: "Cap the number of columns processed (omit / 0 for no cap).",
                },
                batchSize: {
                    type: "number",
                    description: "Number of elements created per transaction batch.",
                },
                minLengthMm: {
                    type: "number",
                    description: "Ignore framing shorter than this length (mm).",
                },
                sizeRoundMm: {
                    type: "number",
                    description: "Rounding granularity (mm) applied when matching section sizes to family types.",
                },
                framingCategory: {
                    type: "string",
                    description: "Override the IFC category treated as framing.",
                },
                columnCategory: {
                    type: "string",
                    description: "Override the IFC category treated as columns.",
                },
                baseFramingType: {
                    type: "string",
                    description: "Base family type name used as the framing template.",
                },
                baseColumnType: {
                    type: "string",
                    description: "Base family type name used as the column template.",
                },
                baseSteelColumnType: {
                    type: "string",
                    description: "Base family type used for steel (H / wide-flange) columns.",
                },
                baseShsColumnType: {
                    type: "string",
                    description: "Base family type used for square-hollow-section (SHS) columns.",
                },
                baseRcColumnType: {
                    type: "string",
                    description: "Base family type used for RC (rectangular concrete) columns.",
                },
                autoColumnBaseType: {
                    type: "boolean",
                    description: "Auto-select the column base family type per detected section instead of always using baseColumnType.",
                },
                shsColumnMinSizeMm: {
                    type: "number",
                    description: "Minimum square size (mm) for a section to be classified as SHS.",
                },
                shsSquareToleranceMm: {
                    type: "number",
                    description: "Width/depth tolerance (mm) used when deciding if a section is square (SHS).",
                },
                alignColumnTopsToFloorBottom: {
                    type: "boolean",
                    description: "Align created column tops to the bottom of the floor above, detected by ray/geometry probing.",
                },
                maxColumnTopSearchDistanceMm: {
                    type: "number",
                    description: "Maximum upward search distance (mm) when aligning column tops to the floor bottom.",
                },
                sourceTagPrefix: {
                    type: "string",
                    description: "Prefix written to Mark / Comments to tag synced elements by their IFC source.",
                },
            },
        },
    },
];
