/**
 * Revit Clash Viewer — MCP App (extension `io.modelcontextprotocol/ui`).
 *
 * Rendered inline by a UI-capable host when `detect_clashes` runs. Receives the tool's
 * CallToolResult via `ontoolresult`, renders a clash table + summary, and can call back into
 * the server (`colorize_clashes`, `zoom_to_element`) via `callServerTool` over postMessage.
 *
 * This is BROWSER code. It is bundled into a single self-contained HTML file by
 * `scripts/build-apps.mjs` (esbuild) and is intentionally EXCLUDED from the Node `tsc` build
 * (see tsconfig.json). No network access is used — only postMessage to the host.
 */
import { App } from "@modelcontextprotocol/ext-apps";

const app = new App(
  { name: "revit-clash-viewer", version: "1.0.0" },
  {} // McpUiAppCapabilities — defaults
);

const root = document.getElementById("root") as HTMLElement;

/** The most recent raw Revit result (detect_clashes output), used as `clashData` for colorize. */
let lastRaw: unknown = null;

type Dict = Record<string, unknown>;

function el(
  tag: string,
  props: Record<string, unknown> = {},
  children: (Node | string)[] = []
): HTMLElement {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (v == null) continue;
    if (k === "class") node.className = String(v);
    else if (k === "onclick") node.addEventListener("click", v as EventListener);
    else node.setAttribute(k, String(v));
  }
  for (const c of children) node.append(typeof c === "string" ? document.createTextNode(c) : c);
  return node;
}

function status(text: string, kind: "info" | "error" = "info"): HTMLElement {
  return el("div", { class: `status ${kind}` }, [text]);
}

/** Pull an array of clash rows out of an unknown Revit result shape. */
function toRows(data: unknown): Dict[] {
  if (Array.isArray(data)) return data.filter((x) => x && typeof x === "object") as Dict[];
  if (data && typeof data === "object") {
    const d = data as Dict;
    for (const key of ["clashes", "conflicts", "results", "items", "pairs", "list", "data"]) {
      if (Array.isArray(d[key])) return (d[key] as unknown[]).filter((x) => x && typeof x === "object") as Dict[];
    }
    if (d.data && typeof d.data === "object") {
      const inner = toRows(d.data);
      if (inner.length) return inner;
    }
  }
  return [];
}

/** Scalar top-level fields become summary chips (e.g. counts / totals). */
function toSummary(data: unknown, rowCount: number): Record<string, string> {
  const out: Record<string, string> = {};
  if (data && typeof data === "object" && !Array.isArray(data)) {
    for (const [k, v] of Object.entries(data as Dict)) {
      if (typeof v === "number" || typeof v === "string" || typeof v === "boolean") out[k] = String(v);
    }
  }
  out["碰撞列 / rows"] = String(rowCount);
  return out;
}

/** Parse the CallToolResult: our server returns content[0].text = JSON.stringify(revitResult). */
function parseResult(result: unknown): unknown {
  const r = result as { content?: Array<{ type?: string; text?: string }>; structuredContent?: unknown } | null;
  if (r?.structuredContent != null) return r.structuredContent;
  const textBlock = r?.content?.find((c) => c?.type === "text" && typeof c.text === "string");
  if (textBlock?.text) {
    try {
      return JSON.parse(textBlock.text);
    } catch {
      return textBlock.text; // not JSON — show raw text
    }
  }
  return result ?? null;
}

function firstElementId(row: Dict): number | null {
  const keys = Object.keys(row);
  const preferred = keys.find((k) => /element.*id$/i.test(k) && typeof row[k] === "number");
  const anyId = preferred ?? keys.find((k) => /id$/i.test(k) && typeof row[k] === "number");
  return anyId ? (row[anyId] as number) : null;
}

function columnsOf(rows: Dict[]): string[] {
  const cols = new Set<string>();
  for (const r of rows.slice(0, 50)) for (const k of Object.keys(r)) cols.add(k);
  return [...cols].slice(0, 8);
}

function cell(v: unknown): string {
  if (v == null) return "";
  if (typeof v === "number") return Number.isInteger(v) ? String(v) : v.toFixed(2);
  if (typeof v === "object") return JSON.stringify(v);
  return String(v);
}

function render(result: unknown): void {
  const raw = parseResult(result);
  lastRaw = raw;
  const rows = toRows(raw);
  const summary = toSummary(raw, rows.length);

  root.textContent = "";
  root.append(el("div", { class: "hd" }, [el("h1", {}, ["🔍 Revit 碰撞檢視 / Clash Viewer"])]));

  root.append(
    el(
      "div",
      { class: "summary" },
      Object.entries(summary).map(([k, v]) =>
        el("div", { class: "chip" }, [
          el("span", { class: "chip-k" }, [k]),
          el("span", { class: "chip-v" }, [v]),
        ])
      )
    )
  );

  const colorizeBtn = el("button", {
    class: "btn primary",
    onclick: async () => {
      colorizeBtn.setAttribute("disabled", "true");
      colorizeBtn.textContent = "上色中… / colorizing…";
      try {
        await app.callServerTool({ name: "colorize_clashes", arguments: { clashData: lastRaw } });
        colorizeBtn.textContent = "已在 Revit 上色 ✓";
      } catch (err) {
        colorizeBtn.removeAttribute("disabled");
        colorizeBtn.textContent = "在 Revit 上色（重試）/ Colorize (retry)";
      }
    },
  }, ["在 Revit 上色 / Colorize in Revit"]) as HTMLButtonElement;
  root.append(el("div", { class: "actions" }, [colorizeBtn]));

  if (!rows.length) {
    root.append(status(raw == null ? "無法解析碰撞資料。" : "沒有碰撞列可顯示（顯示原始結果）。"));
    if (raw != null) root.append(el("pre", { class: "raw" }, [JSON.stringify(raw, null, 2).slice(0, 5000)]));
    return;
  }

  const cols = columnsOf(rows);
  const thead = el("thead", {}, [
    el("tr", {}, [...cols.map((c) => el("th", {}, [c])), el("th", {}, ["動作 / action"])]),
  ]);
  const tbody = el("tbody");
  rows.slice(0, 500).forEach((row) => {
    const tds = cols.map((c) => el("td", {}, [cell(row[c])]));
    const id = firstElementId(row);
    const actionTd = el("td", {});
    if (id != null) {
      const zoomBtn = el("button", {
        class: "btn small",
        onclick: async () => {
          zoomBtn.setAttribute("disabled", "true");
          try {
            await app.callServerTool({ name: "zoom_to_element", arguments: { elementId: Number(id) } });
            zoomBtn.textContent = "已定位 ✓";
          } catch {
            zoomBtn.removeAttribute("disabled");
            zoomBtn.textContent = "定位 / Zoom";
          }
        },
      }, ["定位 / Zoom"]) as HTMLButtonElement;
      actionTd.append(zoomBtn);
    }
    tbody.append(el("tr", {}, [...tds, actionTd]));
  });
  root.append(el("table", { class: "clash-table" }, [thead, tbody]));
  if (rows.length > 500) root.append(status(`僅顯示前 500 列，共 ${rows.length} 列。`));
}

app.ontoolresult = (params) => {
  // Spec: McpUiToolResultNotification.params IS the CallToolResult.
  render(params);
};

app
  .connect()
  .catch((err: unknown) => {
    root.textContent = "";
    root.append(
      status(
        "無法連線到宿主。此頁需在支援 MCP Apps（io.modelcontextprotocol/ui）的用戶端中開啟。/ " +
          String((err as { message?: string })?.message ?? err),
        "error"
      )
    );
  });
