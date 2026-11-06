# Revit to Archicad Terminology and Boundary Map

## Contents

1. Mapping rules
2. Core model terms
3. Documentation and organization terms
4. Data and collaboration terms
5. Which Archicad system owns this term
6. Units and the numeric contract
7. Tool orchestration mapping
8. Stop conditions
9. Official terminology sources

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
| Internal feet | Two different Archicad layers, see [Units and the numeric contract](#units-and-the-numeric-contract) | No 1:1 | Never apply Revit unit conversion automatically, and never assume a returned number's unit. |

## Which Archicad system owns this term

Revit collapses almost all element data into "parameters". Archicad splits the same ground across
three systems that are queried by different commands and follow different rules. Deciding which one
owns the user's term is the first step of any translation; guessing produces empty results that
look like missing data.

| System | What it holds | How it is reached |
|---|---|---|
| **Property** | Per-element data values, both built-in and user-defined | Resolve name to id, then read values |
| **Classification** | A separate semantic taxonomy the element is filed under | Classification systems and items |
| **Attribute** | Project-wide named resources: Layer, Line, Fill, Composite, Surface, Profile, Pen Table, Zone Category, MEP System, Building Material | Attribute commands, by attribute type |
| **Library Part parameter** | GDL parameters of Objects, Doors, Windows, Lamps | GDL parameter commands |

Rules that have no Revit analogue:

- **Property availability is driven by classification.** A property can exist in the project and
  still be unavailable on a given element because of how that element is classified. An empty
  property value is therefore ambiguous: it can mean "no value" or "not available here". Resolve
  which, rather than reporting a blank.
- **Built-in and user-defined properties are identified differently.** A built-in property is
  identified by a single non-localized name. A user-defined property is identified by a two-part
  localized name: the group name and the property name. A one-part name will not resolve a
  user-defined property.
- **Never type a property name by hand.** List the available properties, resolve the name to its
  identifier, then read values by identifier. This is the Archicad form of the
  `domain/element-query-workflow.md` rule that forbids guessing parameter names.
- **An attribute is not a property.** Building Material and Surface are attributes, so questions
  about materials are answered from the attribute side, not by looking for a material property.

## Units and the numeric contract

Archicad returns numbers through two layers that do not agree with each other. This is Archicad's
own design, not an artifact of any wrapper version.

| Layer | What it returns | Follows |
|---|---|---|
| **Property layer** | Display *strings*, which may embed a unit symbol | The project's **calculation units** |
| **Geometry layer** | Typed numbers | Raw SI: metres, square metres, cubic metres, radians |

Observed on one wall in a project whose calculation units were Meter with 2 decimals and
DecimalDegree for angle:

| | Slant angle | Reference line length | Thickness |
|---|---|---|---|
| Property layer | `"90°"` | `"18.96"` | — |
| Geometry layer | `1.570796327` | — | `0.2` |

`"90°"` and `1.570796327` are the same angle. The property layer had already converted radians to
degrees and appended the symbol, because the project asked for degrees.

Consequences, all of which produce silently wrong numbers rather than errors:

- **Read the project's calculation units before converting anything.** The same wall returns
  `"18.96"` in a metre project and `"1896"` in a centimetre one, with no error and no warning.
- **Do not parse a property string as a bare number.** It can carry a unit symbol, and its decimal
  places come from a project setting rather than from the value.
- **Do not mix the two layers in one calculation.** A length from the property layer and a
  thickness from the geometry layer are not necessarily in the same unit.
- **Revit-side Domains carry Revit-side units.** The takeoff Domains in `domain/` compute in
  millimetres. Feeding an Archicad metre value into one of those formulas is a factor-of-1000
  error that every downstream total will inherit without complaint.
- **Archicad working units are a third setting again.** They govern the drafting interface, no
  command to read them was found, and nothing observed here follows them. A user saying "my project
  is in centimetres" is usually describing working units, which is not what the API answers with.

Report the unit basis alongside any quantity that leaves this adapter. A number without its unit
basis is not evidence.

## Tool orchestration mapping

| Revit-oriented Skill step | Archicad adapter step |
|---|---|
| Call a named Revit MCP tool | Resolve the intent against the current runtime, then dispatch. The discovery mechanism differs by wrapper version — see [archicad-runtime-facts.md](archicad-runtime-facts.md). |
| Pass an ElementId | Pass a GUID returned by the selected Archicad instance. |
| Pass a category name | Inspect whether the command expects element type, classification, or another enum. |
| Use current Revit view | Discover the relevant Navigator/view command and anchor current Archicad state. |
| Mutate then trust success | Re-read affected GUIDs and verify changed fields. |
| Reuse a previous model result | Re-anchor project/port and fetch current state in this turn. |
| Read a numeric parameter value | Establish the unit basis first; property values and geometry values are in different units. |

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
