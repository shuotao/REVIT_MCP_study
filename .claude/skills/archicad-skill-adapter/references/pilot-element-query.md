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

## Reference

- [Terminology and boundary map](revit-archicad-terminology.md)
- `domain/element-query-workflow.md`
- `domain/tool-capability-boundary.md`
