# Archicad Pilot: Room and Zone Numbering

## Scope

Translate the originating `room-numbering` method from Revit Rooms to Archicad Zones. Preserve target Story selection, top-to-bottom then left-to-right ordering, dry-run review, conflict checks, explicit write approval, and post-write verification.

This is a guarded write pilot. It does not claim that Archicad property writes are atomic or equivalent to one Revit transaction.

## Backend Route

- Revit selected: return to `.claude/skills/room-numbering/SKILL.md` and use `renumber_rooms_by_level` as documented.
- Archicad selected: continue below and use Zone GUIDs only.
- Target ambiguous: ask which application and project to use before reading room/Zone data.

## Archicad Workflow

1. Anchor one live project and port with `discovery_list_active_archicads`.
2. Discover a command that lists Zones for the requested Story. Confirm the actual Story name rather than mapping a Revit Level string by assumption.
3. Discover details, bounding boxes, or geometry sufficient to calculate a stable center point for each Zone. Confirm coordinate units and axes.
4. Discover the exact writable property that represents the project team's Zone number. Do not assume its display label or identifier.
5. Read all current candidate values and detect duplicates both inside and outside the target Story.
6. Build a dry-run table using the Domain ordering rule: descending Y groups, then ascending X within each group. Keep the requested prefix and increment only the trailing number.
7. Show Zone GUID, current number, proposed number, Story, center point, skipped Zones, and conflicts. Obtain explicit approval before writing.
8. Discover and dispatch the property-write schema. If the runtime offers no atomic batch/rollback guarantee, state that limitation before the write and use the smallest verifiable batch.
9. Re-read every affected Zone on the same port. Report successful, unchanged, failed, and conflicting GUIDs separately.

## Observed Capability Hints

Use these only as discovery hints:

| Intent | Observed command hint |
|---|---|
| List Zones | `elements_get_elements_by_type` |
| Read Zone details | `elements_get_details_of_elements` |
| Read current number property | `properties_get_property_values_of_elements` |
| Write proposed number property | `properties_set_property_values_of_elements` |

## Stop Conditions

- Story resolution is ambiguous.
- Zone center points, coordinate units, or the number property cannot be verified.
- Proposed values conflict with Zones outside the target Story.
- The selected Archicad instance changes between dry-run and write.
- The user has not approved the exact dry-run table.

## Live-Test Evidence

```text
backend: archicad
canonical_skill: room-numbering
domain_method: domain/room-numbering-workflow.md
adapter_reference: pilot-room-numbering.md
project_port: <current port>
target_story: <resolved Story>
dry_run_count: <count>
write_guarantee: atomic | non-atomic | unknown
verification: <success/failed/unchanged counts>
```

## Reference

- [Terminology and boundary map](revit-archicad-terminology.md)
- `domain/room-numbering-workflow.md`
- `domain/session-context-guard.md`
