import { readFile, writeFile } from "node:fs/promises";
import { registerRevitTools } from "../build/tools/index.js";

process.env.MCP_PROFILE = "full";
const tools = registerRevitTools();
const target = new URL("../../docs/BIM_MCP/reference/tools-index.html", import.meta.url);
let html = await readFile(target, "utf8");
const oldTotal = Number(html.match(/TOOLS INDEX <span class="accent">(\d+) 個/)[1]);
const categoryPattern = /<h3 id="cat-[A-H]" class="cat-header">([A-H]) · ([^<]*?)（\d+ 個）<\/h3>\s*<p class="cat-blurb">([\s\S]*?)<\/p>([\s\S]*?)(?=\s*<h3 id="cat-[A-H]"|\s*<\/section>)/g;
const metadata = new Map();
const categories = new Map();
for (const match of html.matchAll(categoryPattern)) {
    categories.set(match[1], { label: match[2], blurb: match[3] });
    for (const card of match[4].matchAll(/data-tool="([^"]+)"[\s\S]*?<div class="tool-module">([^<]+)<\/div>/g))
        metadata.set(card[1], { category: match[1], module: card[2] });
}
metadata.set("create_walls_from_dwg", { category: "B", module: "cad-wall-tools" });
metadata.set("shape_floor_ramp", { category: "B", module: "framing-floor-tools" });
metadata.set("list_floor_types", { category: "B", module: "framing-floor-tools" });
metadata.set("create_floors_from_framing_regions", { category: "B", module: "framing-floor-tools" });
metadata.set("fill_beam_marks_from_dwg", { category: "B", module: "beam-mark-tools" });
metadata.set("fill_column_marks_from_dwg", { category: "B", module: "column-mark-tools" });
metadata.set("copy_parameter_by_group", { category: "A", module: "parameter-copy-tools" });
metadata.set("align_coordinate_points_to_grids", { category: "A", module: "coordinate-tools" });
metadata.set("create_structural_beam_types", { category: "B", module: "structure-tools" });
metadata.set("fit_beam_to_floor", { category: "B", module: "structure-tools" });
metadata.set("create_beam_additions", { category: "B", module: "structure-tools" });
metadata.set("create_toposolid_from_cad", { category: "B", module: "grading-tools" });
metadata.set("unjoin_wall_joins", { category: "B", module: "wall-tools" });
metadata.set("unjoin_column_joins", { category: "B", module: "wall-tools" });
for (const tool of tools) if (!metadata.has(tool.name)) throw new Error("Missing editorial category: " + tool.name);
const escape = text => String(text).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const badge = tool => tool.annotations?.destructiveHint ? "destructive" : tool.annotations?.readOnlyHint ? "readonly" : "write";
const labels = { readonly: "唯讀", write: "會寫入", destructive: "破壞性" };
const counts = { readonly: 0, write: 0, destructive: 0 };
tools.forEach(tool => counts[badge(tool)]++);
html = html.replace(categoryPattern, (_, key, title, blurb) => {
    const members = tools.filter(tool => metadata.get(tool.name).category === key);
    const cards = members.map(tool => [
        '    <div class="tool-card" id="' + tool.name + '" data-tool="' + tool.name + '" data-search-item data-effect="' + badge(tool) + '" data-search-in="' + key + '">',
        '      <div>',
        '        <div class="tool-name">' + tool.name + '</div>',
        '        <div class="tool-flags"><span class="tool-badge ' + badge(tool) + '">' + labels[badge(tool)] + '</span></div>',
        '        <div class="tool-module">' + escape(metadata.get(tool.name).module) + '</div>',
        '      </div>',
        '      <div>',
        '        <div class="tool-desc">' + escape(tool.description ?? "") + '</div>',
        '      </div>',
        '    </div>'
    ].join("\n")).join("\n");
    return '<h3 id="cat-' + key + '" class="cat-header">' + key + ' · ' + title + '（' + members.length + ' 個）</h3>\n    <p class="cat-blurb">' + blurb + '</p>\n' + cards + '\n';
});
for (const key of categories.keys()) {
    const count = tools.filter(t => metadata.get(t.name).category === key).length;
    html = html.replace(new RegExp('(<tr><td><strong>' + key + '</strong></td><td>[^<]*</td><td class="num">)\\d+'), (_, prefix) => prefix + count);
}
for (const [key, label] of Object.entries(labels))
    html = html.replace(new RegExp('(<strong>' + label + '</strong>（)\\d+( 個）)'), (_, prefix, suffix) => prefix + counts[key] + suffix);
html = html.replace(new RegExp("\\b" + oldTotal + "\\b", "g"), String(tools.length));
await writeFile(target, html, "utf8");
const shared = new URL("../../docs/BIM_MCP/shared.js", import.meta.url);
await writeFile(shared, (await readFile(shared, "utf8")).replace(/desc: "\d+ 個執行層工具/, 'desc: "' + tools.length + ' 個執行層工具'), "utf8");
console.log(JSON.stringify({ tools: tools.length, badges: counts, categories: categories.size }));


