---
name: qa-review
description: "Run quality assurance checks on the Revit project and MCP system. TRIGGER when: user mentions QA, quality check, verification, consistency check, project review, path maintenance, or system health check. Tools: get_all_sheets, get_detail_components, get_all_views, query_elements_with_filter."
---

# QA Review & Project Quality Check

## Checklist Categories

### 1. Sheet & Numbering Consistency
- [ ] All sheets have proper numbering format (`[Code]-[Type][Serial]`)
- [ ] No `-1` suffix duplicates
- [ ] Sheet names match content
- [ ] All views placed on appropriate sheets

### 2. Detail Component Integrity
- [ ] All detail headers have matching parameters
- [ ] No orphan types (types with zero instances)
- [ ] Type names follow `{SheetNumber}-{SheetName}-{DetailName}` convention

### 3. View Organization
- [ ] All views assigned to sheets or marked as working views
- [ ] View templates applied consistently
- [ ] No duplicate view names

### 4. Parameter Consistency
- [ ] Fire rating parameters populated on all relevant walls
- [ ] Room names and numbers complete
- [ ] Level assignments correct

### 5. MCP System Health
- [ ] WebSocket connection on port 8964 responsive
- [ ] All registered tools returning valid responses
- [ ] Build artifacts match source code version

## Execution

Run checks sequentially:
1. `get_all_sheets` → verify numbering
2. `get_detail_components` → verify header integrity
3. `get_all_views` → verify organization
4. `query_elements_with_filter` → spot-check parameters
5. Report findings with pass/fail summary

## Path Maintenance

When modifying file paths or references:
- Verify all cross-references in domain files still resolve
- Check CLAUDE.md trigger keyword table is up to date
- Confirm skill descriptions match current tool availability

## Reference

See `domain/qa-checklist.md`, `domain/path-maintenance-qa.md` for full checklists.
