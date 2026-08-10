# Revit to Archicad Terminology and Boundary Map

## Contents

1. Mapping rules
2. Core model terms
3. Documentation and organization terms
4. Data and collaboration terms
5. Tool orchestration mapping
6. Stop conditions
7. Official terminology sources

## Mapping rules

- These are semantic hints, not API type conversions.
- `Direct` means the design concept is close; still discover the current command schema.
- `Approximate` means the workflows differ and user intent must be clarified.
- `No 1:1` means do not auto-translate.
- Keep Archicad-native enum values and API property names exactly as discovered.

## Core model terms

| Revit term | Archicad term | Mapping | Guardrail |
|---|---|---|---|
| Document / Project | Project in one running Archicad instance | Direct | Anchor by current instance port. |
| ElementId | Element GUID | No 1:1 | Never copy, cast, or compare across backends. |
| Category | Element type and/or Classification | Approximate | Element type drives API operations; Classification is a separate semantic system. |
| Family | Library Part, Tool-defined element, or attribute-based construction | No 1:1 | Ask whether the intent concerns geometry, reusable content, or classification. |
| Loadable Family | Library Part (often Object, Door, Window, Lamp) | Approximate | Library Part parameters and placement rules differ. |
| System Family | Native Tool element plus attributes/composites/profiles | Approximate | Do not search for a Family container. |
| Family Type | Library Part variation, Favorite, or element defaults | No 1:1 | Discover parameters and creation schema. |
| Instance | Placed element | Direct | Use Archicad GUID. |
| Type parameter | Property, attribute, Library Part parameter, or default | No 1:1 | Identify the actual Archicad data owner first. |
| Instance parameter | Element property or element-specific parameter | Approximate | Read schema and property identifiers. |
| Level | Story | Approximate | Story membership and elevation behavior differ. |
| Room | Zone | Approximate | Boundaries, stamps, categories, and calculations differ. |
| Floor | Slab | Direct for common modeling intent | Confirm element type and structure. |
| Ceiling | Slab, Shell, Morph, or object-based solution | No 1:1 | Ask what geometry/documentation behavior is required. |
| Wall | Wall | Direct | Still discover type/property schema. |
| Curtain Wall | Curtain Wall | Direct concept | Panel/frame APIs and hierarchy differ. |
| Column | Column | Direct concept | Segment/profile behavior may differ. |
| Beam | Beam | Direct concept | Segment/profile behavior may differ. |
| Roof | Roof | Direct concept | Multi-plane and pivot-line semantics differ. |
| Mass | Morph | Approximate | Geometry editing workflows are not equivalent. |
| Material | Building Material and/or Surface | No 1:1 | Structural composition and appearance are separate attributes. |
| Fill Pattern | Fill | Approximate | Distinguish drafting, cut, cover, and surface display intent. |

## Documentation and organization terms

| Revit term | Archicad term | Mapping | Guardrail |
|---|---|---|---|
| View | View Map item / saved View | Approximate | Distinguish live viewpoint from saved view settings. |
| Floor Plan View | Floor Plan viewpoint/view | Approximate | Use Story and Navigator context. |
| Sheet | Layout | Direct documentation intent | Use Layout Book APIs. |
| Title Block | Master Layout content | Approximate | A Layout is based on a Master Layout; it is not a title-block Family instance. |
| Viewport | Drawing placed on Layout | Approximate | Drawing source/update behavior differs. |
| Schedule | Interactive Schedule / list | Approximate | Verify API exposure; do not assume table-cell operations exist. |
| View Template | Saved View settings / combinations | No 1:1 | Layer, Model View, Graphic Override, Renovation, and scale settings are composed differently. |
| Visibility/Graphics | Layer Combination, Model View Options, Graphic Overrides | No 1:1 | Select the Archicad mechanism that matches the requested scope. |
| Phase | Renovation Status and Renovation Filter | Approximate | Do not map phase IDs or phase filters directly. |
| Revit Link | Hotlink Module, or XREF for DXF/DWG | Approximate | Use Hotlink for Archicad model content; XREF is limited to external DXF/DWG references. Host/source ownership and update workflows differ. |

## Data and collaboration terms

| Revit term | Archicad term | Mapping | Guardrail |
|---|---|---|---|
| Shared Parameter | Property definition / Classification property | Approximate | Property identifiers and availability rules differ. |
| Built-in Parameter | Native element field/property | Approximate | Never reuse Revit parameter names without discovery. |
| Workset | Teamwork reservation/workspace concepts | No 1:1 | Do not automate ownership changes without explicit scope. |
| Design Option | Design Options | Approximate | Confirm current API/Add-On support. |
| Shared Coordinates | Project Location / Survey Point / Project Origin concepts | No 1:1 | Require a coordinate-specific workflow and explicit unit verification. |
| Internal feet | Tool-schema-defined Archicad units | No 1:1 | Never apply Revit unit conversion automatically. |

## Tool orchestration mapping

| Revit-oriented Skill step | Archicad adapter step |
|---|---|
| Call a named Revit MCP tool | Search by application-neutral intent with `archicad_discover_tools`. |
| Pass an ElementId | Pass a GUID returned by the selected Archicad instance. |
| Pass a category name | Inspect whether the command expects element type, classification, or another enum. |
| Use current Revit view | Discover the relevant Navigator/view command and anchor current Archicad state. |
| Mutate then trust success | Re-read affected GUIDs and verify changed fields. |
| Reuse a previous model result | Re-anchor project/port and fetch current state in this turn. |

## Stop conditions

Stop and report a capability gap when:

- discovery returns no command covering a required Domain step;
- only an approximate mapping exists and the user's intent changes the result;
- the command schema lacks required fields or units;
- a write result cannot be verified;
- the selected instance changes or becomes unavailable;
- an identifier originated from Revit or from a different Archicad port.

## Official terminology sources

These Graphisoft references substantiate the Archicad terms above. They do not
guarantee that a matching command is exposed by the currently installed MCP or
Tapir Add-On, so command discovery remains mandatory.

- [Story Settings and elevation behavior](https://help.graphisoft.com/AC/29/INT/_AC29_Help/140_UserInterfaceDialogBoxes/140_UserInterfaceDialogBoxes-33.htm)
- [Home Story behavior](https://help.graphisoft.com/AC/29/INT/_AC29_Help/040_ElementsVB/040_ElementsVB-4.htm)
- [Navigator Project Map, viewpoints, views, and schedules](https://help.graphisoft.com/AC/29/INT/_AC29_Help/030_Interaction/030_Interaction-4.htm)
- [Interactive Schedule](https://help.graphisoft.com/AC/29/INT/_AC29_Help/055_InteractiveSchedule/055_InteractiveSchedule-1.htm)
- [Master Layout and title-block content](https://help.graphisoft.com/AC/18/INT/AC18Help/04_Documentation/04_Documentation-96.htm)
- [Drawings placed on Layouts](https://help.graphisoft.com/AC/25/INT/_AC25_Help/070_Documentation/070_Documentation-94.htm)
- [Library Part element types](https://help.graphisoft.com/AC/18/INT/AC18Help/Appendix_Tools/Appendix_Tools-21.htm)
- [Hotlinked Modules](https://help.graphisoft.com/AC/18/INT/AC18Help/05_Collaboration/05_Collaboration-64.htm)
- [XREF support for DXF/DWG](https://help.graphisoft.com/AC/26/INT/_AC26_Help/120_Interoperability/120_Interoperability-28.htm)
- [Views, Renovation, and Graphic Overrides](https://help.graphisoft.com/AC/29/INT/_AC29_Help/050_ViewsVB/050_ViewsVB-1.htm)
