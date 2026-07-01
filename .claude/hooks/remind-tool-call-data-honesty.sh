#!/usr/bin/env bash
# Post-tool-use hook: reminds AI of CLAUDE.md "Tool Call Data Honesty" after any MCP tool call.
# Matcher in settings.json: "mcp__.*" — fires for every MCP tool across any server (Revit / Rhino / AutoCAD / future).
# stdout is injected as a system reminder into the next AI inference context.

cat <<'EOF'
[Tool Call Data Honesty Reminder]
The next response MUST NOT contain any concrete identifier (6+ digit ID), enumerated named entity list, count, or external-system type name unless that datum appears in a tool response from THIS turn. If such data is not available, switch to generic language and proactively offer the query (Branch B). Rule is defined in CLAUDE.md > "Tool Call Data Honesty" and supersedes all Skills and subagents.
EOF
exit 0
