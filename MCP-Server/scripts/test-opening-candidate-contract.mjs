import assert from "node:assert/strict";
import { clashTools } from "../build/tools/clash-tools.js";
import { withAnnotations } from "../build/tools/annotations.js";

const tool = clashTools.find(({ name }) => name === "scan_opening_candidates");

assert.ok(tool, "scan_opening_candidates must be registered in clashTools");
assert.deepEqual(
  tool.inputSchema.required,
  ["mepSource", "structureSource", "clearanceMm"],
  "the public contract must require both sources and an explicit clearanceMm",
);
assert.equal(tool.inputSchema.properties.clearanceMm.minimum, 0);
assert.equal(tool.inputSchema.properties.maxCount.minimum, 1);
assert.ok(tool.inputSchema.properties.levels, "levels must be available as an optional scope filter");
assert.ok(tool.inputSchema.properties.categories, "categories must be available as an optional scope filter");

const annotated = withAnnotations(tool);
assert.equal(annotated.annotations?.readOnlyHint, true);
assert.equal(annotated.annotations?.destructiveHint, false);

console.log("opening candidate MCP contract: PASS");
