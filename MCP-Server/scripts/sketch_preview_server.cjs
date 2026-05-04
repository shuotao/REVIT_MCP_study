#!/usr/bin/env node
/**
 * sketch_preview_server.js
 *
 * Localhost preview for the sketch-to-revit pipeline.
 * Serves the original sketch + the vectorized preview side-by-side and lets
 * the user click "OK / Redo" — the decision is written to a JSON file the
 * orchestrator skill polls.
 *
 * Usage:
 *   node sketch_preview_server.js \
 *     --original C:/path/sketch.png \
 *     --preview  C:/path/sketch_preview.png \
 *     --geometry C:/path/sketch_geometry.json \
 *     --decision C:/path/decision.json \
 *     [--dxf C:/path/sketch.dxf]              # default: <geometry-dir>/sketch.dxf, used by /api/rescale
 *     [--rescale-script C:/path/rescale_geometry.py]  # default: .claude/skills/plan-sketch-to-dxf/scripts/rescale_geometry.py
 *     [--python python3]                      # default: "python" (or env SKETCH_PREVIEW_PYTHON)
 *     [--port 10003]
 *
 * Endpoints:
 *   GET  /                  → preview UI (HTML)
 *   GET  /original          → original sketch image
 *   GET  /preview           → vectorized preview image
 *   GET  /api/geometry      → geometry.json
 *   GET  /api/state         → { decisionFile, decisionWritten }
 *   POST /api/decision      → write decision.json (action=ok|redo, mode, column_side)
 *   POST /api/rescale       → run rescale_geometry.py to update mmPerPx in-place
 *                             body: { mode: "two_points"|"width"|"height", px_distance?, actual_cm }
 *                             returns: { ok, mmPerPx, bbox_cm: {width, height}, ... }
 */

const http = require("http");
const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

function parseArgs(argv) {
    const args = {};
    for (let i = 2; i < argv.length; i++) {
        const a = argv[i];
        if (a.startsWith("--")) {
            const k = a.slice(2);
            const v = argv[i + 1];
            if (v === undefined || v.startsWith("--")) {
                args[k] = true;
            } else {
                args[k] = v;
                i++;
            }
        }
    }
    return args;
}

const args = parseArgs(process.argv);
const ORIGINAL = args.original;
const PREVIEW = args.preview;
const GEOMETRY = args.geometry;
const DECISION = args.decision;
const DXF = args.dxf || (GEOMETRY ? path.join(path.dirname(GEOMETRY), "sketch.dxf") : null);
const RESCALE_SCRIPT = args["rescale-script"] || path.resolve(
    __dirname,
    "..", "..", ".claude", "skills", "plan-sketch-to-dxf", "scripts", "rescale_geometry.py",
);
const PYTHON_BIN = args.python || process.env.SKETCH_PREVIEW_PYTHON || "python";
const PORT_PRIMARY = parseInt(args.port || "10003", 10);

if (!ORIGINAL || !PREVIEW || !DECISION) {
    console.error("missing required args. need --original, --preview, --decision (and --geometry optional)");
    process.exit(2);
}

for (const [name, p] of [["original", ORIGINAL], ["preview", PREVIEW]]) {
    if (!fs.existsSync(p)) {
        console.error(`[sketch_preview] ${name} not found: ${p}`);
        process.exit(2);
    }
}

const SCRIPT_DIR = __dirname;
const HTML_PATH = path.join(SCRIPT_DIR, "sketch_preview.html");

function contentType(filePath) {
    const ext = path.extname(filePath).toLowerCase();
    return {
        ".png": "image/png",
        ".jpg": "image/jpeg",
        ".jpeg": "image/jpeg",
        ".gif": "image/gif",
        ".webp": "image/webp",
        ".html": "text/html; charset=utf-8",
        ".json": "application/json; charset=utf-8",
    }[ext] || "application/octet-stream";
}

function serveFile(res, filePath, fallbackType) {
    fs.readFile(filePath, (err, data) => {
        if (err) {
            res.writeHead(404, { "Content-Type": "text/plain" });
            res.end(`not found: ${filePath}`);
            return;
        }
        res.writeHead(200, {
            "Content-Type": fallbackType || contentType(filePath),
            "Access-Control-Allow-Origin": "*",
            "Cache-Control": "no-store",
        });
        res.end(data);
    });
}

