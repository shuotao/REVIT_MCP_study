/**
 * MCP Apps (extension `io.modelcontextprotocol/ui`, spec 2026-01-26) — server-side wiring.
 *
 * We integrate manually on the low-level `Server` (this project does not use the high-level
 * `McpServer`, so the `registerAppTool` / `registerAppResource` helpers from
 * `@modelcontextprotocol/ext-apps/server` do not apply). Concretely we:
 *   1. advertise the UI HTML via `resources/list` + `resources/read` (see src/index.ts), and
 *   2. tag the associated tool with `_meta.ui.resourceUri` so a UI-capable host renders it.
 *
 * All of this is ADDITIVE: hosts that do not support the `ui` extension ignore `_meta.ui`
 * and the extra resource, and every tool keeps working as plain text.
 */
import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { Tool } from "@modelcontextprotocol/sdk/types.js";

/** MCP Apps HTML resource MIME type (exact string required by the spec). */
export const APP_RESOURCE_MIME = "text/html;profile=mcp-app";

/** Tool name -> UI resource URI. Only tools listed here render an interactive App. */
export const APP_TOOL_UI: Record<string, string> = {
  detect_clashes: "ui://clash-viewer/index.html",
};

interface AppResourceDef {
  name: string;
  /** Built HTML path, relative to this compiled module (build/apps/register-apps.js). */
  file: string;
}

const APP_RESOURCES: Record<string, AppResourceDef> = {
  "ui://clash-viewer/index.html": {
    name: "Revit Clash Viewer",
    file: "./clash-viewer/index.html",
  },
};

/** Returns true if `uri` is a registered app UI resource. */
export function isAppResource(uri: string): boolean {
  return Object.prototype.hasOwnProperty.call(APP_RESOURCES, uri);
}

/** `resources/list` entries for the app UIs. */
export function listAppResources() {
  return Object.entries(APP_RESOURCES).map(([uri, def]) => ({
    uri,
    name: def.name,
    mimeType: APP_RESOURCE_MIME,
    _meta: { ui: {} },
  }));
}

/**
 * `resources/read` for a `ui://` app resource.
 * Returns `null` if `uri` is not an app resource (caller should fall through / 404).
 * Throws a descriptive error if the resource is registered but its HTML was not built.
 */
export function readAppResource(uri: string) {
  const def = APP_RESOURCES[uri];
  if (!def) return null;

  let text: string;
  try {
    const path = fileURLToPath(new URL(def.file, import.meta.url));
    text = readFileSync(path, "utf8");
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    throw new Error(
      `App resource "${uri}" is registered but not built. Run "npm run build" (builds the app bundle). Cause: ${msg}`
    );
  }

  return {
    contents: [
      {
        uri,
        mimeType: APP_RESOURCE_MIME,
        text,
        _meta: { ui: {} },
      },
    ],
  };
}

/**
 * Attach `_meta.ui.resourceUri` to a tool that has an associated App UI.
 * No-op (returns the tool unchanged) for tools without a UI. Never overwrites an existing
 * `_meta.ui.resourceUri`. Additive only — safe for old clients.
 */
export function withAppUi(tool: Tool): Tool {
  const uri = APP_TOOL_UI[tool.name];
  if (!uri) return tool;

  const meta = ((tool as { _meta?: Record<string, unknown> })._meta) ?? {};
  const existingUi = (meta.ui as Record<string, unknown> | undefined) ?? {};
  if (typeof existingUi.resourceUri === "string") return tool;

  return {
    ...tool,
    _meta: { ...meta, ui: { ...existingUi, resourceUri: uri } },
  } as Tool;
}
