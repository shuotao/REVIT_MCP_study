# Archicad Pilot: Element Query

## Scope

Apply the originating `element-query` intent to the selected Archicad project while preserving the Domain sequence: explore, align, then extract. This pilot supports element and property reads. Highlighting is optional and must be discovered separately.

## Backend Route

- Revit selected: return to `.claude/skills/element-query/SKILL.md` and use its existing Revit workflow.
- Archicad selected: continue below and keep all GUIDs isolated from Revit ElementIds.
- Target ambiguous: ask which application and project to use.

## Archicad Workflow

1. Anchor the live project with `discovery_list_active_archicads`; retain the selected project and port.
2. Explore by discovering a command that lists the requested Archicad element type. Dispatch only the returned schema and follow pagination until complete.
3. Align by discovering element-detail and property-definition/value commands. Determine whether the user's term maps to element type, classification, property, attribute, or Library Part parameter.
4. Extract by discovering either a server-side filter or a property-value read. Preserve each result's GUID and the exact property identifier used.
5. If highlighting was requested, discover a highlight command, apply it only to current-chain GUIDs, and verify that a compatible clear operation exists.
6. Report the selected project/port, discovered commands, returned count, property source, pagination state, and any approximate mappings.

## Observed Capability Hints

The pinned runtime has exposed commands resembling the following during implementation. These names are search hints only; run discovery and use the returned schema every time.

| Intent | Observed command hint |
|---|---|
| List by element type | `elements_get_elements_by_type` |
| Filter elements | `elements_filter_elements` |
| Read element details | `elements_get_details_of_elements` |
| Read property values | `properties_get_property_values_of_elements` |
| Highlight results | `elements_highlight_elements` |

## Stop Conditions

- The target type or property has more than one plausible Archicad mapping.
- A filter would require guessing localized property names or enum values.
- Pagination cannot be completed or the selected port changes.
- Visualization is requested but no clear/revert path is discoverable.

## Live-Test Evidence

Record these fields so capability use can be distinguished from Skill use:

```text
backend: archicad
canonical_skill: element-query
domain_method: domain/element-query-workflow.md
adapter_reference: pilot-element-query.md
project_port: <current port>
discovered_commands: <names returned by discovery>
result_guids: <count, not fabricated values>
verification: read-only result or highlight cleared
```

### Recorded run 2026-08-18

```text
backend: archicad
canonical_skill: element-query
agy_codex_mirror: .agents/skills/element-query/SKILL.md
domain_method: domain/element-query-workflow.md
adapter_reference: pilot-element-query.md
runtime: tapir-archicad-mcp==0.4.3 (uvx), Archicad 28, Tapir Add-On 1.5.8
project_port: 19723
project: AC28 空白樣板20260709 (solo)
discovered_commands:
  explore: elements_get_elements_by_type, elements_get_all_elements
  align:   elements_get_details_of_elements, properties_get_all_property_names,
           properties_get_property_ids
  extract: properties_get_property_values_of_elements
  visualize (discovered, NOT dispatched): elements_highlight_elements
result_guids: 304 Wall GUIDs, scope elementType=Wall + filters=["OnActualFloor"],
              pagination exhausted (offsets 0/100/200 = 100 each, offset 300 = 4,
              final page returned no next_page_token)
              Unscoped project-wide Wall enumeration reached 1600 GUIDs over 16 pages
              and then failed; see the Stop Conditions section above.
property_source: BuiltIn property definitions resolved by name via GetPropertyIds
  Wall_ReferenceLineLength -> 736276cc-0825-4738-a2e8-cdd740c7f635
  Wall_CenterLength        -> 6651c8de-502e-47f0-9a96-671a3c5255f2
extract_sample: 4 Wall GUIDs from the final explore page, values returned
  (values are display strings formatted by the project's calculation units; see finding 3)
  5cc16bbe-1d8a-4eb6-b5ab-0ed0f4b53770 -> 0.85 / 0.78
  ea3dc95e-2569-42a6-a99f-876288a4ff6a -> 4.70 / 4.63
  1d415f56-5909-44dc-93b6-603cce626832 -> 1.95 / 1.95
  6fd1a710-0de7-4321-9f4d-e741724af798 -> 17.50 / 17.50
identifiers: Archicad GUID only; no Revit ElementId entered or left this chain
verification: read-only; no write command dispatched; no highlight applied,
              so no highlight had to be cleared
unsupported_steps: none required for this read path
```

### Follow-up verification 2026-08-19

Same project and port, read-only, to pin down what the returned numbers actually mean.

```text
project_get_calculation_units ->
  length: Meter,       decimals 2
  area:   SquareMeter, decimals 2
  volume: CubicMeter,  decimals 2
  angle:  DecimalDegree, decimals 0

Same wall c69cf43d-cdf4-4870-a1d4-122b53c0eef0, two different layers:
  property layer  Wall_ReferenceLineLength  = "18.96"
                  Wall_NetInsideSurfaceArea = "56.87"
                  Wall_InsideSlantAngle     = "90°"
  geometry layer  slantAlpha                = 1.570796327
                  begThickness/endThickness = 0.2
```