function handleRequest(req, res) {
    const url = new URL(req.url, `http://${req.headers.host}`);
    const p = url.pathname;

    if (req.method === "GET" && (p === "/" || p === "/index.html")) {
        return serveFile(res, HTML_PATH);
    }
    if (req.method === "GET" && p === "/original") {
        return serveFile(res, ORIGINAL);
    }
    if (req.method === "GET" && p === "/preview") {
        return serveFile(res, PREVIEW);
    }
    if (req.method === "GET" && p === "/api/geometry") {
        if (!GEOMETRY || !fs.existsSync(GEOMETRY)) {
            res.writeHead(404, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: "geometry json not provided or missing" }));
            return;
        }
        return serveFile(res, GEOMETRY, "application/json; charset=utf-8");
    }
    if (req.method === "GET" && p === "/api/state") {
        const exists = fs.existsSync(DECISION);
        res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
        res.end(JSON.stringify({ decisionFile: DECISION, decisionWritten: exists }));
        return;
    }
    if (req.method === "POST" && p === "/api/rescale") {
        return handleRescale(req, res);
    }
    if (req.method === "POST" && p === "/api/decision") {
        let body = "";
        req.on("data", chunk => { body += chunk.toString(); });
        req.on("end", () => {
            try {
                const parsed = JSON.parse(body || "{}");
                const payload = {
                    action: parsed.action,
                    mode: parsed.mode || null,
                    column_side: parsed.column_side || null,
                    timestamp: new Date().toISOString(),
                };
                if (!["ok", "redo"].includes(payload.action)) {
                    res.writeHead(400, { "Content-Type": "application/json" });
                    res.end(JSON.stringify({ error: "action must be 'ok' or 'redo'" }));
                    return;
                }
                fs.writeFileSync(DECISION, JSON.stringify(payload, null, 2), "utf-8");
                console.log(`[sketch_preview] decision written: ${payload.action}`);
                res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
                res.end(JSON.stringify({ ok: true, written: DECISION }));
            } catch (err) {
                res.writeHead(400, { "Content-Type": "application/json" });
                res.end(JSON.stringify({ error: String(err) }));
            }
        });
        return;
    }

    res.writeHead(404, { "Content-Type": "text/plain" });
    res.end("not found");
}

function computeBboxPx(geom) {
    const xs = [], ys = [];
    for (const c of (geom.columns || [])) { xs.push(+c.px); ys.push(+c.py); }
    for (const arr of [geom.magenta_segments || [], geom.cyan_segments || []]) {
        for (const s of arr) { xs.push(+s.sx, +s.ex); ys.push(+s.sy, +s.ey); }
    }
    if (!xs.length) return null;
    const minx = Math.min(...xs), maxx = Math.max(...xs);
    const miny = Math.min(...ys), maxy = Math.max(...ys);
    return { minx, maxx, miny, maxy, width: maxx - minx, height: maxy - miny };
}

