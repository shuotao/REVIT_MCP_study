# /qaqc - Repository QA/QC

Run the repository QA/QC gate and report failures with actionable file paths.

## Primary Command

On Windows:

```powershell
.\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy
```

For release or deployment validation:

```powershell
.\scripts\verify-qaqc.ps1 -Version 2024
```

Use `-SkipBuild -SkipDeploy` for documentation-only work unless the user asks for a full build.

## Expected Coverage

The QA/QC script verifies:

1. File structure integrity.
2. Forbidden legacy files.
3. AI rule redirects.
4. Stale path references.
5. README navigation completeness.
6. Build configuration and `.addin` safety.
7. Optional C# and MCP Server builds.
8. Optional deployed add-in checks.
9. Cross-document Skill / Domain / Tool count alignment.
10. Domain table forward and reverse coverage.
11. BIM_MCP source-link resolution.
12. Local Markdown link rot.
13. Domain frontmatter quality.
14. AI/human/shared document audience classification.
15. Mojibake risk in canonical AI and human-facing docs.

## Interpretation

- `PASS`: the check is satisfied.
- `FAIL`: fix before merging or delivering the work.
- `SKIP`: intentionally skipped by command flags or unavailable local state.
- `WARN`: informational risk; report it when relevant, but it does not fail the run.

## Reporting Format

When QA/QC fails, report:

```text
QA/QC failed.
- Phase: <phase>
- File: <path>
- Problem: <short explanation>
- Fix: <specific next action>
```

Do not paste the entire script output unless the user asks. Summarize the failing checks and first relevant file/line references.

## Non-Windows Fallback

If PowerShell is unavailable:

1. State that the canonical script is Windows PowerShell.
2. Manually inspect the edited files.
3. Run any available equivalent checks such as `npm run build`.
4. Tell the user which canonical checks were not run.
