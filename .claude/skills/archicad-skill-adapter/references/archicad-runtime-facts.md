# Archicad MCP Runtime Facts

> **維護者補註（收編時新增，非貢獻者原文）**
>
> Every runtime observation in this file was gathered by external contributor @Archwiz-boss in
> their own environment and submitted via PR #123 (`REVIT_MCP_study` issue #98). This repository
> hosts no Archicad instance and has not reproduced or independently verified any of it. Where the
> text below says "this repository," it refers to the fork the contributor was writing from at the
> time (`Archwiz-boss/BIM_MCP_study`), not `REVIT_MCP_study`. See `log/2026-08.md` for the adoption
> record and verification scope.

## What this file is, and when to distrust it

This records what the Archicad MCP runtime actually did when driven from this repository, as
opposed to what its schemas and descriptions claim. Everything here has a shelf life: the
wrapper, the Tapir Add-On and Archicad itself ship independently, so a fact that held for one
combination can be wrong for the next.

| Verified against | Value |
|---|---|
| Wrapper | `tapir-archicad-mcp` `0.4.3` and `0.5.3` (both observed; differences called out) |
| Archicad | 28 |
| Tapir Add-On | 1.5.8 |
| Date | 2026-08-19 |
| Project | one solo project, metric calculation units |

`docs/integrations/archicad-mcp.md` currently pins `0.4.3`. `0.5.3` was tested separately and is
**not** the pinned version; the sections below say which one they describe.

Treat every command name here as a search hint, never as a contract. Re-discover before dispatch,
exactly as `SKILL.md` requires. Nothing in this file removes that obligation.

## Discovery works differently on each version

**`0.5.x` — two-step, no search.**

| Tool | Behaviour |
|---|---|
| `archicad_list_commands` | Takes no arguments. Returns the full command inventory with one-line descriptions. |
| `archicad_get_command_schema` | Takes `command_name`. Returns that command's exact input schema. |
| `archicad_call_tool` | Dispatch. Its own description now requires `archicad_get_command_schema` first. |

Because there is no query, there are no false negatives. List everything, then read the schema of
the command you picked.

**`0.4.3` — one-step, and the search is not what it claims.**

`archicad_discover_tools` took a query string and its description advertised semantic search. It
behaved as a **case-insensitive literal substring match over command description text**:

- `get the currently selected elements` — the tool's own documented example — returned `[]`.
- `all elements` matched `CreateWalls` and `ModifyWalls`, because their descriptions contain
  `Wall elements`, i.e. the substring `all elements`.
- Broad single words appeared to cap at 10 results ordered by command name.

On that version, query short noun phrases likely to appear verbatim in a description
(`given type`, `property names`, `details of`), and never read an empty result as evidence that a
capability is absent.

`archicad_discover_tools` **does not exist on `0.5.3`**. `SKILL.md` step 4 and its tool table still
name it; that instruction cannot be followed on the newer wrapper.

## Discovery silently conflates two different failures

`discovery_list_active_archicads` returns a bare `{"result": []}` when the Tapir Add-On is not
loaded — the same response as when no Archicad is running at all. Observed with Archicad running
and port 19723 listening: an unloaded Add-On still produced an empty array with no diagnostic.

