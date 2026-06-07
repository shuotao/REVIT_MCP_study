import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const dwgGridRegistrationTools: Tool[] = [
    {
        name: "import_dwg_to_levels",
        description: "Import one DWG into CAD staging floor plan views named CAD 放樣_{LevelName}. Creates missing floor plans, imports origin-to-origin with ThisViewOnly, pins the imported CAD, and returns a per-level report.",
        inputSchema: {
            type: "object",
            properties: {
                dwgPath: {
                    type: "string",
                    description: "Absolute path to the DWG file."
                },
                levelNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "Optional Level names to process. If omitted, all Levels are used."
                },
                levelIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Optional Revit Level ElementIds to process."
                },
                settings: {
                    type: "object",
                    properties: {
                        thisViewOnly: { type: "boolean", default: true },
                        pinAfterLoad: { type: "boolean", default: true },
                        visibleLayersOnly: { type: "boolean", default: true },
                        unit: { type: "string", default: "Millimeter" },
                        colorMode: { type: "string", default: "BlackAndWhite" },
                        loadMode: { type: "string", enum: ["Import"], default: "Import" },
                        placementMode: { type: "string", enum: ["OriginToOrigin", "OriginThenGridCheck"], default: "OriginThenGridCheck" },
                        toleranceMm: { type: "number", default: 1.0 }
                    },
                    additionalProperties: false
                }
            },
            required: ["dwgPath"]
        }
    }
];
