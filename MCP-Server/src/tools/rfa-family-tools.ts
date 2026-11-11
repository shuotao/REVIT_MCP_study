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
    {
        name: "rfa_create_dimension",
        description:
            "Create a dimension in the active family document. Measures between parallel reference planes. " +
            "Two planes + label binds the dimension to a length parameter (the planes then follow the parameter); three or more planes + equalize sets EQ. " +
            "Combine with rfa_align_to_reference_plane so forms follow the planes.",
        inputSchema: {
            type: "object",
            properties: {
                referencePlanes: {
                    type: "array",
                    items: { type: "string" },
                    description: "Names of parallel reference planes (order does not matter; they are sorted along the normal).",
                },
                label: { type: "string", description: "Length family parameter to label the dimension with (exactly two planes)." },
                equalize: { type: "boolean", description: "Set EQ on a multi-segment dimension (three or more planes)." },
                throughPointMm: {
                    type: "array",
                    items: { type: "number" },
                    description: "[x, y, z] mm point the dimension line passes through. Default: end of the first plane, usually outside the geometry.",
                },
                viewName: { type: "string", description: "Host view. Auto-picked (plan for vertical planes, elevation for horizontal) when omitted." },
            },
            required: ["referencePlanes"],
        },
    },
    {
        name: "rfa_align_to_reference_plane",
        description:
            "Lock form faces to reference planes. Works in the active family document. " +
            "For each plane, finds the form's planar face lying on it and creates a locked alignment, so the form moves with the plane (e.g. when a labelled dimension changes). " +
            "The face must already be coplanar with the plane.",
        inputSchema: {
            type: "object",
            properties: {
                elementId: { type: "number", description: "ElementId of the extrusion / blend / sweep." },
                referencePlanes: { type: "array", items: { type: "string" }, description: "Reference plane names to align to." },
                lock: { type: "boolean", description: "Lock the alignments. Default true.", default: true },
                viewName: { type: "string", description: "Optional host view; auto-picked when omitted." },
            },
            required: ["elementId", "referencePlanes"],
        },
    },
    {
        name: "rfa_create_sweep",
        description:
            "Create a sweep in the active family document. Sweeps closed profile loops along a planar path (handrails, mouldings, frames). " +
            "Profiles are drawn in 2D [u, v] mm and Revit places them perpendicular to the path at its start. " +
            "Path: pathPoints polyline or pathSegments of lines and 3-point arcs, all in one plane; or pathReferenceLineIds (reference lines from " +
            "rfa_create_reference_line) so the sweep follows the lines when parameters move them. With diameterParameter and no profiles, a circular " +
            "profile is generated and its diameter is labelled with the parameter (parametric round pipe); the result includes a flex check.",
        inputSchema: {
            type: "object",
            properties: {
                pathReferenceLineIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "Reference line element ids forming a connected planar path (closed loops allowed). The sweep follows these lines.",
                },
                diameterParameter: { type: "string", description: "Length parameter for a circular profile's diameter (pathReferenceLineIds mode). Profiles may then be omitted." },
                profileCurveIndex: { type: "number", description: "Index of the path curve where the profile is placed (pathReferenceLineIds mode). Default 0." },
                viewName: { type: "string", description: "View looking along the path at the profile, for the diameter label. Auto-picked when omitted." },
                pathPoints: {
                    type: "array",
                    items: { type: "array", items: { type: "number" } },
                    description: "Polyline path [[x, y, z], ...] in mm (family coordinates).",
                },
                closed: { type: "boolean", description: "Close the pathPoints polyline back to its first point." },
                pathSegments: {
                    type: "array",
                    items: {
                        type: "object",
                        properties: {
                            line: { type: "object", properties: { start: { type: "array", items: { type: "number" } }, end: { type: "array", items: { type: "number" } } } },
                            arc: { type: "object", properties: { start: { type: "array", items: { type: "number" } }, mid: { type: "array", items: { type: "number" } }, end: { type: "array", items: { type: "number" } } } },
                        },
                    },
                    description: "Alternative to pathPoints: connected line / arc segments, [x, y, z] mm.",
                },
                profiles: { type: "array", items: profileLoopSchema, description: "Cross-section loops in [u, v] mm; extra loops inside the first become holes." },
                isSolid: { type: "boolean", description: "true = solid (default), false = void.", default: true },
                material: { type: "string", description: "Material name that already exists in the family document (solid only)." },
                associate: {
                    type: "object",
                    description: "Bind properties to family parameters. Keys: material (material param), visible (yes/no param).",
                    additionalProperties: { type: "string" },
                },
            },
        },
    },
    {
        name: "rfa_create_blend",
        description:
            "Create a blend in the active family document. Lofts from a base loop to a top loop (one closed loop each, any shapes) over heightMm along the sketch plane normal. " +
            "Sketch on plane XY/XZ/YZ (+ planeOffsetMm) or a named reference plane; both loops use the same [u, v] mm coordinates.",
        inputSchema: {
            type: "object",
            properties: {
                baseProfile: profileLoopSchema,
                topProfile: profileLoopSchema,
                heightMm: { type: "number", description: "Distance from base to top along the sketch plane normal, mm." },
                plane: { type: "string", enum: ["XY", "XZ", "YZ"], description: "Sketch plane when referencePlane is not given. Default XY." },
                planeOffsetMm: { type: "number", description: "Offset of the sketch plane along its normal axis, mm. Default 0." },
                referencePlane: { type: "string", description: "Sketch the base on this named reference plane instead." },
                isSolid: { type: "boolean", description: "true = solid (default), false = void.", default: true },
                material: { type: "string", description: "Material name that already exists in the family document (solid only)." },
                associate: {
                    type: "object",
                    description: "Bind properties to family parameters. Keys: blendTop, blendBase (length params), material, visible.",
                    additionalProperties: { type: "string" },
                },
            },
            required: ["baseProfile", "topProfile", "heightMm"],
        },
    },
    {
        name: "rfa_create_reference_line",
        description:
            "Create a reference line (straight line or 3-point arc) hosted on a reference plane in the active family document, and constrain it to other reference planes. " +
            "Reference lines are the rig for parametric pipes and bars: constrain the line here (align, or dimension with a length parameter), then sweep a profile along it " +
            "with rfa_create_sweep pathReferenceLineIds. This works where constraining sketch curves does not (family documents have no sketch-edit API). " +
            "The line moves with hostPlane along its normal; use constraints for the in-plane directions. Targets: curve (straight line parallel to the plane), start, end, center (arc). " +
            "Arcs can also carry radiusParameter. Every point must already sit where the constraint says (on the plane, or at the parameter's current distance).",
        inputSchema: {
            type: "object",
            properties: {
                hostPlane: { type: "string", description: "Reference plane the line lies in (its work plane)." },
                line: {
                    type: "object",
                    properties: {
                        startMm: { type: "array", items: { type: "number" }, description: "[x, y, z] mm" },
                        endMm: { type: "array", items: { type: "number" }, description: "[x, y, z] mm" },
                    },
                },
                arc: {
                    type: "object",
                    properties: {
                        startMm: { type: "array", items: { type: "number" } },
                        midMm: { type: "array", items: { type: "number" } },
                        endMm: { type: "array", items: { type: "number" } },
                    },
                    description: "3-point arc, [x, y, z] mm.",
                },
                constraints: {
                    type: "array",
                    items: {
                        type: "object",
                        properties: {
                            target: { type: "string", enum: ["curve", "start", "end", "center"], description: "Which part to constrain. Default curve." },
                            referencePlane: { type: "string" },
                            distanceParameter: { type: "string", description: "Length parameter for the distance. Omit to align and lock (the part must lie on the plane)." },
                            viewName: { type: "string", description: "View for the alignment/dimension. Auto-picked (elevations first) when omitted." },
                        },
                        required: ["referencePlane"],
                    },
                },
                radiusParameter: { type: "string", description: "Arc only: length parameter labelling the radius." },
                viewName: { type: "string", description: "View for the radius dimension. Auto-picked when omitted." },
            },
            required: ["hostPlane"],
        },
    },
];
