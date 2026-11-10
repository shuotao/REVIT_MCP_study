import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * RFA family-editor tools (rfa_*). They operate on the ACTIVE family document:
 * the user opens or creates a .rfa from a template in Revit first, then these tools
 * add reference planes, extrusions, parameters and types inside it.
 * C# handlers: MCP/Core/Commands/CommandExecutor.RfaFamily.cs.
 * Units: length mm, angle degrees, area m², volume m³.
 */

const profileLoopSchema = {
    type: "object",
    description:
        "One closed loop. Give exactly one of: points (polyline, auto-closed), rectangle, or circle. " +
        "Coordinates are 2D [u, v] in mm on the sketch plane (XY: u=X, v=Y; XZ: u=X, v=Z; YZ: u=Y, v=Z; " +
        "referencePlane: u/v along the plane's own X/Y vectors from its origin). " +
        "Curved Rhino edges must be discretised into points, except full circles.",
    properties: {
        points: {
            type: "array",
            items: { type: "array", items: { type: "number" }, minItems: 2, maxItems: 2 },
            description: "Polyline vertices [[u, v], ...] in mm, at least 3. The loop closes automatically.",
        },
        rectangle: {
            type: "object",
            properties: {
                center: { type: "array", items: { type: "number" }, description: "[u, v] in mm. Default [0, 0]." },
                width: { type: "number", description: "Size along u, mm." },
                height: { type: "number", description: "Size along v, mm." },
            },
        },
        circle: {
            type: "object",
            properties: {
                center: { type: "array", items: { type: "number" }, description: "[u, v] in mm." },
                radius: { type: "number", description: "Radius in mm." },
            },
        },
    },
};

export const rfaFamilyTools: Tool[] = [
    {
        name: "rfa_get_document_info",
        description:
            "Read the active family (.rfa) document. Returns category, placement type, types, family parameters with current values, reference planes, levels, views and existing solid/void forms. " +
            "Call this first before any other rfa_* tool; it fails if the active document is a project instead of a family.",
        inputSchema: { type: "object", properties: {} },
    },
    {
        name: "rfa_create_reference_plane",
        description:
            "Create reference planes in the active family document. Creates one or more named planes. " +
            "Shortcut form: normal X/Y/Z + offsetMm (X/Y planes are vertical and drawn in a plan view; Z planes are horizontal and drawn in a front/back elevation). " +
            "Advanced form: bubbleEndMm + freeEndMm + cutVector. Planes whose name already exists are skipped.",
        inputSchema: {
            type: "object",
            properties: {
                planes: {
                    type: "array",
                    items: {
                        type: "object",
                        properties: {
                            name: { type: "string", description: "Reference plane name, e.g. \"Left\", \"Top\"." },
                            normal: { type: "string", enum: ["X", "Y", "Z"], description: "Plane normal axis (shortcut form)." },
                            offsetMm: { type: "number", description: "Plane position along the normal axis, mm. Default 0." },
                            extentMm: { type: "number", description: "Half-length of the drawn plane line, mm. Default 1000." },
                            bubbleEndMm: { type: "array", items: { type: "number" }, description: "[x, y, z] mm (advanced form)." },
                            freeEndMm: { type: "array", items: { type: "number" }, description: "[x, y, z] mm (advanced form)." },
                            cutVector: { type: "array", items: { type: "number" }, description: "[x, y, z] direction lying in the plane, perpendicular to the line; must be parallel to the host view's direction (advanced form)." },
                            viewName: { type: "string", description: "Optional host view name. Auto-picked when omitted." },
                        },
                        required: ["name"],
                    },
                    description: "Reference planes to create.",
                },
            },
            required: ["planes"],
        },
    },
    {
        name: "rfa_create_extrusion",
        description:
            "Create an extrusion in the active family document. Builds a solid or void from one or more closed 2D profiles (extra loops inside the first become holes). " +
            "Sketch on plane XY/XZ/YZ (+ planeOffsetMm) or on a named reference plane; the extrusion runs along the sketch plane normal (XY → +Z, XZ → -Y, YZ → +X). " +
            "Optionally assign a material and bind extrusionEnd/extrusionStart/material/visible to existing family parameters.",
        inputSchema: {
            type: "object",
            properties: {
                profiles: { type: "array", items: profileLoopSchema, description: "Closed loops; the first is the outer boundary." },
                plane: { type: "string", enum: ["XY", "XZ", "YZ"], description: "Sketch plane when referencePlane is not given. Default XY." },
                planeOffsetMm: { type: "number", description: "Offset of the sketch plane along its normal axis, mm. Default 0." },
                referencePlane: { type: "string", description: "Sketch on this named reference plane instead of plane/planeOffsetMm (keeps the form hosted on it)." },
                startOffsetMm: { type: "number", description: "Extrusion start along the normal, mm. Default 0." },
                endOffsetMm: { type: "number", description: "Extrusion end along the normal, mm. Negative values extrude the other way." },
                isSolid: { type: "boolean", description: "true = solid (default), false = void.", default: true },
                material: { type: "string", description: "Material name that already exists in the family document (solid only)." },
                associate: {
                    type: "object",
                    description: "Bind extrusion properties to family parameters, e.g. {\"extrusionEnd\": \"Height\", \"material\": \"Body Material\"}. Keys: extrusionEnd, extrusionStart (length params), material (material param), visible (yes/no param).",
                    additionalProperties: { type: "string" },
                },
            },
            required: ["profiles", "endOffsetMm"],
        },
    },
    {
        name: "rfa_add_parameter",
        description:
            "Add family parameters to the active family document. Each may carry a formula or an initial value for the current type. " +
            "Existing names are skipped. Values use mm / degrees / m² / m³ / true-false / material name.",
        inputSchema: {
            type: "object",
            properties: {
                parameters: {
                    type: "array",
                    items: {
                        type: "object",
                        properties: {
                            name: { type: "string" },
                            type: {
                                type: "string",
                                enum: ["length", "number", "integer", "yes_no", "text", "angle", "area", "volume", "material"],
                                description: "Parameter data type. Default length.",
                            },
                            group: {
                                type: "string",
                                enum: ["dimensions", "constraints", "materials", "identity_data", "data", "text", "graphics", "construction", "visibility", "other"],
                                description: "Properties-palette group. Default: dimensions (materials for material, identity_data for text).",
                            },
                            isInstance: { type: "boolean", description: "Instance parameter (true) or type parameter (false, default)." },
                            formula: { type: "string", description: "Optional Revit formula, e.g. \"Width / 2\"." },
                            value: { description: "Optional initial value for the current type (ignored when formula is set)." },
                        },
                        required: ["name", "type"],
                    },
                },
            },
            required: ["parameters"],
        },
    },
    {
        name: "rfa_set_family_type",
        description:
            "Create or update a family type in the active family document. The type becomes current; sets parameter values and formulas, optionally rename. " +
            "A new type copies values from baseType (or the current type).",
        inputSchema: {
            type: "object",
            properties: {
                typeName: { type: "string", description: "Type to create or update." },
                baseType: { type: "string", description: "When creating, copy values from this existing type." },
                renameTo: { type: "string", description: "Optional new name for the type." },
                values: {
                    type: "object",
                    description: "Parameter name → value. Length mm, angle degrees, area m², volume m³, yes/no true-false, material by name.",
                    additionalProperties: true,
                },
                formulas: {
                    type: "object",
                    description: "Parameter name → formula string (empty string or null clears the formula).",
                    additionalProperties: true,
                },
            },
            required: ["typeName"],
        },
    },
];