function handleRescale(req, res) {
    let body = "";
    req.on("data", chunk => { body += chunk.toString(); });
    req.on("end", () => {
        let parsed;
        try { parsed = JSON.parse(body || "{}"); }
        catch (err) {
            res.writeHead(400, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: "invalid json: " + String(err) }));
            return;
        }

        if (!GEOMETRY || !fs.existsSync(GEOMETRY)) {
            res.writeHead(400, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: "geometry json not provided or missing" }));
            return;
        }
        if (!DXF || !fs.existsSync(DXF)) {
            res.writeHead(400, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: `dxf not found at ${DXF}` }));
            return;
        }
        if (!fs.existsSync(RESCALE_SCRIPT)) {
            res.writeHead(500, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: `rescale_geometry.py not found at ${RESCALE_SCRIPT}` }));
            return;
        }

        const mode = parsed.mode;
        const cliArgs = [
            RESCALE_SCRIPT,
            "--geometry", GEOMETRY,
            "--dxf", DXF,
            "--out-dxf", DXF,  // overwrite in place
        ];

        if (mode === "two_points") {
            const px = parseFloat(parsed.px_distance);
            const cm = parseFloat(parsed.actual_cm);
            if (!isFinite(px) || px <= 0 || !isFinite(cm) || cm <= 0) {
                res.writeHead(400, { "Content-Type": "application/json" });
                res.end(JSON.stringify({ error: "two_points requires positive px_distance and actual_cm" }));
                return;
            }
            // rescale_geometry.py uses the geometry.json refPxDistance to compute mmPerPx from --ref-mm.
            // For arbitrary two-point measurement, we patch geometry.json's refPxDistance to the user's
            // measured pixels first, then call --ref-mm with cm*10 mm. (We restore refPair label too.)
            try {
                const geom = JSON.parse(fs.readFileSync(GEOMETRY, "utf-8"));
                if (!geom.scale) geom.scale = {};
                geom.scale.refPxDistance = px;
                geom.scale.refPair = ["P1", "P2"];
                fs.writeFileSync(GEOMETRY, JSON.stringify(geom, null, 2), "utf-8");
            } catch (err) {
                res.writeHead(500, { "Content-Type": "application/json" });
                res.end(JSON.stringify({ error: "failed to patch geometry.json refPxDistance: " + String(err) }));
                return;
            }
            cliArgs.push("--ref-mm", String(cm * 10));
        } else if (mode === "width") {
            const cm = parseFloat(parsed.actual_cm);
            if (!isFinite(cm) || cm <= 0) {
                res.writeHead(400, { "Content-Type": "application/json" });
                res.end(JSON.stringify({ error: "width requires positive actual_cm" }));
                return;
            }
            cliArgs.push("--target-width-mm", String(cm * 10));
        } else if (mode === "height") {
            const cm = parseFloat(parsed.actual_cm);
            if (!isFinite(cm) || cm <= 0) {
                res.writeHead(400, { "Content-Type": "application/json" });
                res.end(JSON.stringify({ error: "height requires positive actual_cm" }));
                return;
            }
            cliArgs.push("--target-height-mm", String(cm * 10));
        } else {
            res.writeHead(400, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: "mode must be one of: two_points, width, height" }));
            return;
        }

        const proc = spawn(PYTHON_BIN, cliArgs, { windowsHide: true });
        let stdout = "", stderr = "";
        proc.stdout.on("data", d => { stdout += d.toString(); });
        proc.stderr.on("data", d => { stderr += d.toString(); });
        proc.on("error", (err) => {
            res.writeHead(500, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ error: "failed to spawn python: " + String(err), python: PYTHON_BIN }));
        });
        proc.on("close", (code) => {
            if (code !== 0) {
                console.error(`[sketch_preview] rescale failed (code=${code}): ${stderr.trim()}`);
                res.writeHead(500, { "Content-Type": "application/json" });
                res.end(JSON.stringify({ error: `rescale_geometry.py exited ${code}`, stderr: stderr.trim(), stdout: stdout.trim() }));
                return;
            }
            // Read updated geometry.json to report new mmPerPx + bbox
            try {
                const geom = JSON.parse(fs.readFileSync(GEOMETRY, "utf-8"));
                const mmPerPx = geom.scale && geom.scale.mmPerPx;
                const bb = computeBboxPx(geom);
                let bboxMm = null, bboxCm = null;
                if (bb && mmPerPx) {
                    bboxMm = { width: bb.width * mmPerPx, height: bb.height * mmPerPx };
                    bboxCm = { width: bboxMm.width / 10, height: bboxMm.height / 10 };
                }
                console.log(`[sketch_preview] geometry rescaled: mmPerPx=${mmPerPx} (mode=${mode}) bbox_cm=${bboxCm ? `${bboxCm.width.toFixed(1)}x${bboxCm.height.toFixed(1)}` : "?"}`);
                res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
                res.end(JSON.stringify({
                    ok: true,
                    mode: mode,
                    mmPerPx: mmPerPx,
                    bbox_mm: bboxMm,
                    bbox_cm: bboxCm,
                    refPxDistance: geom.scale ? geom.scale.refPxDistance : null,
                    refMmDistance: geom.scale ? geom.scale.refMmDistance : null,
                    stdout: stdout.trim(),
                }));
            } catch (err) {
                res.writeHead(500, { "Content-Type": "application/json" });
                res.end(JSON.stringify({ error: "rescale ok but failed to re-read geometry.json: " + String(err) }));
            }
        });
    });
}

function tryListen(port, attemptsLeft) {
    const server = http.createServer(handleRequest);
    server.on("error", (err) => {
        if (err && err.code === "EADDRINUSE" && attemptsLeft > 0) {
            console.error(`[sketch_preview] port ${port} busy, trying ${port + 1}`);
            tryListen(port + 1, attemptsLeft - 1);
        } else {
            console.error("[sketch_preview] listen failed:", err);
            process.exit(1);
        }
    });
    server.listen(port, "127.0.0.1", () => {
        console.log(`[sketch_preview] http://localhost:${port}`);
        console.log(`[sketch_preview] decision file: ${DECISION}`);
    });
}

tryListen(PORT_PRIMARY, 5);