**Before concluding that no instance is available, confirm the Tapir Add-On is loaded.** Tracked
upstream as [SzamosiMate/tapir-archicad-MCP#11](https://github.com/SzamosiMate/tapir-archicad-MCP/issues/11).

## Known defect: `elements_get_details_of_elements` is unusable

A single Wall GUID fails validation on both versions tested:

| Wrapper | Tapir Add-On | Result |
|---|---|---|
| `0.4.3` | 1.5.8 | 431 validation errors |
| `0.5.3` | 1.5.8 | 522 validation errors |

The generated response models mark every per-type detail variant `additionalProperties: false`, so
fields the Add-On has since added are rejected rather than ignored. Because the result is a union,
each unmatched field is reported once per variant, so the error count tracks the number of
supported variants rather than how far behind the schema is — `0.5.3` reports *more* errors while
being *less* out of date.

`0.5.3` did narrow the gap: `flipped` and `floorPlanPolygons` are accepted where `0.4.3` rejected
them. Tapir 1.5.8 then added the next batch, currently rejected: `referenceLineLocation`,
`profileType`, `slantAlpha`, `slantBeta`, `topOffset`, `relativeTopStory`, `zoneRel`, `visibility`,
`isAutoOnStoryVisibility`, `referenceMaterial`, `oppositeMaterial`, `sideMaterial`, `cutFillPen`,
`cutFillBackgroundPen`.

Tracked upstream as
[SzamosiMate/tapir-archicad-MCP#24](https://github.com/SzamosiMate/tapir-archicad-MCP/issues/24).

**Consequence for planning.** Archicad does return `flipped` and `referenceLineLocation` — the
wrapper discards them. Any assessment that concluded "Archicad exposes no equivalent of Revit's
`Wall.Flipped`" was reading a wrapper defect as an API gap. The data exists; it is currently
unreachable through this command. Do not plan a wall-orientation workflow around
`elements_get_details_of_elements` until the upstream issue closes, and do not record the absence
of exterior-side evidence as a permanent Archicad limitation.

## Pagination

- Page size is 100. `next_page_token` is base64 of the integer offset (`MTAw` = 100).
- Absence of `next_page_token` is the only end-of-list signal, so a complete count is
  `offset_of_final_page + len(final page)`.
- On `0.4.3`, an unscoped `Wall` walk succeeded for 16 consecutive pages (1600 GUIDs) and then
  returned `Pagination session expired. Please start a new request.` Tokens also failed when
  reused a few minutes later in a separate turn with no intervening pages, which points at a
  **time-based session TTL rather than a page-count limit**.
- On `0.5.3`, a token survived a large intervening call. The walk was **not** run to completion, so
  whether the TTL still exists is unresolved.

**Working rule regardless of version:** bound the scope with a server-side filter first, then
exhaust pagination inside that scope, and report the scope alongside the count. A count without its
scope is not evidence.

## Command inventory

`archicad_list_commands` on `0.5.3` returned roughly 250 commands. Do not copy that list into a
Skill — run the command. The families below exist so an application-neutral intent can be pointed
at the right area quickly.

| Family | Covers |
|---|---|
| `app_*` | Active window, Add-On version, special folders, alerts |
| `attributes_*` | Layer, Line, Fill, Composite, Surface, Profile, Pen Table, Zone Category, MEP System, Building Material; folders; physical properties |
| `classifications_*` | Systems, items, per-element classification, availability |
| `components_*` | Element components and their property values |
| `design_options_*` | Design options, sets, combinations (Archicad 29+) |
| `elements_*` | Query, create, modify, delete, relations, collisions, bounding boxes, GDL parameters, highlight, selection |
| `favorites_*` | Create, apply, rename, import/export Favorites |
| `grouping_*` | Groups and Suspend Groups mode |
| `ifc_*` | IFC ids, types, properties, file operations |
| `issues_*` | Issues and BCF import/export |
| `keynotes_*` | Keynote tree, items, folders, labels (Archicad 28+) |
| `layout_*`, `navigator_*` | Layout Book, views, drawings, sections, publisher sets |
| `library_*` | Loaded libraries, available library parts, embedded library |
| `mep_*` | MEP elements, routing, ports, distribution systems, preference tables (Archicad 28+) |
| `project_*` | Stories, calculation units, geo location, project info, open/save/close |
| `properties_*` | Property definitions, groups, ids, values, availability |
| `revisions_*` | Document revisions, changes, issues |
| `solid_ops_*` | Solid element operation links |
| `teamwork_*` | Reserve, release, send, receive |

### Commands that change existing assessments

These exist on `0.5.3` and did not on `0.4.3`, or were not found by its discovery. Each one
invalidates an earlier "capability gap" conclusion, so check here before recording a new one.

| Command | Why it matters |
|---|---|
| `elements_get_zone_boundaries` | Zone boundaries including connected elements and neighbour zones — the evidence a Zone-based takeoff needs |
| `elements_get_relations_of_elements` | Wall/beam endpoint and reference-line connections, zone boundaries, the zones either side of a window or door, roofs and shells |
| `elements_get_elements_related_to_zones` | Elements grouped by type per Zone |
| `elements_get_subelements_of_hierarchical_elements` | Decomposes Stair and Railing into their subelements |
| `properties_get_all_properties` | All user-defined and built-in properties in one call |
| `properties_get_all_property_ids_of_elements` | Which properties are actually available on given elements |
| `properties_get_property_definition_availability` | Which classification items a property is available for |
| `classifications_get_classification_item_availability` | Which properties a classification item exposes |
| `elements_get_gdl_parameters_of_elements` | Library Part parameters, the nearest thing to Revit type/instance parameters on objects |
| `mep_get_mep_preference_tables` | Circular cross-section preference tables for Piping and Ventilation — the closest Archicad analogue to a Revit pipe segment size catalog |
| `elements_filter_elements`, `elements_get_collisions` | Both referenced by the Wave 2 plan; confirmed present |

## Verified response shapes

Dispatched on `0.5.3` against the test project, so these are observed rather than inferred from
schemas. Re-verify before relying on them; the schema is the contract, this is only what one
project actually returned.

### `elements_get_relations_of_elements` on Zones

Returns `zoneRelations` per input element:

```text
elementsGroupedByType : [{elementType, elements[]}]   Wall, Beam, Column, Door, Window observed
wallParts             : [{elementId, roomEdgeIndex, begDistance, endDistance}]
beamParts             : [{elementId, begDistance, endDistance}]
curtainWallSegmentParts : []
```

`wallParts` is the important one. It states which segment of which wall forms which edge of the
Zone polygon, which is the Archicad counterpart of a Revit room boundary segment. Two consequences:

- A room-side test does not need ray casting. "Does this wall segment have a Zone on one side" is
  answerable directly, and it does not depend on `elements_get_details_of_elements`, which is
  currently broken.
- A perimeter-based takeoff can source its evidence chain here: which walls bound a room, and over
  what length of each.

Distances are geometry-layer values, so metres, and `begDistance` can be negative where the wall
runs past the start of the Zone edge.

**Not every Zone returns relations.** Of seven Zones on the active story, six returned full
relations and one returned an empty structure; `elements_get_zone_boundaries` was also empty for
that Zone. An empty result must be reported as distinct from a Zone that genuinely has no
boundaries, not silently dropped.

### `project_get_hotlinks`

Returns only `[{location}]` — a file path per hotlink node. No module name, and **no GUID
ownership**, so it cannot answer which elements came from which hotlink. Any workflow that needs to
separate host content from hotlinked content cannot get that from this command.

Worth knowing when reading counts from any Archicad project: the test project used here reports 13
hotlink nodes, all pointing at its own file. Element counts therefore include hotlinked content
unless something else excludes it, and an unscoped `Wall` enumeration in this project showed a
visible change of GUID format partway through, consistent with crossing from host elements into
module elements. **State whether a count includes hotlinked content.**

## Element types decompose further than Revit categories

`elements_get_elements_by_type` takes an `ElementType` enum with roughly seventy values. The trap
is not the size of the list, it is that several Revit "single elements" are several Archicad
elements:

- A stair is `Stair` plus `Riser`, `Tread` and `StairStructure`.
- A railing is `Railing` plus a dozen part types (`RailingPost`, `RailingBaluster`, `RailingPanel`,
  `RailingSegment`, and so on).
- A curtain wall is `CurtainWall` plus `CurtainWallSegment`, `Frame`, `Panel`, `Junction`,
  `Accessory`.
- Beams and columns also expose `BeamSegment` and `ColumnSegment`.

Before any count or quantity, decide and state which granularity is being measured, and use
`elements_get_subelements_of_hierarchical_elements` rather than guessing at the parent/child split.
Counting parents and subelements together double-counts; counting only parents loses the parts a
takeoff usually needs.

`ElementFilter` (`IsEditable`, `OnActualFloor`, `IsVisibleByLayer`, `IsVisibleIn3D`, ...) filters by
visibility and editability state. It is **not** a type filter — type selection comes only from the
separate `elementType` argument.

## Re-verification triggers

Re-run the checks in this file when any of these change: the pinned wrapper version, the Tapir
Add-On version, the Archicad major version, or the project's calculation units. Record the new
combination in the table at the top rather than editing the old observations away.

## Reference

- [Terminology and boundary map](revit-archicad-terminology.md)
- [Element query pilot](pilot-element-query.md)
- `domain/tool-capability-boundary.md`