`"90°"` and `1.570796327` are the same angle. See finding 3.

### Runtime findings

> **Version scope.** All of this was observed on the pinned `tapir-archicad-mcp==0.4.3`
> (released 2026-06-24) against Archicad 28 with Tapir Add-On 1.5.8. That pin is four
> releases behind: `0.5.0`, `0.5.1`, `0.5.2`, `0.5.3` (2026-08-17) have shipped since.
> Findings 1 and 2 are runtime behaviour and may already differ on `0.5.x` — treat them as
> version-specific until re-tested. Findings 3, 4, 6 and 7 describe Archicad's own API
> semantics and are not expected to change with the wrapper version.

1. **(0.4.3, may be fixed upstream)** `archicad_discover_tools` behaves as a case-insensitive
   **literal substring match over command description text**, not the semantic search its
   own tool description advertises. Long application-neutral sentences return `[]`, including
   the tool's own documented example `get the currently selected elements`. The query
   `all elements` matches `CreateWalls`/`ModifyWalls` only because their description contains
   `Wall elements`, i.e. the substring `all elements`. Single broad words appear to cap at
   10 results ordered by command name.
   Practical consequence on this pin: step 4.1's "describe the application-neutral operation"
   produces systematic false negatives. Query short noun phrases likely to appear verbatim in
   a command description (`given type`, `property names`, `all elements`, `details of`), and
   never read an empty result as proof that a capability is absent.
   Note: `0.5.0` advertises a reworked tool-discovery mechanism (upstream issue #28), so this
   finding should be re-tested before any pilot text is changed on account of it.

2. **(0.4.3, unverified on 0.5.x)** Pagination sessions expire. An unscoped
   `elements_get_elements_by_type` walk over `Wall` succeeded for 16 consecutive pages
   (1600 GUIDs) and then returned `Pagination session expired. Please start a new request.`
   Page tokens also do not survive across turns. This triggers the "Pagination cannot be
   completed" Stop Condition above. Bound the scope first with a discovered server-side
   filter (here `filters: ["OnActualFloor"]`), then exhaust pagination within that scope.

3. **Units are a two-layer model, and the two layers disagree.** This is Archicad API
   behaviour, not a wrapper artifact.
   - The **property layer** (`GetPropertyValuesOfElements`) returns *display strings*
     formatted by the project's **calculation units**, and those strings may carry a unit
     symbol: this project returned `"90°"` for `Wall_InsideSlantAngle`.
   - The **geometry layer** returns raw SI typed numbers: the same wall's `slantAlpha` is
     `1.570796327` (radians) and its thickness is `0.2` (metres).
   Consequences for any Domain that carries Revit-side units (the takeoff Domains compute in
   mm): never assume a unit, never `parseFloat` a property string blindly, and call
   `project_get_calculation_units` before converting. A project switched to centimetres would
   return `"1896"` where this one returns `"18.96"`, with no error and no warning.
   Archicad's *working units* are a separate setting again; no command to read them was found,
   and nothing observed here follows them.

4. `GetPropertyValuesOfElements` returns `propertyValuesForElements` positionally and does
   **not** echo the element GUID or the property GUID. The caller must preserve the request
   ordering to keep the GUID-to-value evidence chain intact.

5. **`elements_get_details_of_elements` is unusable on this pin.** A single Wall GUID returned
   431 pydantic validation errors: the generated response models mark every per-type detail
   variant `additionalProperties: false`, so fields the add-on has since added are rejected
   rather than ignored. This is already tracked upstream as
   [SzamosiMate/tapir-archicad-MCP#24](https://github.com/SzamosiMate/tapir-archicad-MCP/issues/24)
   (open since 2026-07-23, reported there for Zone on Tapir 1.5.3 and Wall on 1.5.5).
   Two points matter for this repository:
   - The rejected payload **does** contain `flipped`, `referenceLineLocation`, `profileType`,
     `slantAlpha`/`slantBeta`, the three material overrides and `floorPlanPolygons`. The data
     exists; the wrapper discards it.
   - `0.5.3` regenerates schemas against Tapir **1.5.7**, while this environment runs Tapir
     **1.5.8**, so upgrading the pin may not be sufficient on its own.

6. The `ElementFilter` enum (`IsEditable`, `OnActualFloor`, `IsVisibleByLayer`, ...) filters by
   visibility/editability state, not by element type. Element type selection comes only from
   the separate `elementType` enum.

7. `elements_highlight_elements` carries its own clear path: passing an empty `elements`
   array removes all previously set highlights. The clear operation required by step 5 was
   confirmed from the schema without dispatching any highlight.

## Reference

- [Terminology and boundary map](revit-archicad-terminology.md)
- `domain/element-query-workflow.md`
- `domain/tool-capability-boundary.md`
