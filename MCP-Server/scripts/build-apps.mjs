/**
 * Bundle each MCP App (io.modelcontextprotocol/ui) browser entry into a single self-contained
 * HTML file under build/apps/<name>/index.html.
 *
 * The app TS entry imports @modelcontextprotocol/ext-apps (+ deps); esbuild bundles it into one
 * minified IIFE which is injected into the app's template.html at the `/*__APP_BUNDLE__*\/`
 * marker. The result has no external requests, satisfying the MCP Apps single-file / CSP model.
 *
 * Run after `tsc` (see package.json "build"). Requires esbuild (devDependency).
 */
import { build } from "esbuild";
import { readFileSync, writeFileSync, mkdirSync } from "fs";
import { dirname, resolve } from "path";
import { fileURLToPath } from "url";

const here = dirname(fileURLToPath(import.meta.url));
const appsSrc = resolve(here, "../src/apps");
const appsOut = resolve(here, "../build/apps");

const MARKER = "/*__APP_BUNDLE__*/";

/** name = subfolder under src/apps/ that holds app.ts + template.html */
const APPS = ["clash-viewer"];

for (const name of APPS) {
  const entry = resolve(appsSrc, name, "app.ts");
  const templatePath = resolve(appsSrc, name, "template.html");

  const result = await build({
    entryPoints: [entry],
    bundle: true,
    format: "iife",
    platform: "browser",
    target: "es2020",
    minify: true,
    legalComments: "none",
    write: false,
    logLevel: "warning",
  });

  const js = result.outputFiles[0].text;
  const template = readFileSync(templatePath, "utf8");
  if (!template.includes(MARKER)) {
    throw new Error(`[build-apps] ${name}/template.html is missing the ${MARKER} marker`);
  }
  // Function replacer avoids `$`-pattern interpretation in the bundled JS.
  const html = template.replace(MARKER, () => js);

  const outDir = resolve(appsOut, name);
  mkdirSync(outDir, { recursive: true });
  const outFile = resolve(outDir, "index.html");
  writeFileSync(outFile, html, "utf8");
  console.log(`[build-apps] ${name}: ${(js.length / 1024).toFixed(1)} KiB JS -> ${outFile}`);
}
